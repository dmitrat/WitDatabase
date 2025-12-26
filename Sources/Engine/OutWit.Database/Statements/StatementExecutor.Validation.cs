using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Parser;
using OutWit.Database.Values;

namespace OutWit.Database.Statements;

public sealed partial class StatementExecutor
{
    #region Constraint Validation

    /// <summary>
    /// Validates all constraints for a row.
    /// </summary>
    private void ValidateConstraints(DefinitionTable table, WitSqlRow row, string tableName, long? excludeRowId = null)
    {
        ValidateUniqueConstraints(table, row, tableName, excludeRowId);
        ValidateCheckConstraints(table, row, tableName);
        ValidateForeignKeyConstraints(table, row, tableName);
    }

    #endregion

    #region UNIQUE Constraints

    private void ValidateUniqueConstraints(DefinitionTable table, WitSqlRow row, string tableName, long? excludeRowId = null)
    {
        var constraintSets = new List<(IReadOnlyList<string> Columns, string Description)>();

        // 1. Add column-level UNIQUE constraints (single columns)
        foreach (var col in table.Columns)
        {
            if (col.IsUnique)
            {
                constraintSets.Add(([col.Name], $"UNIQUE constraint on {tableName}.{col.Name}"));
            }
        }

        // 2. Add PRIMARY KEY as unique constraint
        if (table.PrimaryKey != null && table.PrimaryKey.Count > 0)
        {
            constraintSets.Add((table.PrimaryKey, $"PRIMARY KEY constraint on {tableName}"));
        }
        else
        {
            // Single-column PRIMARY KEY via column flags
            foreach (var col in table.Columns)
            {
                if (col.IsPrimaryKey)
                {
                    bool alreadyAdded = constraintSets.Any(cs =>
                        cs.Columns.Count == 1 &&
                        cs.Columns[0].Equals(col.Name, StringComparison.OrdinalIgnoreCase));

                    if (!alreadyAdded)
                    {
                        constraintSets.Add(([col.Name], $"PRIMARY KEY constraint on {tableName}.{col.Name}"));
                    }
                }
            }
        }

        // 3. Add table-level composite UNIQUE constraints
        if (table.UniqueConstraints != null)
        {
            foreach (var uniqueColumns in table.UniqueConstraints)
            {
                constraintSets.Add((uniqueColumns, $"UNIQUE constraint on {tableName}({string.Join(", ", uniqueColumns)})"));
            }
        }

        if (constraintSets.Count == 0)
            return;

        // Scan existing rows to check for duplicates
        var iterator = m_context.Database.CreateTableScan(tableName);
        iterator.Open();

        try
        {
            while (iterator.MoveNext())
            {
                var existingRow = iterator.Current;

                // Skip the row being updated (for UPDATE operations)
                if (excludeRowId.HasValue)
                {
                    var rowIdValue = existingRow["_rowid"];
                    if (!rowIdValue.IsNull && rowIdValue.AsInt64() == excludeRowId.Value)
                        continue;
                }

                // Check each constraint set
                foreach (var (columns, description) in constraintSets)
                {
                    if (IsUniqueViolation(row, existingRow, columns))
                    {
                        throw new InvalidOperationException(
                            $"UNIQUE constraint failed: {tableName}.{columns[0]} (duplicate value: {row[columns[0]].AsString()})");
                    }
                }
            }
        }
        finally
        {
            iterator.Dispose();
        }
    }

    private static bool IsUniqueViolation(WitSqlRow newRow, WitSqlRow existingRow, IReadOnlyList<string> columns)
    {
        foreach (var colName in columns)
        {
            var newValue = newRow[colName];
            var existingValue = existingRow[colName];

            // NULL values don't violate UNIQUE constraint
            if (newValue.IsNull || existingValue.IsNull)
                return false;

            if (!newValue.Equals(existingValue))
                return false;
        }

        return true; // All columns match - it's a duplicate
    }

