using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Interfaces;
using OutWit.Database.Iterators;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.TableSources;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Schema;
using OutWit.Database.Values;

namespace OutWit.Database.Query;

/// <summary>
/// Source iterator creation for QueryPlanner (tables, views, indexes, joins).
/// </summary>
public sealed partial class QueryPlanner
{
    #region Source Iterator Creation

    private IResultIterator CreateSourceIterator(WitSqlStatementSelect select)
    {
        if (select.FromClause == null || select.FromClause.Count == 0)
        {
            // SELECT without FROM - returns single row
            return new IteratorSingleRow([], []);
        }

        // For multiple tables in FROM (implicit cross joins), optimize the order
        if (select.FromClause.Count > 1)
        {
            return CreateOptimizedMultiTableIterator(select.FromClause, select.WhereClause);
        }

        // Single table source (may be a join)
        return CreateTableSourceIterator(select.FromClause[0], select.WhereClause);
    }

    /// <summary>
    /// Creates an optimized iterator for multiple tables in FROM clause.
    /// Reorders tables to minimize intermediate result sizes.
    /// </summary>
    private IResultIterator CreateOptimizedMultiTableIterator(
        IReadOnlyList<TableSource> tables, 
        WitSqlExpression? whereClause)
    {
        // Try to optimize join order
        var joinOptimizer = new JoinOrderOptimizer(m_context.Database);
        var joinConditions = ExtractJoinConditions(whereClause);
        var optimizedOrder = joinOptimizer.OptimizeJoinOrder(tables, joinConditions);

        // Use optimized order if available, otherwise use original
        var orderedTables = optimizedOrder ?? tables;

        // Start with the first table source (with index optimization)
        var iterator = CreateTableSourceIterator(orderedTables[0], whereClause);

        // Handle implicit cross joins
        for (int i = 1; i < orderedTables.Count; i++)
        {
            var rightIterator = CreateTableSourceIterator(orderedTables[i], null);
            iterator = new IteratorJoin(iterator, rightIterator, JoinType.Cross, null, m_context);
        }

        return iterator;
    }

    /// <summary>
    /// Extracts join condition information from WHERE clause for optimization.
    /// </summary>
    private List<JoinConditionInfo>? ExtractJoinConditions(WitSqlExpression? whereClause)
    {
        if (whereClause == null)
            return null;

        var conditions = new List<JoinConditionInfo>();
        ExtractJoinConditionsRecursive(whereClause, conditions);

        return conditions.Count > 0 ? conditions : null;
    }

    private void ExtractJoinConditionsRecursive(WitSqlExpression expression, List<JoinConditionInfo> conditions)
    {
        switch (expression)
        {
            case WitSqlExpressionBinary binary:
                if (binary.Operator == BinaryOperatorType.And)
                {
                    ExtractJoinConditionsRecursive(binary.Left, conditions);
                    ExtractJoinConditionsRecursive(binary.Right, conditions);
                }
                else if (binary.Operator == BinaryOperatorType.Equal)
                {
                    // Check if this is a join condition (column = column)
                    if (binary.Left is WitSqlExpressionColumnRef leftCol && 
                        binary.Right is WitSqlExpressionColumnRef rightCol &&
                        leftCol.TableName != null && rightCol.TableName != null)
                    {
                        var isPkJoin = IsColumnPrimaryKey(leftCol.TableName, leftCol.ColumnName) ||
                                      IsColumnPrimaryKey(rightCol.TableName, rightCol.ColumnName);

                        conditions.Add(new JoinConditionInfo
                        {
                            LeftTableAlias = leftCol.TableName,
                            LeftColumnName = leftCol.ColumnName,
                            RightTableAlias = rightCol.TableName,
                            RightColumnName = rightCol.ColumnName,
                            IsPrimaryKeyJoin = isPkJoin
                        });
                    }
                }
                break;
        }
    }

    private bool IsColumnPrimaryKey(string tableName, string columnName)
    {
        var table = m_context.Database.GetTable(tableName);
        if (table?.PrimaryKey == null)
            return false;

        return table.PrimaryKey.Any(pk => 
            pk.Equals(columnName, StringComparison.OrdinalIgnoreCase));
    }

    private IResultIterator CreateTableSourceIterator(TableSource source, WitSqlExpression? whereClause = null)
    {
        return source switch
        {
            TableSourceSimple simple => CreateSimpleTableIterator(simple, whereClause),
            TableSourceJoin join => CreateJoinIterator(join),
            TableSourceSubquery subquery => CreateSubqueryIterator(subquery),
            _ => throw new NotSupportedException($"Table source type not supported: {source.GetType().Name}")
        };
    }

