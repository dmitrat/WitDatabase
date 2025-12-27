using OutWit.Database.Context;
using OutWit.Database.Core.Builder;
using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Parser;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Serializers;
using OutWit.Database.Schema;
using OutWit.Database.Types;
using OutWit.Database.Utils;
using OutWit.Database.Values;

namespace OutWit.Database;

/// <summary>
/// DDL (Data Definition Language) operations for tables in WitSqlEngine.
/// </summary>
public sealed partial class WitSqlEngine
{
    #region Create Table

    /// <summary>
    /// Create a new table.
    /// </summary>
    /// <param name="table">The table definition.</param>
    public void CreateTable(DefinitionTable table)
    {
        m_schema.CreateTable(table);
    }

    #endregion

    #region Drop Table

    /// <summary>
    /// Drop a table.
    /// </summary>
    /// <param name="tableName">The table name to drop.</param>
    public void DropTable(string tableName)
    {
        m_schema.DropTable(tableName);
    }

    #endregion

    #region Rename Table

    /// <summary>
    /// Rename a table.
    /// </summary>
    /// <param name="oldName">Current table name.</param>
    /// <param name="newName">New table name.</param>
    public void RenameTable(string oldName, string newName)
    {
        var table = m_schema.GetTable(oldName)
            ?? throw new InvalidOperationException($"Table '{oldName}' not found");

        // Migrate all data from old table prefix to new table prefix
        var oldPrefix = SchemaCatalog.GetTableDataPrefix(oldName);
        var newPrefix = SchemaCatalog.GetTableDataPrefix(newName);

        var rowsToMove = new List<(long rowId, byte[] data)>();

        // Collect all rows with old prefix using Scan
        foreach (var (key, value) in m_database.Scan(oldPrefix, GetNextPrefix(oldPrefix)))
        {
            var rowId = SchemaCatalog.ParseRowId(key, oldName);
            rowsToMove.Add((rowId, value));
        }

        // Delete old keys and insert new ones
        foreach (var (rowId, data) in rowsToMove)
        {
            var oldKey = SchemaCatalog.CreateRowKey(oldName, rowId);
            var newKey = SchemaCatalog.CreateRowKey(newName, rowId);

            DeleteFromStore(oldKey);
            PutToStore(newKey, data);
        }

        m_schema.RenameTable(oldName, newName);
    }

    #endregion

    #region Add Column

    /// <summary>
    /// Add a column to an existing table.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="column">The column definition to add.</param>
    public void AddColumn(string tableName, DefinitionColumn column)
    {
        var table = m_schema.GetTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' not found");

        // Parse and evaluate default value expression if provided
        WitSqlExpression? defaultExpression = null;
        if (!string.IsNullOrEmpty(column.DefaultValue))
        {
            defaultExpression = WitSql.ParseExpression(column.DefaultValue);
        }

        // Create execution context for evaluating default expression
        var context = new ContextExecution { Database = this };
        var evaluator = new ExpressionEvaluator(context);

        // Check if expression is deterministic (can be evaluated once)
        var isDeterministic = defaultExpression == null || IsDeterministicExpression(defaultExpression);
        WitSqlValue cachedDefaultValue = WitSqlValue.Null;

        if (isDeterministic && defaultExpression != null)
        {
            // Evaluate once for deterministic expressions
            cachedDefaultValue = evaluator.Evaluate(defaultExpression, new WitSqlRow([], []));
        }

        // Migrate existing rows - append new column value
        var prefix = SchemaCatalog.GetTableDataPrefix(tableName);
        var rowsToUpdate = new List<(long rowId, WitSqlValue[] newValues)>();

        foreach (var (key, value) in m_database.Scan(prefix, GetNextPrefix(prefix)))
        {
            var rowId = SchemaCatalog.ParseRowId(key, tableName);
            var existingRow = table.DeserializeRow(value);
            var values = existingRow.Values.ToArray();

            // Calculate default value for this row
            WitSqlValue defaultValue;
            if (defaultExpression == null)
            {
                defaultValue = WitSqlValue.Null;
            }
            else if (isDeterministic)
            {
                defaultValue = cachedDefaultValue;
            }
            else
            {
                // Evaluate per row for non-deterministic expressions (NOW(), NEWGUID(), etc.)
                defaultValue = evaluator.Evaluate(defaultExpression, existingRow);
            }

            // Create new row with additional column
            var newValues = new WitSqlValue[values.Length + 1];
            Array.Copy(values, newValues, values.Length);
            newValues[values.Length] = defaultValue;

            rowsToUpdate.Add((rowId, newValues));
        }

        // Update schema first so serialization uses correct column count
        m_schema.AddColumn(tableName, column);

        // Get updated table definition for serialization
        var updatedTable = m_schema.GetTable(tableName)!;

        // Apply updates
        foreach (var (rowId, newValues) in rowsToUpdate)
        {
            var key = SchemaCatalog.CreateRowKey(tableName, rowId);
            var data = updatedTable.SerializeValuesArray(newValues);
            PutToStore(key, data);
        }
    }

