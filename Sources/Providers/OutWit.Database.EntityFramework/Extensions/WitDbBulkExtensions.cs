using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using OutWit.Database.AdoNet;

namespace OutWit.Database.EntityFramework.Extensions;

/// <summary>
/// Bulk operations extensions for DbContext when using WitDatabase.
/// Provides high-performance batch operations similar to EFCore.BulkExtensions.
/// </summary>
/// <remarks>
/// These extensions bypass the standard EF Core change tracker for performance,
/// directly executing optimized SQL against the underlying WitDatabase engine.
/// </remarks>
public static class WitDbBulkExtensions
{
    #region BulkInsert

    /// <summary>
    /// Bulk insert entities into the database.
    /// Much faster than AddRange + SaveChanges for large datasets.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="context">The DbContext.</param>
    /// <param name="entities">Entities to insert.</param>
    /// <param name="options">Bulk operation options (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of rows inserted.</returns>
    /// <example>
    /// <code>
    /// var users = Enumerable.Range(1, 10000)
    ///     .Select(i => new User { Name = $"User{i}", Email = $"user{i}@test.com" });
    /// 
    /// int inserted = await context.BulkInsertAsync(users);
    /// 
    /// // With progress reporting for large datasets
    /// var options = new BulkOptions 
    /// { 
    ///     BatchSize = 1000,
    ///     BatchProgress = count => Console.WriteLine($"Inserted {count} rows")
    /// };
    /// int inserted = await context.BulkInsertAsync(users, options);
    /// </code>
    /// </example>
    public static async Task<int> BulkInsertAsync<T>(
        this DbContext context,
        IEnumerable<T> entities,
        BulkOptions? options = null,
        CancellationToken cancellationToken = default) where T : class
    {
        return await Task.Run(() => BulkInsert(context, entities, options, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Bulk insert entities into the database (synchronous).
    /// </summary>
    public static int BulkInsert<T>(
        this DbContext context,
        IEnumerable<T> entities,
        BulkOptions? options = null,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entities);

        var entityType = context.Model.FindEntityType(typeof(T))
            ?? throw new InvalidOperationException($"Entity type {typeof(T).Name} is not part of the model.");

        var tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException($"Entity type {typeof(T).Name} does not have a table name.");

        var connection = GetWitDbConnection(context);
        var engine = connection.Engine
            ?? throw new InvalidOperationException("Connection is not open or engine is not available.");

        // Get column mappings
        var entitiesList = entities as IList<T> ?? entities.ToList();

        var supplied = FindSuppliedGeneratedColumns(context, entityType, entitiesList);
        var columns = GetInsertColumns(entityType, options, supplied).ToList();

        // The one generated key the store fills, if the caller asked for it back. Only a
        // single-column integer key can be answered from the row counter.
        var generatedKey = options?.SetOutputIdentity == true
            ? entityType.FindPrimaryKey()?.Properties.SingleOrDefault(
                p => p.ValueGenerated != ValueGenerated.Never
                     && !supplied.Contains(p)
                     && !p.IsShadowProperty()
                     && (p.ClrType == typeof(int) || p.ClrType == typeof(long)))
            : null;
        var columnNames = columns.Select(c => c.GetColumnName()).ToArray();
        var properties = columns.ToArray();

        // Build parameterized INSERT statement
        var paramNames = columnNames.Select((_, i) => $"@p{i}").ToArray();
        var columnList = string.Join(", ", columnNames.Select(QuoteIdentifier));
        var paramList = string.Join(", ", paramNames);
        var sql = $"INSERT INTO {QuoteIdentifier(tableName)} ({columnList}) VALUES ({paramList})";

        using var stmt = engine.Prepare(sql);

        int totalInserted = 0;
        var batchSize = options?.BatchSize ?? 0;
        var useTransaction = options?.UseTransaction ?? true;
        
        // Start transaction if needed
        bool ownTransaction = useTransaction && engine.CurrentTransaction == null;
        if (ownTransaction)
            engine.Execute("BEGIN TRANSACTION");

        try
        {
            int batchCount = 0;
            foreach (var entity in entitiesList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                stmt.ClearParameters();
                for (int i = 0; i < properties.Length; i++)
                {
                    var value = ReadValue(context, entity!, properties[i]);
                    stmt.SetParameter($"p{i}", value);
                }

                using var result = stmt.Execute(cancellationToken);
                totalInserted += result.RowsAffected;
                batchCount++;

                // SetOutputIdentity promises the generated keys come back on the entities. It used
                // to do the opposite - forcing the key into the insert, so every row sent an
                // explicit zero and the second collided with the first.
                if (generatedKey != null)
                {
                    var rowId = stmt.LastInsertRowId;

                    // Boxed separately on purpose: a conditional over int and long unifies to
                    // long, and the setter then rejects it for an int property.
                    generatedKey.PropertyInfo?.SetValue(
                        entity,
                        generatedKey.ClrType == typeof(int) ? (object)(int)rowId : rowId);
                }

                // Handle batching
                if (batchSize > 0 && batchCount >= batchSize)
                {
                    if (ownTransaction)
                    {
                        engine.Execute("COMMIT");
                        options?.BatchProgress?.Invoke(totalInserted);
                        engine.Execute("BEGIN TRANSACTION");
                    }
                    batchCount = 0;
                }
            }

            if (ownTransaction)
                engine.Execute("COMMIT");
        }
        catch
        {
            if (ownTransaction)
                engine.Execute("ROLLBACK");
            throw;
        }

        return totalInserted;
    }

    #endregion

    #region BulkUpdate

    /// <summary>
    /// Bulk update entities in the database.
    /// Updates all entities based on their primary key.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="context">The DbContext.</param>
    /// <param name="entities">Entities to update.</param>
    /// <param name="options">Bulk operation options (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of rows updated.</returns>
    public static async Task<int> BulkUpdateAsync<T>(
        this DbContext context,
        IEnumerable<T> entities,
        BulkOptions? options = null,
        CancellationToken cancellationToken = default) where T : class
    {
        return await Task.Run(() => BulkUpdate(context, entities, options, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Bulk update entities in the database (synchronous).
    /// </summary>
    public static int BulkUpdate<T>(
        this DbContext context,
        IEnumerable<T> entities,
        BulkOptions? options = null,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entities);

        var entityType = context.Model.FindEntityType(typeof(T))
            ?? throw new InvalidOperationException($"Entity type {typeof(T).Name} is not part of the model.");

        var tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException($"Entity type {typeof(T).Name} does not have a table name.");

        var connection = GetWitDbConnection(context);
        var engine = connection.Engine
            ?? throw new InvalidOperationException("Connection is not open or engine is not available.");

        // Get primary key properties
        var primaryKey = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException($"Entity type {typeof(T).Name} does not have a primary key.");

        var pkProperties = primaryKey.Properties.ToList();
        
        // Get columns to update (exclude PK)
        var updateColumns = GetUpdateColumns(entityType, pkProperties, options).ToList();
        
        if (updateColumns.Count == 0)
            return 0;

        // Build UPDATE statement
        var setClauses = updateColumns.Select((c, i) => $"{QuoteIdentifier(c.GetColumnName())} = @s{i}");
        var whereClauses = pkProperties.Select((p, i) => $"{QuoteIdentifier(p.GetColumnName())} = @pk{i}");
        
        var sql = $"UPDATE {QuoteIdentifier(tableName)} SET {string.Join(", ", setClauses)} WHERE {string.Join(" AND ", whereClauses)}";

        using var stmt = engine.Prepare(sql);

        int totalUpdated = 0;
        var entitiesList = entities as IList<T> ?? entities.ToList();

        foreach (var entity in entitiesList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            stmt.ClearParameters();
            
            // SET parameters
            for (int i = 0; i < updateColumns.Count; i++)
            {
                var value = ReadValue(context, entity!, updateColumns[i]);
                stmt.SetParameter($"s{i}", value);
            }
            
            // WHERE parameters (PK)
            for (int i = 0; i < pkProperties.Count; i++)
            {
                var value = ReadValue(context, entity!, pkProperties[i]);
                stmt.SetParameter($"pk{i}", value);
            }

            using var result = stmt.Execute(cancellationToken);
            totalUpdated += result.RowsAffected;
        }

        return totalUpdated;
    }

    #endregion

    #region BulkDelete

    /// <summary>
    /// Bulk delete entities from the database.
    /// Deletes entities based on their primary key.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="context">The DbContext.</param>
    /// <param name="entities">Entities to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of rows deleted.</returns>
    public static async Task<int> BulkDeleteAsync<T>(
        this DbContext context,
        IEnumerable<T> entities,
        CancellationToken cancellationToken = default) where T : class
    {
        return await Task.Run(() => BulkDelete(context, entities, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Bulk delete entities from the database (synchronous).
    /// </summary>
    public static int BulkDelete<T>(
        this DbContext context,
        IEnumerable<T> entities,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entities);

        var entityType = context.Model.FindEntityType(typeof(T))
            ?? throw new InvalidOperationException($"Entity type {typeof(T).Name} is not part of the model.");

        var tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException($"Entity type {typeof(T).Name} does not have a table name.");

        var connection = GetWitDbConnection(context);
        var engine = connection.Engine
            ?? throw new InvalidOperationException("Connection is not open or engine is not available.");

        // Get primary key properties
        var primaryKey = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException($"Entity type {typeof(T).Name} does not have a primary key.");

        var pkProperties = primaryKey.Properties.ToList();
        
        // Build DELETE statement
        var whereClauses = pkProperties.Select((p, i) => $"{QuoteIdentifier(p.GetColumnName())} = @pk{i}");
        var sql = $"DELETE FROM {QuoteIdentifier(tableName)} WHERE {string.Join(" AND ", whereClauses)}";

        using var stmt = engine.Prepare(sql);

        int totalDeleted = 0;
        var entitiesList = entities as IList<T> ?? entities.ToList();

        foreach (var entity in entitiesList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            stmt.ClearParameters();
            
            // WHERE parameters (PK)
            for (int i = 0; i < pkProperties.Count; i++)
            {
                var value = ReadValue(context, entity!, pkProperties[i]);
                stmt.SetParameter($"pk{i}", value);
            }

            using var result = stmt.Execute(cancellationToken);
            totalDeleted += result.RowsAffected;
        }

        return totalDeleted;
    }

    /// <summary>
    /// Bulk delete all entities matching a predicate.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="context">The DbContext.</param>
    /// <param name="predicate">Predicate to match entities to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of rows deleted.</returns>
    /// <example>
    /// <code>
    /// // Delete all inactive users
    /// int deleted = await context.BulkDeleteAsync&lt;User&gt;(u => u.IsActive == false);
    /// </code>
    /// </example>
    public static async Task<int> BulkDeleteAsync<T>(
        this DbContext context,
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(predicate);

        // Use EF Core's ExecuteDelete which is available in EF Core 7+
        return await context.Set<T>().Where(predicate).ExecuteDeleteAsync(cancellationToken);
    }

    #endregion

    #region BulkInsertOrUpdate (Upsert)

    /// <summary>
    /// Bulk insert or update entities.
    /// If entity exists (by PK), updates it; otherwise inserts it.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="context">The DbContext.</param>
    /// <param name="entities">Entities to insert or update.</param>
    /// <param name="options">Bulk operation options (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of rows affected.</returns>
    public static async Task<int> BulkInsertOrUpdateAsync<T>(
        this DbContext context,
        IEnumerable<T> entities,
        BulkOptions? options = null,
        CancellationToken cancellationToken = default) where T : class
    {
        return await Task.Run(() => BulkInsertOrUpdate(context, entities, options, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Bulk insert or update entities (synchronous).
    /// </summary>
    public static int BulkInsertOrUpdate<T>(
        this DbContext context,
        IEnumerable<T> entities,
        BulkOptions? options = null,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entities);

        var entityType = context.Model.FindEntityType(typeof(T))
            ?? throw new InvalidOperationException($"Entity type {typeof(T).Name} is not part of the model.");

        var tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException($"Entity type {typeof(T).Name} does not have a table name.");

        var connection = GetWitDbConnection(context);
        var engine = connection.Engine
            ?? throw new InvalidOperationException("Connection is not open or engine is not available.");

        // Get primary key properties
        var primaryKey = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException($"Entity type {typeof(T).Name} does not have a primary key.");

        var pkProperties = primaryKey.Properties.ToList();
        var pkColumnNames = pkProperties.Select(p => p.GetColumnName()).ToList();
        var pkPropertyNames = pkProperties.Select(p => p.Name).ToHashSet();

        // For UPSERT, we need to include PK columns in INSERT even if they are auto-generated
        // because ON CONFLICT uses them to detect conflicts
        var insertColumns = GetInsertColumnsForUpsert(entityType, pkPropertyNames, options).ToList();
        var columnNames = insertColumns.Select(c => c.GetColumnName()).ToArray();
        var properties = insertColumns.ToArray();

        // Get update columns (exclude PK)
        var updateColumns = insertColumns.Where(c => !pkColumnNames.Contains(c.GetColumnName())).ToList();

        // Build INSERT ... ON CONFLICT statement
        var paramNames = columnNames.Select((_, i) => $"@p{i}").ToArray();
        var columnList = string.Join(", ", columnNames.Select(QuoteIdentifier));
        var paramList = string.Join(", ", paramNames);
        var conflictColumns = string.Join(", ", pkColumnNames.Select(QuoteIdentifier));
        var updateClauses = updateColumns.Select(c => 
        {
            var colName = QuoteIdentifier(c.GetColumnName());
            return $"{colName} = EXCLUDED.{colName}";
        });

        var sql = $"INSERT INTO {QuoteIdentifier(tableName)} ({columnList}) VALUES ({paramList}) " +
                  $"ON CONFLICT ({conflictColumns}) DO UPDATE SET {string.Join(", ", updateClauses)}";

        using var stmt = engine.Prepare(sql);

        int totalAffected = 0;
        var entitiesList = entities as IList<T> ?? entities.ToList();

        foreach (var entity in entitiesList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            stmt.ClearParameters();
            for (int i = 0; i < properties.Length; i++)
            {
                var value = ReadValue(context, entity!, properties[i]);
                stmt.SetParameter($"p{i}", value);
            }

            using var result = stmt.Execute(cancellationToken);
            totalAffected += result.RowsAffected;
        }

        return totalAffected;
    }

    #endregion

    #region Helpers

    private static WitDbConnection GetWitDbConnection(DbContext context)
    {
        var connection = context.Database.GetDbConnection();
        
        if (connection is not WitDbConnection witDbConnection)
            throw new InvalidOperationException(
                "Bulk operations are only supported when using WitDatabase. " +
                "Ensure you are using UseWitDb() in your DbContext configuration.");

        if (connection.State != System.Data.ConnectionState.Open)
        {
            context.Database.OpenConnection();
        }

        return witDbConnection;
    }

    private static IEnumerable<IProperty> GetInsertColumns(
        IEntityType entityType,
        BulkOptions? options,
        IReadOnlySet<IProperty> suppliedGenerated)
    {
        // Shadow properties are written like any other. Excluding them dropped the column from
        // the insert with nothing reported - and they are not exotic: EF Core creates one for any
        // relationship whose foreign key has no CLR property, which is the default when a
        // navigation is declared without one.
        //
        // A store-generated column is written only when the caller has actually supplied values
        // for it. SetOutputIdentity used to force it into the list unconditionally, so every row
        // carried an explicit zero and the second collided with the first - the option made any
        // insert of more than one row fail. Leaving it out unconditionally is just as wrong the
        // other way: a bulk insert is a loading tool, and an explicitly assigned key has to reach
        // the table.
        var properties = entityType.GetProperties()
            .Where(p => p.ValueGenerated == ValueGenerated.Never || suppliedGenerated.Contains(p));

        // Exclude properties marked as computed
        properties = properties.Where(p => p.GetComputedColumnSql() == null);

        // Apply include/exclude filters
        properties = ApplyPropertyFilters(properties, options);

        return properties;
    }

    private static IEnumerable<IProperty> GetInsertColumnsForUpsert(IEntityType entityType, HashSet<string> pkPropertyNames, BulkOptions? options)
    {
        // Shadow properties are written like any other. Excluding them dropped the column from
        // the insert with nothing reported - and they are not exotic: EF Core creates one for any
        // relationship whose foreign key has no CLR property, which is the default when a
        // navigation is declared without one.
        //
        // A store-generated column is written only when the caller has actually supplied values
        // for it. SetOutputIdentity used to force it into the list unconditionally, so every row
        // carried an explicit zero and the second collided with the first - the option made any
        // insert of more than one row fail. Leaving it out unconditionally is just as wrong the
        // other way: a bulk insert is a loading tool, and an explicitly assigned key has to reach
        // the table.
        var properties = entityType.GetProperties()
            .Where(p => p.ValueGenerated == ValueGenerated.Never);

        // Exclude properties marked as computed
        properties = properties.Where(p => p.GetComputedColumnSql() == null);

        // Always include primary key columns for upsert
        properties = properties.Concat(entityType.FindPrimaryKey()!.Properties);

        // Apply include/exclude filters
        properties = ApplyPropertyFilters(properties, options);

        return properties.Distinct();
    }

    private static IEnumerable<IProperty> GetUpdateColumns(IEntityType entityType, List<IProperty> pkProperties, BulkOptions? options = null)
    {
        var pkNames = pkProperties.Select(p => p.Name).ToHashSet();
        
        var properties = entityType.GetProperties()
            .Where(p => !p.IsShadowProperty())
            .Where(p => p.PropertyInfo != null)
            .Where(p => !pkNames.Contains(p.Name))
            .Where(p => p.GetComputedColumnSql() == null);

        // Apply include/exclude filters
        properties = ApplyPropertyFilters(properties, options);

        return properties;
    }

    private static IEnumerable<IProperty> ApplyPropertyFilters(IEnumerable<IProperty> properties, BulkOptions? options)
    {
        if (options?.PropertiesToInclude is { Count: > 0 } includes)
        {
            var includeSet = includes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            properties = properties.Where(p => includeSet.Contains(p.Name));
        }

        if (options?.PropertiesToExclude is { Count: > 0 } excludes)
        {
            var excludeSet = excludes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            properties = properties.Where(p => !excludeSet.Contains(p.Name));
        }

        return properties;
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

    #endregion

    /// <summary>
    /// The store-generated columns for which the caller has supplied values.
    /// </summary>
    /// <param name="context">The context the entities belong to.</param>
    /// <param name="entityType">The entity type being written.</param>
    /// <param name="entities">The batch about to be inserted.</param>
    /// <returns>The generated properties that carry a value on at least one entity.</returns>
    /// <remarks>
    /// Decided once for the batch, because the INSERT is prepared once. A column is included if any
    /// row assigns it - a batch that mixes assigned and unassigned keys is a caller error either
    /// way, and including the column at least reports it rather than dropping half the values.
    /// </remarks>
    private static IReadOnlySet<IProperty> FindSuppliedGeneratedColumns<T>(
        DbContext context,
        IEntityType entityType,
        IList<T> entities)
        where T : class
    {
        var generated = entityType.GetProperties()
            .Where(p => p.ValueGenerated != ValueGenerated.Never)
            .ToList();

        var supplied = new HashSet<IProperty>();

        foreach (var property in generated)
        {
            var unset = property.ClrType.IsValueType ? Activator.CreateInstance(property.ClrType) : null;

            if (entities.Any(entity => !Equals(ReadValue(context, entity, property), unset)))
            {
                supplied.Add(property);
            }
        }

        return supplied;
    }

    #region Values

    /// <summary>
    /// Reads a property's value in the form the store expects.
    /// </summary>
    /// <param name="context">The context the entity belongs to.</param>
    /// <param name="entity">The entity being written.</param>
    /// <param name="property">The property being read.</param>
    /// <returns>The value to send as a parameter.</returns>
    /// <remarks>
    /// Reflection over PropertyInfo used to do this, which got two things wrong. A shadow property
    /// has no PropertyInfo at all - its value lives in the change tracker - and a property with a
    /// value converter had its raw CLR value sent straight through, so the conversion the model
    /// declares never happened and the value layer refused the type outright.
    ///
    /// The tracker is consulted only for shadow properties: doing it for every property would make
    /// a bulk insert of untracked entities create an entry per row, which is the cost the bulk path
    /// exists to avoid.
    /// </remarks>
    private static object? ReadValue(DbContext context, object entity, IProperty property)
    {
        var value = property.IsShadowProperty()
            ? context.Entry(entity).Property(property.Name).CurrentValue
            : property.GetGetter().GetClrValue(entity);

        var converter = property.GetValueConverter();

        return converter == null ? value : converter.ConvertToProvider(value);
    }

    #endregion
}

/// <summary>
/// Options for bulk operations.
/// </summary>
public class BulkOptions
{
    /// <summary>
    /// Gets or sets whether to set output identity values on entities after insert.
    /// When true, auto-generated primary key values will be read back into entities.
    /// Default is false (auto-increment values are not retrieved for performance).
    /// </summary>
    /// <remarks>
    /// Setting this to true adds overhead as it requires reading LAST_INSERT_ROWID after each insert.
    /// Only enable when you need the generated IDs.
    /// </remarks>
    public bool SetOutputIdentity { get; set; }

    /// <summary>
    /// Gets or sets the batch size for operations.
    /// When greater than 0, entities are processed in batches with transaction commits between batches.
    /// Default is 0 (all entities in single transaction).
    /// </summary>
    /// <remarks>
    /// Use batching for very large datasets to reduce memory pressure and allow progress tracking.
    /// Each batch is committed separately, so partial failures are possible.
    /// </remarks>
    public int BatchSize { get; set; }

    /// <summary>
    /// Gets or sets an action to be called after each batch is processed.
    /// Useful for progress reporting on large bulk operations.
    /// </summary>
    /// <remarks>
    /// The parameter is the cumulative count of processed entities.
    /// Only called when BatchSize > 0.
    /// </remarks>
    public Action<int>? BatchProgress { get; set; }

    /// <summary>
    /// Gets or sets whether to wrap the entire operation in a transaction.
    /// Default is true. Set to false if you're managing transactions externally.
    /// </summary>
    public bool UseTransaction { get; set; } = true;

    /// <summary>
    /// Gets or sets the list of property names to include in the operation.
    /// If null or empty, all applicable properties are included.
    /// </summary>
    /// <remarks>
    /// For BulkUpdate, this controls which columns are updated.
    /// Primary key columns are always included in WHERE clause regardless of this setting.
    /// </remarks>
    public IList<string>? PropertiesToInclude { get; set; }

    /// <summary>
    /// Gets or sets the list of property names to exclude from the operation.
    /// </summary>
    /// <remarks>
    /// For BulkUpdate, this controls which columns are NOT updated.
    /// Primary key columns cannot be excluded from WHERE clause.
    /// </remarks>
    public IList<string>? PropertiesToExclude { get; set; }

}
