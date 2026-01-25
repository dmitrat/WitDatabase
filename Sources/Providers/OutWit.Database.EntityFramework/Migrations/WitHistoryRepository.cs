using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace OutWit.Database.EntityFramework.Migrations;

/// <summary>
/// Repository for managing the migration history table in WitDatabase.
/// </summary>
public sealed class WitHistoryRepository : HistoryRepository
{
    #region Constants

    private const string DEFAULT_TABLE_NAME = "__EFMigrationsHistory";

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitHistoryRepository"/> class.
    /// </summary>
    /// <param name="dependencies">The history repository dependencies.</param>
    public WitHistoryRepository(HistoryRepositoryDependencies dependencies)
        : base(dependencies)
    {
    }

    #endregion

    #region SQL Generation

    /// <inheritdoc/>
    protected override string ExistsSql
    {
        get
        {
            var tableName = TableName;
            // Use COUNT(*) to return a numeric value that EF Core can interpret
            return $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{tableName}'";
        }
    }

    /// <inheritdoc/>
    public override string GetCreateScript()
    {
        var tableName = Dependencies.SqlGenerationHelper.DelimitIdentifier(TableName);
        var migrationIdColumn = Dependencies.SqlGenerationHelper.DelimitIdentifier(MigrationIdColumnName);
        var productVersionColumn = Dependencies.SqlGenerationHelper.DelimitIdentifier(ProductVersionColumnName);

        return $@"CREATE TABLE IF NOT EXISTS {tableName} (
    {migrationIdColumn} VARCHAR(150) PRIMARY KEY NOT NULL,
    {productVersionColumn} VARCHAR(32) NOT NULL
);
";
    }

    /// <inheritdoc/>
    public override string GetCreateIfNotExistsScript()
    {
        return GetCreateScript();
    }

    /// <inheritdoc/>
    public override string GetBeginIfNotExistsScript(string migrationId)
    {
        // WitSQL doesn't support IF NOT EXISTS in a procedural way
        return string.Empty;
    }

    /// <inheritdoc/>
    public override string GetBeginIfExistsScript(string migrationId)
    {
        return string.Empty;
    }

    /// <inheritdoc/>
    public override string GetEndIfScript()
    {
        return string.Empty;
    }

    /// <inheritdoc/>
    public override string GetInsertScript(HistoryRow row)
    {
        var tableName = Dependencies.SqlGenerationHelper.DelimitIdentifier(TableName);
        var migrationIdColumn = Dependencies.SqlGenerationHelper.DelimitIdentifier(MigrationIdColumnName);
        var productVersionColumn = Dependencies.SqlGenerationHelper.DelimitIdentifier(ProductVersionColumnName);
        
        var migrationId = row.MigrationId.Replace("'", "''");
        var productVersion = row.ProductVersion.Replace("'", "''");

        // Use INSERT OR IGNORE to handle case where migration already exists
        return $"INSERT OR IGNORE INTO {tableName} ({migrationIdColumn}, {productVersionColumn}) VALUES ('{migrationId}', '{productVersion}');\n";
    }

    /// <inheritdoc/>
    public override string GetDeleteScript(string migrationId)
    {
        var tableName = Dependencies.SqlGenerationHelper.DelimitIdentifier(TableName);
        var migrationIdColumn = Dependencies.SqlGenerationHelper.DelimitIdentifier(MigrationIdColumnName);
        var escapedMigrationId = migrationId.Replace("'", "''");

        return $"DELETE FROM {tableName} WHERE {migrationIdColumn} = '{escapedMigrationId}';\n";
    }

    #endregion

    #region Abstract Member Implementations

    /// <inheritdoc/>
    public override LockReleaseBehavior LockReleaseBehavior => LockReleaseBehavior.Explicit;

    /// <inheritdoc/>
    public override IMigrationsDatabaseLock AcquireDatabaseLock()
    {
        // WitDatabase uses file-based locking or in-memory isolation
        // For single-user scenarios, we don't need explicit locking
        return new WitMigrationsDatabaseLock(this);
    }

    /// <inheritdoc/>
    public override Task<IMigrationsDatabaseLock> AcquireDatabaseLockAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IMigrationsDatabaseLock>(new WitMigrationsDatabaseLock(this));
    }

    /// <inheritdoc/>
    protected override bool InterpretExistsResult(object? value)
    {
        if (value == null || value == DBNull.Value)
            return false;
        
        // Handle COUNT(*) result which returns a number
        if (value is long longValue)
            return longValue > 0;
        if (value is int intValue)
            return intValue > 0;
        if (value is short shortValue)
            return shortValue > 0;
        
        // Fallback: any non-null value means exists
        return true;
    }

    #endregion

    #region Configuration

    /// <inheritdoc/>
    protected override void ConfigureTable(EntityTypeBuilder<HistoryRow> history)
    {
        base.ConfigureTable(history);
        
        history.Property(h => h.MigrationId).HasColumnType("VARCHAR(150)");
        history.Property(h => h.ProductVersion).HasColumnType("VARCHAR(32)");
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    protected override string TableName => DEFAULT_TABLE_NAME;

    #endregion

    #region Nested Types

    private sealed class WitMigrationsDatabaseLock : IMigrationsDatabaseLock
    {
        private readonly IHistoryRepository m_historyRepository;

        public WitMigrationsDatabaseLock(IHistoryRepository historyRepository)
        {
            m_historyRepository = historyRepository;
        }

        public IHistoryRepository HistoryRepository => m_historyRepository;

        public void Dispose() { }
        
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    #endregion
}