    /// <summary>
    /// Checks if an expression is deterministic (returns same value for every call).
    /// Non-deterministic functions: NOW(), NEWGUID(), RANDOM(), etc.
    /// </summary>
    private static bool IsDeterministicExpression(WitSqlExpression expression)
    {
        return expression switch
        {
            WitSqlExpressionLiteral => true,
            WitSqlExpressionFunctionCall func => IsDeterministicFunction(func),
            WitSqlExpressionBinary bin => IsDeterministicExpression(bin.Left) && IsDeterministicExpression(bin.Right),
            WitSqlExpressionUnary unary => IsDeterministicExpression(unary.Operand),
            WitSqlExpressionCase caseExpr => IsDeterministicCase(caseExpr),
            WitSqlExpressionCast cast => IsDeterministicExpression(cast.Expression),
            WitSqlExpressionIif iif => IsDeterministicExpression(iif.Condition) 
                                       && IsDeterministicExpression(iif.TrueValue) 
                                       && IsDeterministicExpression(iif.FalseValue),
            WitSqlExpressionColumnRef => true, // Column refs are deterministic per row
            WitSqlExpressionParameter => true, // Parameters don't change per row
            _ => false // Conservative: unknown expressions treated as non-deterministic
        };
    }

    private static bool IsDeterministicFunction(WitSqlExpressionFunctionCall func)
    {
        // Non-deterministic functions
        var nonDeterministicFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "NOW", "CURRENT_TIMESTAMP", "CURRENT_DATE", "CURRENT_TIME",
            "NEWGUID", "NEWUUID", "RANDOM",
            "NEXTVAL", "INCREMENT", "LAST_INSERT_ROWID"
        };

        if (nonDeterministicFunctions.Contains(func.FunctionName))
            return false;

