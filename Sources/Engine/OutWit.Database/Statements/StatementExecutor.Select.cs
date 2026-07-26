using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.TableSources;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Sql;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Statements;

public sealed partial class StatementExecutor
{
    #region SELECT

    private WitSqlResult ExecuteSelect(WitSqlStatementSelect select)
    {
        // Try fast path for simple PK-based SELECT
        if (TryExecuteSelectFastPath(select, out var fastResult))
        {
            return fastResult;
        }

        // Try batch fast path for PK IN (...) pattern
        if (TryExecuteSelectBatchFastPath(select, out var batchResult))
        {
            return batchResult;
        }

        // Fall back to standard query planning
        return ExecuteSelectStandard(select);
    }

    /// <summary>
    /// Attempts to execute SELECT using fast path (direct row lookup).
    /// Fast path is used when:
    /// - Single simple table (no JOINs, no subqueries)
    /// - Simple PK equality WHERE clause (Id = @value)
    /// - PK column is AUTOINCREMENT (ensures PK value equals _rowid)
    /// - No aggregates, no GROUP BY, no HAVING
    /// - No ORDER BY, DISTINCT, LIMIT, OFFSET
    /// </summary>
    private bool TryExecuteSelectFastPath(WitSqlStatementSelect select, out WitSqlResult result)
    {
        result = default!;

        // Fast path not applicable for complex queries
        if (!IsSimpleSelectQuery(select))
            return false;

        // Must have exactly one simple table source
        if (select.FromClause == null || select.FromClause.Count != 1)
            return false;

        if (select.FromClause[0] is not TableSourceSimple tableSource)
            return false;

        // Get table definition
        var table = m_context.Database.GetTable(tableSource.TableName);
        if (table == null)
            return false;

        // Must have WHERE clause
        if (select.WhereClause == null)
            return false;

        // Try to extract simple PK equality condition
        var pkCondition = TryExtractSimplePkCondition(select.WhereClause, table);
        if (pkCondition == null)
            return false;

        // Evaluate PK value
        var evaluator = new ExpressionEvaluator(m_context);
        var dummyRow = new WitSqlRow([], []);

        WitSqlValue pkValue;
        try
        {
            pkValue = evaluator.Evaluate(pkCondition.Value.ValueExpression, dummyRow);
        }
        catch
        {
            return false; // Expression evaluation failed
        }

        if (pkValue.IsNull)
        {
            // NULL PK matches nothing - return empty result
            result = CreateEmptySelectResult(select, table, tableSource.Alias);
            return true;
        }

        // Get row directly by ID
        long rowId = pkValue.AsInt64();
        var row = m_context.Database.GetRowById(tableSource.TableName, rowId);

        if (row == null)
        {
            // Row not found - return empty result
            result = CreateEmptySelectResult(select, table, tableSource.Alias);
            return true;
        }

        // Apply column projection
        var tableAlias = tableSource.Alias ?? tableSource.TableName;
        var projectedRow = ApplySelectProjection(row.Value, select.SelectList, table, tableAlias, evaluator);
        var schema = BuildSelectColumnSchema(select.SelectList, table, tableAlias);

        result = new WitSqlResult([projectedRow], schema);
        return true;
    }