    private IResultIterator CreateSimpleTableIterator(TableSourceSimple simple, WitSqlExpression? whereClause = null)
    {
        // First check if it's an INFORMATION_SCHEMA reference
        if (simple.TableName.StartsWith("INFORMATION_SCHEMA.", StringComparison.OrdinalIgnoreCase))
        {
            return CreateInformationSchemaIterator(simple);
        }

        // Check for INFORMATION_SCHEMA without prefix (case when FROM uses INFORMATION_SCHEMA.TABLES directly)
        var parts = simple.TableName.Split('.', 2);
        if (parts.Length == 2 && parts[0].Equals("INFORMATION_SCHEMA", StringComparison.OrdinalIgnoreCase))
        {
            return CreateInformationSchemaIterator(simple);
        }

        // Then check if it's a CTE reference
        var cteDef = TryGetCteDefinition(simple.TableName);
        if (cteDef != null)
        {
            // Check if this is a recursive CTE and we have a working table
            if (IsRecursiveCte(simple.TableName))
            {
                var workingTable = GetRecursiveWorkingTable(simple.TableName);
                if (workingTable != null)
                {
                    // Use the working table for recursive iteration with proper schema
                    var schema = GetRecursiveWorkingTableSchema(simple.TableName);
                    var inMemoryIterator = schema != null
                        ? new IteratorInMemory(workingTable, schema)
                        : new IteratorInMemory(workingTable);
                    return WrapWithAlias(inMemoryIterator, simple.Alias ?? simple.TableName);
                }
                
                // First access to recursive CTE - execute it fully
                return CreateRecursiveCteIterator(cteDef, simple.Alias ?? simple.TableName);
            }
            
            // For non-recursive CTEs, check if we have cached results
            var cached = TryGetCachedCte(simple.TableName);
            if (cached != null)
            {
                // Use cached results
                var cachedIterator = new IteratorInMemory(cached.Rows, cached.Schema);
                return WrapWithAlias(cachedIterator, simple.Alias ?? simple.TableName);
            }
            
            return CreateCteIterator(cteDef, simple.Alias ?? simple.TableName);
        }

        // Then check if it's a view
        var view = m_context.Database.GetView(simple.TableName);
        if (view != null)
        {
            return CreateViewIterator(view, simple.Alias ?? simple.TableName);
        }

        // Otherwise it's a regular table - try to use index if available
        return CreateOptimizedTableIterator(simple.TableName, simple.Alias ?? simple.TableName, whereClause);
    }

    #endregion

    #region Table and Index Iterators

    /// <summary>
    /// Creates an optimized table iterator, potentially using an index.
    /// </summary>
    private IResultIterator CreateOptimizedTableIterator(string tableName, string alias, WitSqlExpression? whereClause)
    {
        // Get table definition (may be null for mocked databases in tests)
        var table = m_context.Database.GetTable(tableName);
        
        // If table definition is not available, fall back to simple table scan
        if (table == null)
        {
            return WrapWithAlias(m_context.Database.CreateTableScan(tableName), alias);
        }

        // Get available indexes for this table
        var indexes = m_context.Database.GetTableIndexes(tableName).ToList();

        // Try to find the best index strategy
        IndexStrategy? strategy = null;
        if (whereClause != null && indexes.Count > 0)
        {
            // Estimate row count (we don't have statistics, so use a heuristic)
            long estimatedRowCount = EstimateTableRowCount(tableName);
            
            if (estimatedRowCount >= MIN_ROWS_FOR_INDEX)
            {
                strategy = m_optimizer.FindBestIndex(tableName, whereClause, indexes, estimatedRowCount);
            }
        }

        IResultIterator iterator;

        if (strategy != null)
        {
            // Use index-based access
            iterator = CreateIndexIterator(tableName, strategy);
        }
        else
        {
            // Fall back to table scan
            iterator = m_context.Database.CreateTableScan(tableName);
        }

        return WrapWithAlias(iterator, alias);
    }

    /// <summary>
    /// Creates an index-based iterator based on the strategy.
    /// </summary>
    private IResultIterator CreateIndexIterator(string tableName, IndexStrategy strategy)
    {
        var evaluator = new ExpressionEvaluator(m_context);
        var dummyRow = new WitSqlRow([], []);

        switch (strategy.AccessType)
        {
            case IndexAccessType.Seek:
                // Equality lookup
                var seekValue = evaluator.Evaluate(strategy.SeekValue!, dummyRow);
                return m_context.Database.CreateIndexSeek(
                    tableName, 
                    strategy.IndexName, 
                    [seekValue]);

            case IndexAccessType.RangeScan:
                // Range scan - explicitly handle nullable WitSqlValue
                WitSqlValue? startValue = null;
                WitSqlValue? endValue = null;
                
                if (strategy.RangeStart != null)
                {
                    startValue = evaluator.Evaluate(strategy.RangeStart, dummyRow);
                }
                
                if (strategy.RangeEnd != null)
                {
                    endValue = evaluator.Evaluate(strategy.RangeEnd, dummyRow);
                }

                return m_context.Database.CreateIndexRangeScan(
                    tableName,
                    strategy.IndexName,
                    startValue,
                    strategy.RangeStartInclusive,
                    endValue,
                    strategy.RangeEndInclusive);

            default:
                throw new NotSupportedException($"Index access type not supported: {strategy.AccessType}");
        }
    }