        // Check arguments recursively
        return func.Arguments?.All(IsDeterministicExpression) ?? true;
    }

    private static bool IsDeterministicCase(WitSqlExpressionCase caseExpr)
    {
        if (caseExpr.Operand != null && !IsDeterministicExpression(caseExpr.Operand))
            return false;

        foreach (var when in caseExpr.WhenClauses)
        {
            if (!IsDeterministicExpression(when.When) || !IsDeterministicExpression(when.Then))
                return false;
        }

        if (caseExpr.ElseResult != null && !IsDeterministicExpression(caseExpr.ElseResult))
            return false;

        return true;
    }

    #endregion

    #region Drop Column

    /// <summary>
    /// Drop a column from an existing table.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name to drop.</param>
    public void DropColumn(string tableName, string columnName)
    {
        var table = m_schema.GetTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' not found");

        // Get column ordinal to remove
        var colOrdinal = table.GetOrdinal(columnName);
        if (colOrdinal < 0) return;

        // Update all existing rows - remove the column value
        MigrateExistingRows(tableName, table, values =>
        {
            var newValues = new WitSqlValue[values.Length - 1];
            for (int i = 0, j = 0; i < values.Length; i++)
            {
                if (i != colOrdinal)
                    newValues[j++] = values[i];
            }
            return newValues;
        });

        m_schema.DropColumn(tableName, columnName);
    }

    #endregion

    #region Rename Column

    /// <summary>
    /// Rename a column in a table.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="oldColumnName">Current column name.</param>
    /// <param name="newColumnName">New column name.</param>
    /// <remarks>Column data is stored by ordinal, not by name - no data migration needed.</remarks>
    public void RenameColumn(string tableName, string oldColumnName, string newColumnName)
    {
        m_schema.RenameColumn(tableName, oldColumnName, newColumnName);
    }

    #endregion

    #region Alter Column Type

    /// <summary>
    /// Change a column's data type.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name.</param>
    /// <param name="newType">The new data type.</param>
    public void AlterColumnType(string tableName, string columnName, WitDataType newType)
    {
        var oldTable = m_schema.GetTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' not found");

        var columnIndex = -1;
        for (int i = 0; i < oldTable.Columns.Count; i++)
        {
            if (oldTable.Columns[i].Name.Equals(columnName, StringComparison.OrdinalIgnoreCase))
            {
                columnIndex = i;
                break;
            }
        }

        if (columnIndex == -1)
            throw new InvalidOperationException($"Column '{columnName}' not found in table '{tableName}'");

        // Read all existing data with OLD schema FIRST, before updating schema
        var prefix = SchemaCatalog.GetTableDataPrefix(tableName);
        var rowsToUpdate = new List<(long rowId, WitSqlValue[] values)>();

        foreach (var (key, value) in m_database.Scan(prefix, GetNextPrefix(prefix)))
        {
            var rowId = SchemaCatalog.ParseRowId(key, tableName);
            var existingRow = oldTable.DeserializeRow(value);

            // Convert the value in the specific column using centralized converter
            var newValues = existingRow.Values.ToArray();
            if (!newValues[columnIndex].IsNull)
            {
                newValues[columnIndex] = WitTypeConverter.Convert(newValues[columnIndex], newType);
            }
            rowsToUpdate.Add((rowId, newValues));
        }

        // Now update schema
        m_schema.AlterColumnType(tableName, columnName, newType);

        // Get the new table definition and write back the converted data
        var newTable = m_schema.GetTable(tableName)!;
        foreach (var (rowId, values) in rowsToUpdate)
        {
            var newKey = SchemaCatalog.CreateRowKey(tableName, rowId);
            var newData = newTable.SerializeValuesArray(values);
            PutToStore(newKey, newData);
        }
    }

    #endregion

    #region Column Default

    /// <summary>
    /// Set a column's default value.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name.</param>
    /// <param name="defaultValue">The default value (or null to clear).</param>
    public void SetColumnDefault(string tableName, string columnName, WitSqlValue? defaultValue)
    {
        m_schema.SetColumnDefault(tableName, columnName, defaultValue?.AsString());
    }

    /// <summary>
    /// Drop a column's default value.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name.</param>
    public void DropColumnDefault(string tableName, string columnName)
    {
        m_schema.SetColumnDefault(tableName, columnName, null);
    }

    #endregion

    #region Column NOT NULL

    /// <summary>
    /// Set or drop NOT NULL constraint on a column.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name.</param>
    /// <param name="notNull">True to set NOT NULL, false to drop it.</param>
    public void SetColumnNotNull(string tableName, string columnName, bool notNull)
    {
        if (notNull)
        {
            // Validate no existing NULL values
            var table = m_schema.GetTable(tableName)
                ?? throw new InvalidOperationException($"Table '{tableName}' not found");

            var columnIndex = -1;
            for (int i = 0; i < table.Columns.Count; i++)
            {
                if (table.Columns[i].Name.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                {
                    columnIndex = i;
                    break;
                }
            }

            if (columnIndex == -1)
                throw new InvalidOperationException($"Column '{columnName}' not found in table '{tableName}'");

            // Check for NULL values
            var prefix = SchemaCatalog.GetTableDataPrefix(tableName);
            foreach (var (key, value) in m_database.Scan(prefix, GetNextPrefix(prefix)))
            {
                var row = table.DeserializeRow(value);
                if (row[columnIndex].IsNull)
                {
                    throw new InvalidOperationException(
                        $"Cannot set NOT NULL on column '{columnName}': existing NULL values found");
                }
            }
        }

        m_schema.SetColumnNotNull(tableName, columnName, notNull);
    }

    #endregion

    #region Add Constraint

    /// <summary>
    /// Add a named constraint to an existing table.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="constraint">The constraint definition.</param>
    public void AddConstraint(string tableName, DefinitionNamedConstraint constraint)
    {
        var table = m_schema.GetTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' not found");

        // Check if constraint with this name already exists
        if (table.GetConstraint(constraint.Name) != null)
        {
            throw new InvalidOperationException($"Constraint '{constraint.Name}' already exists on table '{tableName}'");
        }

        // Validate constraint based on type
        switch (constraint.Type)
        {
            case ConstraintType.Check:
                ValidateCheckConstraint(table, constraint);
                break;

            case ConstraintType.Unique:
                ValidateUniqueConstraint(table, constraint);
                break;

            case ConstraintType.ForeignKey:
                ValidateForeignKeyConstraint(table, constraint);
                break;

            case ConstraintType.PrimaryKey:
                throw new NotSupportedException(
                    "Adding PRIMARY KEY constraint to existing table is not supported. " +
                    "This would require rebuilding the table.");
        }

        // Add constraint to schema
        m_schema.AddConstraint(tableName, constraint);

        // For UNIQUE constraints, create an implicit unique index
        if (constraint.Type == ConstraintType.Unique && constraint.Columns != null)
        {
            var indexName = $"UQ_{tableName}_{constraint.Name}";
            var index = new DefinitionIndex
            {
                Name = indexName,
                TableName = tableName,
                Columns = constraint.Columns.ToList(),
                IsUnique = true
            };
            CreateIndex(index);
        }
    }

    private void ValidateCheckConstraint(DefinitionTable table, DefinitionNamedConstraint constraint)
    {
        if (string.IsNullOrEmpty(constraint.CheckExpression))
        {
            throw new InvalidOperationException("CHECK constraint must have an expression");
        }

        // Parse the check expression
        var expression = WitSql.ParseExpression(constraint.CheckExpression);
        var context = new ContextExecution { Database = this };
        var evaluator = new ExpressionEvaluator(context);

        // Validate all existing rows satisfy the check constraint
        var prefix = SchemaCatalog.GetTableDataPrefix(table.Name);
        foreach (var (key, value) in m_database.Scan(prefix, GetNextPrefix(prefix)))
        {
            var row = table.DeserializeRow(value);
            var result = evaluator.Evaluate(expression, row);

            if (!result.IsNull && result.AsBool() == false)
            {
                throw new InvalidOperationException(
                    $"CHECK constraint '{constraint.Name}' is violated by existing data");
            }
        }
    }

    private void ValidateUniqueConstraint(DefinitionTable table, DefinitionNamedConstraint constraint)
    {
        if (constraint.Columns == null || constraint.Columns.Count == 0)
        {
            throw new InvalidOperationException("UNIQUE constraint must specify at least one column");
        }

        // Verify columns exist
        var columnIndices = new int[constraint.Columns.Count];
        for (int i = 0; i < constraint.Columns.Count; i++)
        {
            var colIndex = table.GetOrdinal(constraint.Columns[i]);
            if (colIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Column '{constraint.Columns[i]}' not found in table '{table.Name}'");
            }
            columnIndices[i] = colIndex;
        }

        // Check for duplicate values
        var seenValues = new HashSet<string>();
        var prefix = SchemaCatalog.GetTableDataPrefix(table.Name);

        foreach (var (key, value) in m_database.Scan(prefix, GetNextPrefix(prefix)))
        {
            var row = table.DeserializeRow(value);

            // Build composite key from all constraint columns
            var keyParts = new string[columnIndices.Length];
            bool allNull = true;

            for (int i = 0; i < columnIndices.Length; i++)
            {
                var val = row[columnIndices[i]];
                keyParts[i] = val.IsNull ? "\0NULL\0" : val.AsString() ?? "";
                if (!val.IsNull) allNull = false;
            }

            // Skip rows where all columns are NULL (NULL != NULL in UNIQUE)
            if (allNull) continue;

            var compositeKey = string.Join("\0", keyParts);
            if (!seenValues.Add(compositeKey))
            {
                throw new InvalidOperationException(
                    $"UNIQUE constraint '{constraint.Name}' is violated by existing duplicate data");
            }
        }
    }

    private void ValidateForeignKeyConstraint(DefinitionTable table, DefinitionNamedConstraint constraint)
    {
        if (constraint.ForeignKey == null)
        {
            throw new InvalidOperationException("FOREIGN KEY constraint must have foreign key definition");
        }

        var fk = constraint.ForeignKey;

        // Verify referenced table exists
        var refTable = m_schema.GetTable(fk.ForeignTable)
            ?? throw new InvalidOperationException($"Referenced table '{fk.ForeignTable}' not found");

        // Verify columns exist in source table
        var sourceColumnIndices = new int[fk.Columns.Count];
        for (int i = 0; i < fk.Columns.Count; i++)
        {
            var colIndex = table.GetOrdinal(fk.Columns[i]);
            if (colIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Column '{fk.Columns[i]}' not found in table '{table.Name}'");
            }
            sourceColumnIndices[i] = colIndex;
        }

        // Determine referenced columns (use PK if not specified)
        var refColumns = fk.ForeignColumns ?? refTable.PrimaryKey;
        if (refColumns == null || refColumns.Count == 0)
        {
            throw new InvalidOperationException(
                $"Referenced table '{fk.ForeignTable}' has no primary key and no columns specified");
        }

        // Verify referenced columns exist
        var refColumnIndices = new int[refColumns.Count];
        for (int i = 0; i < refColumns.Count; i++)
        {
            var colIndex = refTable.GetOrdinal(refColumns[i]);
            if (colIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Column '{refColumns[i]}' not found in referenced table '{fk.ForeignTable}'");
            }
            refColumnIndices[i] = colIndex;
        }

        // Build set of valid referenced values
        var validValues = new HashSet<string>();
        var refPrefix = SchemaCatalog.GetTableDataPrefix(fk.ForeignTable);
        foreach (var (key, value) in m_database.Scan(refPrefix, GetNextPrefix(refPrefix)))
        {
            var row = refTable.DeserializeRow(value);
            var keyParts = new string[refColumnIndices.Length];
            for (int i = 0; i < refColumnIndices.Length; i++)
            {
                var val = row[refColumnIndices[i]];
                keyParts[i] = val.IsNull ? "\0NULL\0" : val.AsString() ?? "";
            }
            validValues.Add(string.Join("\0", keyParts));
        }

        // Validate all source rows reference valid values
        var sourcePrefix = SchemaCatalog.GetTableDataPrefix(table.Name);
        foreach (var (key, value) in m_database.Scan(sourcePrefix, GetNextPrefix(sourcePrefix)))
        {
            var row = table.DeserializeRow(value);

            // Build source key
            var keyParts = new string[sourceColumnIndices.Length];
            bool hasNull = false;
            for (int i = 0; i < sourceColumnIndices.Length; i++)
            {
                var val = row[sourceColumnIndices[i]];
                if (val.IsNull)
                {
                    hasNull = true;
                    break;
                }
                keyParts[i] = val.AsString() ?? "";
            }

            // NULL values don't violate FK constraint
            if (hasNull) continue;

            var compositeKey = string.Join("\0", keyParts);
            if (!validValues.Contains(compositeKey))
            {
                throw new InvalidOperationException(
                    $"FOREIGN KEY constraint '{constraint.Name}' is violated: value not found in referenced table");
            }
        }
    }

    #endregion

    #region Drop Constraint

    /// <summary>
    /// Drop a named constraint from an existing table.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="constraintName">The constraint name.</param>
    public void DropConstraint(string tableName, string constraintName)
    {
        var table = m_schema.GetTable(tableName)
            ?? throw new InvalidOperationException($"Table '{tableName}' not found");

        var constraint = table.GetConstraint(constraintName);
        if (constraint == null)
        {
            throw new InvalidOperationException(
                $"Constraint '{constraintName}' not found on table '{tableName}'");
        }

        // Cannot drop PRIMARY KEY (would require table rebuild)
        if (constraint.Type == ConstraintType.PrimaryKey)
        {
            throw new NotSupportedException(
                "Dropping PRIMARY KEY constraint is not supported. This would require rebuilding the table.");
        }

        // For UNIQUE constraints, drop the associated index
        if (constraint.Type == ConstraintType.Unique)
        {
            var indexName = $"UQ_{tableName}_{constraintName}";
            var existingIndex = GetIndex(indexName);
            if (existingIndex != null)
            {
                DropIndex(indexName);
            }
        }

        // Remove constraint from schema
        m_schema.DropConstraint(tableName, constraintName);
    }

    #endregion

    #region Auto Increment

    /// <summary>
    /// Get next value for an auto-increment column.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <returns>The next auto-increment value.</returns>
    public long GetNextAutoIncrement(string tableName)
    {
        // Pass the current transaction to avoid lock conflicts
        return m_schema.GetNextRowId(tableName, m_currentTransaction);
    }

    #endregion

    #region Migration Helpers

    private void MigrateExistingRows(string tableName, DefinitionTable table, Func<WitSqlValue[], WitSqlValue[]> transform)
    {
        var prefix = SchemaCatalog.GetTableDataPrefix(tableName);
        var rowsToUpdate = new List<(long rowId, WitSqlValue[] newValues)>();

        foreach (var (key, value) in m_database.Scan(prefix, GetNextPrefix(prefix)))
        {
            var rowId = SchemaCatalog.ParseRowId(key, tableName);
            var existingRow = table.DeserializeRow(value);
            var newValues = transform(existingRow.Values.ToArray());
            rowsToUpdate.Add((rowId, newValues));
        }

        // Apply updates using same format as SerializeRow
        foreach (var (rowId, newValues) in rowsToUpdate)
        {
            var key = SchemaCatalog.CreateRowKey(tableName, rowId);
            var data = table.SerializeValuesArray(newValues);
            PutToStore(key, data);
        }
    }

    private static byte[] GetNextPrefix(byte[] prefix)
    {
        var next = new byte[prefix.Length];
        Array.Copy(prefix, next, prefix.Length);
        for (int i = next.Length - 1; i >= 0; i--)
        {
            if (next[i] < 0xFF)
            {
                next[i]++;
                break;
            }
            next[i] = 0;
        }
        return next;
    }

    #endregion
}