    #endregion

    #region CHECK Constraints

    private void ValidateCheckConstraints(DefinitionTable table, WitSqlRow row, string tableName)
    {
        var evaluator = new ExpressionEvaluator(m_context);

        // Validate column-level CHECK constraints
        foreach (var col in table.Columns)
        {
            if (col.CheckExpression == null)
                continue;

            // Skip CHECK if the column value is NULL (SQL standard behavior)
            var columnValue = row[col.Name];
            if (columnValue.IsNull)
                continue;

            var checkExpr = WitSql.ParseExpression(col.CheckExpression);
            var result = evaluator.Evaluate(checkExpr, row);

            // Skip if result is NULL
            if (result.IsNull)
                continue;

            if (!result.AsBool())
            {
                throw new InvalidOperationException($"CHECK constraint failed for column {tableName}.{col.Name}");
            }
        }

        // Validate table-level CHECK constraints
        if (table.CheckExpressions != null)
        {
            foreach (var checkSql in table.CheckExpressions)
            {
                var checkExpr = WitSql.ParseExpression(checkSql);
                var result = evaluator.Evaluate(checkExpr, row);

                // Skip if result is NULL
                if (result.IsNull)
                    continue;

                if (!result.AsBool())
                {
                    throw new InvalidOperationException($"CHECK constraint failed for table {tableName}");
                }
            }
        }
    }

    #endregion

    #region FOREIGN KEY Constraints

    private void ValidateForeignKeyConstraints(DefinitionTable table, WitSqlRow row, string tableName)
    {
        // Validate column-level FK constraints
        foreach (var col in table.Columns)
        {
            if (col.ForeignKey != null)
            {
                ValidateForeignKeyReference(col.ForeignKey, row, tableName);
            }
        }

        // Validate table-level FK constraints
        if (table.ForeignKeys != null)
        {
            foreach (var fk in table.ForeignKeys)
            {
                ValidateForeignKeyReference(fk, row, tableName);
            }
        }
    }

    private void ValidateForeignKeyReference(DefinitionForeignKey fk, WitSqlRow row, string tableName)
    {
        var foreignTable = m_context.Database.GetTable(fk.ForeignTable)
            ?? throw new InvalidOperationException($"Referenced table '{fk.ForeignTable}' not found");

        // Get local values for FK columns
        var localValues = new List<WitSqlValue>();
        bool allNull = true;

        foreach (var colName in fk.Columns)
        {
            var val = row[colName];
            localValues.Add(val);
            if (!val.IsNull) allNull = false;
        }

        // NULL values don't violate FK constraint
        if (allNull)
            return;

        // Determine foreign columns to check
        var foreignColumns = fk.ForeignColumns?.ToList()
            ?? foreignTable.PrimaryKey?.ToList()
            ?? throw new InvalidOperationException(
                $"Cannot validate FK constraint: referenced table '{fk.ForeignTable}' has no primary key");

        // Scan foreign table to find matching row
        var iterator = m_context.Database.CreateTableScan(fk.ForeignTable);
        iterator.Open();
        bool found = false;

        try
        {
            while (iterator.MoveNext())
            {
                var foreignRow = iterator.Current;
                bool matches = true;

                for (int i = 0; i < localValues.Count && i < foreignColumns.Count; i++)
                {
                    var localVal = localValues[i];
                    var foreignVal = foreignRow[foreignColumns[i]];

                    if (localVal.IsNull || foreignVal.IsNull || !localVal.Equals(foreignVal))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    found = true;
                    break;
                }
            }
        }
        finally
        {
            iterator.Dispose();
        }

        if (!found)
        {
            var valuesStr = string.Join(", ", localValues.Select(v => v.AsString()));
            throw new InvalidOperationException(
                $"FOREIGN KEY constraint failed: {tableName} -> {fk.ForeignTable} (values: {valuesStr})");
        }
    }

    #endregion
}