    /// <summary>
    /// Attempts to execute SELECT using batch fast path (direct row lookup for multiple rows).
    /// Batch fast path is used when:
    /// - Single simple table (no JOINs, no subqueries)
    /// - Simple PK IN (...) WHERE clause with value list
    /// - PK column is AUTOINCREMENT
    /// - No aggregates, no GROUP BY, no HAVING
    /// - ORDER BY, LIMIT, OFFSET are allowed (applied after fetch)
    /// </summary>
    private bool TryExecuteSelectBatchFastPath(WitSqlStatementSelect select, out WitSqlResult result)
    {
        result = default!;

        // Batch fast path not applicable for complex queries
        if (!IsSimpleBatchSelectQuery(select))
            return false;

        // Must have exactly one simple table source
        if (select.FromClause == null || select.FromClause.Count != 1)
            return false;

        if (select.FromClause[0] is not TableSourceSimple tableSource)
            return false;

        // Get table definition
        var table = m_context.Database.GetTable(tableSource.TableName);
        if (table == null)
            return false;

        // Must have WHERE clause
        if (select.WhereClause == null)
            return false;

        // Try to extract PK IN (...) condition
        var pkInCondition = TryExtractPkInCondition(select.WhereClause, table);
        if (pkInCondition == null)
            return false;

        // Evaluate all PK values
        var evaluator = new ExpressionEvaluator(m_context);
        var dummyRow = new WitSqlRow([], []);

        var pkValues = new List<long>();
        foreach (var valueExpr in pkInCondition.Value.ValueExpressions)
        {
            try
            {
                var pkValue = evaluator.Evaluate(valueExpr, dummyRow);
                if (!pkValue.IsNull)
                {
                    pkValues.Add(pkValue.AsInt64());
                }
            }
            catch
            {
                return false; // Expression evaluation failed
            }
        }

        if (pkValues.Count == 0)
        {
            // No valid PK values - return empty result
            result = CreateEmptySelectResult(select, table, tableSource.Alias);
            return true;
        }

        // Fetch all rows directly
        var rows = new List<WitSqlRow>();
        var tableAlias = tableSource.Alias ?? tableSource.TableName;

        foreach (var rowId in pkValues)
        {
            var row = m_context.Database.GetRowById(tableSource.TableName, rowId);
            if (row != null)
            {
                var projectedRow = ApplySelectProjection(row.Value, select.SelectList, table, tableAlias, evaluator);
                rows.Add(projectedRow);
            }
        }

        // Apply ORDER BY if present
        if (select.OrderByClause != null && select.OrderByClause.Count > 0)
        {
            rows = ApplyOrderByToRows(rows, select.OrderByClause, evaluator);
        }

        // Apply LIMIT/OFFSET if present
        if (select.LimitOffset != null)
        {
            var offsetValue = evaluator.Evaluate(select.LimitOffset, dummyRow);
            if (!offsetValue.IsNull)
            {
                rows = rows.Skip((int)offsetValue.AsInt64()).ToList();
            }
        }
        if (select.LimitCount != null)
        {
            var limitValue = evaluator.Evaluate(select.LimitCount, dummyRow);
            // A negative limit means unbounded; Take(-1) would return nothing.
            if (!limitValue.IsNull && limitValue.AsInt64() >= 0)
            {
                rows = rows.Take((int)limitValue.AsInt64()).ToList();
            }
        }

        var schema = BuildSelectColumnSchema(select.SelectList, table, tableAlias);
        result = new WitSqlResult(rows, schema);
        return true;
    }

    /// <summary>
    /// Standard iterator-based SELECT execution.
    /// </summary>
    private WitSqlResult ExecuteSelectStandard(WitSqlStatementSelect select)
    {
        var iterator = m_planner.Plan(select);
        iterator.Open();

        // Create cleanup action for CTE state
        var context = m_context;
        Action cleanupAction = () =>
        {
            context.CteDefinitions.Clear();
            context.CteCache.Clear();

            var keysToRemove = context.State.Keys
                .Where(k => k.StartsWith("CTE_"))
                .ToList();
            foreach (var key in keysToRemove)
            {
                context.State.Remove(key);
            }
        };

        return new WitSqlResult(iterator, cleanupAction);
    }

    #endregion

    #region SELECT Fast Path Helpers

    /// <summary>
    /// Checks if a SELECT query is simple enough for single-row fast path.
    /// </summary>
    private static bool IsSimpleSelectQuery(WitSqlStatementSelect select)
    {
        // No set operations (UNION, INTERSECT, EXCEPT)
        if (select.SetOperations != null && select.SetOperations.Count > 0)
            return false;

        // No CTEs
        if (select.CteDefinitions != null && select.CteDefinitions.Count > 0)
            return false;

        // No GROUP BY
        if (select.GroupByClause != null && select.GroupByClause.Count > 0)
            return false;

        // No HAVING
        if (select.HavingClause != null)
            return false;

        // No ORDER BY (for single row it doesn't matter, but keep it simple)
        if (select.OrderByClause != null && select.OrderByClause.Count > 0)
            return false;

        // No DISTINCT
        if (select.IsDistinct)
            return false;

        // No LIMIT/OFFSET
        if (select.LimitCount != null || select.LimitOffset != null)
            return false;

        // No aggregate functions in SELECT list
        if (HasAggregateFunctionsInSelectList(select.SelectList))
            return false;

        // No window functions in SELECT list
        if (HasWindowFunctionsInSelectList(select.SelectList))
            return false;

        // No FOR UPDATE/SHARE locking
        if (select.ForClause != null)
            return false;

        return true;
    }