    /// <summary>
    /// Estimates the row count for a table.
    /// Without statistics, we use a simple heuristic.
    /// </summary>
    private long EstimateTableRowCount(string tableName)
    {
        // TODO: Implement proper statistics collection
        // For now, do a quick scan to count rows (expensive but accurate)
        // In a real implementation, we would maintain statistics
        
        try
        {
            using var iterator = m_context.Database.CreateTableScan(tableName);
            iterator.Open();
            
            long count = 0;
            const long sampleLimit = 1000; // Sample up to 1000 rows
            
            while (iterator.MoveNext() && count < sampleLimit)
            {
                count++;
            }
            
            // If we hit the limit, estimate based on sample
            if (count >= sampleLimit)
            {
                // Assume table is larger, return a higher estimate
                return count * 10;
            }
            
            return count;
        }
        catch
        {
            // If counting fails, return a default estimate
            return 100;
        }
    }

    #endregion

    #region View Iterator

    private IResultIterator CreateViewIterator(DefinitionView view, string alias)
    {
        // Parse and plan the view's SELECT statement
        var viewSelect = Parser.WitSql.ParseStatement(view.SelectSql) as WitSqlStatementSelect
            ?? throw new InvalidOperationException($"View '{view.Name}' contains invalid SELECT statement");

        // Recursively plan the view query
        var viewIterator = Plan(viewSelect);
        return WrapWithAlias(viewIterator, alias);
    }

    #endregion

    #region INFORMATION_SCHEMA Iterator

    /// <summary>
    /// Creates an iterator for INFORMATION_SCHEMA virtual tables.
    /// </summary>
    private IResultIterator CreateInformationSchemaIterator(TableSourceSimple simple)
    {
        // Parse the view name from INFORMATION_SCHEMA.VIEW_NAME
        var tableName = simple.TableName;
        var viewName = tableName.Contains('.')
            ? tableName.Split('.', 2)[1]
            : tableName;

        // Get schema catalog from database to create InformationSchema helper
        // We need to get the schema catalog - it's stored in the context's database
        if (m_context.Database is not WitSqlEngine engine)
        {
            throw new InvalidOperationException("INFORMATION_SCHEMA is only available with WitSqlEngine");
        }

        var infoSchema = engine.GetInformationSchema();

        return viewName.ToUpperInvariant() switch
        {
            "TABLES" => new IteratorInformationSchema(
                infoSchema.GetTables(),
                InformationSchema.GetTablesColumns(),
                InformationSchema.GetTablesColumnTypes()),
                
            "COLUMNS" => new IteratorInformationSchema(
                infoSchema.GetColumns(),
                InformationSchema.GetColumnsColumns(),
                InformationSchema.GetColumnsColumnTypes()),
                
            "KEY_COLUMN_USAGE" => new IteratorInformationSchema(
                infoSchema.GetKeyColumnUsage(),
                InformationSchema.GetKeyColumnUsageColumns(),
                InformationSchema.GetKeyColumnUsageColumnTypes()),
                
            "TABLE_CONSTRAINTS" => new IteratorInformationSchema(
                infoSchema.GetTableConstraints(),
                InformationSchema.GetTableConstraintsColumns(),
                InformationSchema.GetTableConstraintsColumnTypes()),
                
            "REFERENTIAL_CONSTRAINTS" => new IteratorInformationSchema(
                infoSchema.GetReferentialConstraints(),
                InformationSchema.GetReferentialConstraintsColumns(),
                InformationSchema.GetReferentialConstraintsColumnTypes()),
                
            "INDEXES" => new IteratorInformationSchema(
                infoSchema.GetIndexes(),
                InformationSchema.GetIndexesColumns(),
                InformationSchema.GetIndexesColumnTypes()),
                
            "VIEWS" => new IteratorInformationSchema(
                infoSchema.GetViews(),
                InformationSchema.GetViewsColumns(),
                InformationSchema.GetViewsColumnTypes()),
                
            _ => throw new InvalidOperationException($"Unknown INFORMATION_SCHEMA view: {viewName}")
        };
    }

    #endregion

    #region Join and Subquery Iterators

    private IResultIterator CreateJoinIterator(TableSourceJoin join)
    {
        var left = CreateTableSourceIterator(join.Left);
        var right = CreateTableSourceIterator(join.Right);

        // Optimize join side order for INNER and CROSS joins
        var joinOptimizer = new JoinOrderOptimizer(m_context.Database);
        if (joinOptimizer.ShouldSwapJoinSides(join))
        {
            // Swap left and right for better performance
            return new IteratorJoin(right, left, join.JoinType, join.OnCondition, m_context);
        }

        return new IteratorJoin(left, right, join.JoinType, join.OnCondition, m_context);
    }

    private IResultIterator CreateSubqueryIterator(TableSourceSubquery subquery)
    {
        var subqueryIterator = Plan(subquery.Subquery);
        var alias = subquery.Alias ?? throw new InvalidOperationException("Subquery in FROM must have an alias");
        return WrapWithAlias(subqueryIterator, alias);
    }

    private static IResultIterator WrapWithAlias(IResultIterator iterator, string alias)
    {
        return new IteratorAlias(iterator, alias);
    }

    #endregion
}