    /// <summary>
    /// Checks if a SELECT query is simple enough for batch fast path.
    /// Similar to single-row but allows ORDER BY and LIMIT/OFFSET.
    /// </summary>
    private static bool IsSimpleBatchSelectQuery(WitSqlStatementSelect select)
    {
        // No set operations
        if (select.SetOperations != null && select.SetOperations.Count > 0)
            return false;

        // No CTEs
        if (select.CteDefinitions != null && select.CteDefinitions.Count > 0)
            return false;

        // No GROUP BY
        if (select.GroupByClause != null && select.GroupByClause.Count > 0)
            return false;

        // No HAVING
        if (select.HavingClause != null)
            return false;

        // No DISTINCT
        if (select.IsDistinct)
            return false;

        // No aggregate functions
        if (HasAggregateFunctionsInSelectList(select.SelectList))
            return false;

        // No window functions
        if (HasWindowFunctionsInSelectList(select.SelectList))
            return false;

        // No FOR UPDATE/SHARE locking
        if (select.ForClause != null)
            return false;

        return true;
    }

    /// <summary>
    /// Checks if any expression in the select list contains aggregate functions.
    /// </summary>
    private static bool HasAggregateFunctionsInSelectList(IReadOnlyList<ClauseSelectItem>? selectList)
    {
        if (selectList == null)
            return false;

        foreach (var item in selectList)
        {
            if (item.IsStar)
                continue;

            if (item.Expression != null && ContainsAggregateFunctionExpr(item.Expression))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if an expression contains aggregate functions.
    /// </summary>
    private static bool ContainsAggregateFunctionExpr(WitSqlExpression expr)
    {
        return expr switch
        {
            WitSqlExpressionFunctionCall func when IsAggregateFunctionName(func.FunctionName) => true,
            WitSqlExpressionBinary binary => 
                ContainsAggregateFunctionExpr(binary.Left) || ContainsAggregateFunctionExpr(binary.Right),
            WitSqlExpressionUnary unary => 
                ContainsAggregateFunctionExpr(unary.Operand),
            WitSqlExpressionFunctionCall func => 
                func.Arguments?.Any(a => ContainsAggregateFunctionExpr(a)) ?? false,
            WitSqlExpressionCase caseExpr => 
                caseExpr.WhenClauses.Any(w => ContainsAggregateFunctionExpr(w.When) || ContainsAggregateFunctionExpr(w.Then)) ||
                (caseExpr.ElseResult != null && ContainsAggregateFunctionExpr(caseExpr.ElseResult)),
            _ => false
        };
    }

    /// <summary>
    /// Checks if a function name is an aggregate function.
    /// </summary>
    private static bool IsAggregateFunctionName(string functionName)
    {
        return functionName.ToUpperInvariant() switch
        {
            "COUNT" or "SUM" or "AVG" or "MIN" or "MAX" or 
            "GROUP_CONCAT" or "STRING_AGG" or "ARRAY_AGG" => true,
            _ => false
        };
    }

    /// <summary>
    /// Checks if any expression in the select list contains window functions.
    /// </summary>
    private static bool HasWindowFunctionsInSelectList(IReadOnlyList<ClauseSelectItem>? selectList)
    {
        if (selectList == null)
            return false;

        foreach (var item in selectList)
        {
            if (item.IsStar)
                continue;

            if (item.Expression != null && ContainsWindowFunctionExpr(item.Expression))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if an expression contains window functions.
    /// </summary>
    private static bool ContainsWindowFunctionExpr(WitSqlExpression expr)
    {
        return expr switch
        {
            WitSqlExpressionFunctionCall func when func.Over != null => true,
            WitSqlExpressionBinary binary =>
                ContainsWindowFunctionExpr(binary.Left) || ContainsWindowFunctionExpr(binary.Right),
            WitSqlExpressionUnary unary =>
                ContainsWindowFunctionExpr(unary.Operand),
            WitSqlExpressionFunctionCall func =>
                func.Arguments?.Any(a => ContainsWindowFunctionExpr(a)) ?? false,
            _ => false
        };
    }

    /// <summary>
    /// Creates an empty result set for SELECT queries that match no rows.
    /// </summary>
    private WitSqlResult CreateEmptySelectResult(WitSqlStatementSelect select, DefinitionTable table, string? tableAlias)
    {
        var schema = BuildSelectColumnSchema(select.SelectList, table, tableAlias ?? table.Name);
        return new WitSqlResult(Array.Empty<WitSqlRow>(), schema);
    }

    /// <summary>
    /// Applies column projection to a row based on SELECT list.
    /// </summary>
    private WitSqlRow ApplySelectProjection(
        WitSqlRow sourceRow, 
        IReadOnlyList<ClauseSelectItem>? selectList, 
        DefinitionTable table,
        string tableAlias,
        ExpressionEvaluator evaluator)
    {
        // Handle SELECT * 
        if (selectList == null || selectList.Count == 0 || 
            (selectList.Count == 1 && selectList[0].IsStar && selectList[0].TableName == null))
        {
            // Return all columns except _rowid
            var values = new List<WitSqlValue>();
            var names = new List<string>();

            foreach (var colName in sourceRow.ColumnNames)
            {
                if (colName == "_rowid")
                    continue;

                values.Add(sourceRow[colName]);
                names.Add(colName);
            }

            return new WitSqlRow(values.ToArray(), names.ToArray());
        }

        var resultValues = new List<WitSqlValue>();
        var resultNames = new List<string>();

        foreach (var item in selectList)
        {
            if (item.IsStar)
            {
                // table.* - expand all columns from that table
                if (item.TableName == null || 
                    item.TableName.Equals(tableAlias, StringComparison.OrdinalIgnoreCase) ||
                    item.TableName.Equals(table.Name, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var col in table.Columns)
                    {
                        resultValues.Add(sourceRow[col.Name]);
                        resultNames.Add(col.Name);
                    }
                }
            }
            else if (item.Expression != null)
            {
                var value = evaluator.Evaluate(item.Expression, sourceRow);
                var name = item.Alias ?? GetSelectExpressionName(item.Expression);
                resultValues.Add(value);
                resultNames.Add(name);
            }
        }

        return new WitSqlRow(resultValues.ToArray(), resultNames.ToArray());
    }

    /// <summary>
    /// Builds the column schema for a SELECT result.
    /// </summary>
    private static IReadOnlyList<WitSqlColumnInfo> BuildSelectColumnSchema(
        IReadOnlyList<ClauseSelectItem>? selectList, 
        DefinitionTable table,
        string tableAlias)
    {
        // Handle SELECT *
        if (selectList == null || selectList.Count == 0 ||
            (selectList.Count == 1 && selectList[0].IsStar && selectList[0].TableName == null))
        {
            return table.Columns.Select(c => new WitSqlColumnInfo
            {
                Name = c.Name,
                Type = c.Type.ToSqlType(),
                IsNullable = c.Nullable,
                TableName = table.Name
            }).ToList();
        }

        var schema = new List<WitSqlColumnInfo>();

        foreach (var item in selectList)
        {
            if (item.IsStar)
            {
                if (item.TableName == null ||
                    item.TableName.Equals(tableAlias, StringComparison.OrdinalIgnoreCase) ||
                    item.TableName.Equals(table.Name, StringComparison.OrdinalIgnoreCase))
                {
                    schema.AddRange(table.Columns.Select(c => new WitSqlColumnInfo
                    {
                        Name = c.Name,
                        Type = c.Type.ToSqlType(),
                        IsNullable = c.Nullable,
                        TableName = table.Name
                    }));
                }
            }
            else if (item.Expression != null)
            {
                var name = item.Alias ?? GetSelectExpressionName(item.Expression);
                var columnType = InferSelectExpressionType(item.Expression, table);
                schema.Add(new WitSqlColumnInfo
                {
                    Name = name,
                    Type = columnType,
                    IsNullable = true,
                    TableName = null
                });
            }
        }

        return schema;
    }

    /// <summary>
    /// Infers the SQL type of an expression for SELECT fast path.
    /// </summary>
    private static WitSqlType InferSelectExpressionType(WitSqlExpression expr, DefinitionTable table)
    {
        return expr switch
        {
            WitSqlExpressionColumnRef colRef => 
                table.Columns.FirstOrDefault(c => c.Name.Equals(colRef.ColumnName, StringComparison.OrdinalIgnoreCase))
                    is { } col ? col.Type.ToSqlType() : WitSqlType.Text,
            WitSqlExpressionLiteral lit => lit.Type switch
            {
                LiteralType.Integer => WitSqlType.Integer,
                LiteralType.Real => WitSqlType.Real,
                LiteralType.Decimal => WitSqlType.Decimal,
                LiteralType.String => WitSqlType.Text,
                LiteralType.Boolean => WitSqlType.Boolean,
                LiteralType.Null => WitSqlType.Null,
                LiteralType.Blob => WitSqlType.Blob,
                _ => WitSqlType.Text
            },
            WitSqlExpressionFunctionCall func => InferSelectFunctionReturnType(func.FunctionName),
            _ => WitSqlType.Text
        };
    }

    /// <summary>
    /// Infers the return type of a function for SELECT fast path.
    /// </summary>
    private static WitSqlType InferSelectFunctionReturnType(string functionName)
    {
        return functionName.ToUpperInvariant() switch
        {
            "COUNT" => WitSqlType.Integer,
            "SUM" or "AVG" => WitSqlType.Real,
            "MIN" or "MAX" => WitSqlType.Text, // Could be any type
            "LENGTH" or "CHAR_LENGTH" => WitSqlType.Integer,
            "UPPER" or "LOWER" or "TRIM" or "LTRIM" or "RTRIM" or "SUBSTRING" or "REPLACE" => WitSqlType.Text,
            "NOW" or "CURRENT_TIMESTAMP" => WitSqlType.DateTime,
            "CURRENT_DATE" => WitSqlType.DateOnly,
            "CURRENT_TIME" => WitSqlType.TimeOnly,
            "COALESCE" or "NULLIF" or "IFNULL" => WitSqlType.Text, // Depends on arguments
            _ => WitSqlType.Text
        };
    }

    /// <summary>
    /// Gets a name for an expression (for result column naming).
    /// </summary>
    private static string GetSelectExpressionName(WitSqlExpression expr)
    {
        return expr switch
        {
            WitSqlExpressionColumnRef col => col.ColumnName,
            WitSqlExpressionFunctionCall func => func.FunctionName,
            _ => "column"
        };
    }

    /// <summary>
    /// Applies ORDER BY to a list of rows.
    /// </summary>
    private List<WitSqlRow> ApplyOrderByToRows(
        List<WitSqlRow> rows, 
        IReadOnlyList<ClauseOrderByItem> orderBy,
        ExpressionEvaluator evaluator)
    {
        if (orderBy.Count == 0 || rows.Count <= 1)
            return rows;

        // Sort by first ORDER BY item
        var firstItem = orderBy[0];
        
        IOrderedEnumerable<WitSqlRow> sorted = firstItem.Descending
            ? rows.OrderByDescending(row => evaluator.Evaluate(firstItem.Expression, row))
            : rows.OrderBy(row => evaluator.Evaluate(firstItem.Expression, row));

        // Apply additional ORDER BY items
        for (int i = 1; i < orderBy.Count; i++)
        {
            var item = orderBy[i];
            sorted = item.Descending
                ? sorted.ThenByDescending(row => evaluator.Evaluate(item.Expression, row))
                : sorted.ThenBy(row => evaluator.Evaluate(item.Expression, row));
        }

        return sorted.ToList();
    }

    #endregion
}
