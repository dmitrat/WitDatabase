using OutWit.Database.Definitions;
using OutWit.Database.Interfaces;
using OutWit.Database.Iterators;
using OutWit.Database.Model;
using OutWit.Database.Optimizers;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.TableSources;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Values;

namespace OutWit.Database.Query;

/// <summary>
/// Source iterator creation for QueryPlanner (tables, views, joins, subqueries).
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
        // A LATERAL item reads the row beside it, so it cannot be moved before the source it
        // reads - reordering is off whenever one is present. The optimiser has no way to know that
        // on its own, and a lateral moved to the front would resolve its outer columns against
        // nothing.
        var hasLateral = tables.Any(table => table is TableSourceLateral);

        IReadOnlyList<TableSource> orderedTables;

        if (hasLateral)
        {
            orderedTables = tables;
        }
        else
        {
            var joinOptimizer = new OptimizerJoinOrder(m_context.Database);
            var joinConditions = ExtractJoinConditions(whereClause);
            orderedTables = joinOptimizer.OptimizeJoinOrder(tables, joinConditions) ?? tables;
        }

        // Start with the first table source (with index optimization)
        var iterator = CreateTableSourceIterator(orderedTables[0], whereClause);

        // Handle implicit cross joins
        for (int i = 1; i < orderedTables.Count; i++)
        {
            // FROM T, LATERAL (…) AS X - the lateral's left is everything built so far, not a
            // cross join partner. This is the FROM-list spelling; the APPLY spelling carries its
            // left in the tree and is built by CreateLateralIterator.
            if (orderedTables[i] is TableSourceLateral lateral && lateral.Left is null)
            {
                iterator = new IteratorLateral(iterator, lateral.Subquery, m_context, lateral.IsOuter,
                    lateral.Alias, lateral.ColumnAliases);
                continue;
            }

            var rightIterator = CreateTableSourceIterator(orderedTables[i], null);
            iterator = new IteratorJoin(iterator, rightIterator, JoinType.Cross, null, m_context);
        }

        return iterator;
    }

    #endregion

    #region Join Condition Extraction

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

    #endregion

    #region Table Source Iterator

    private IResultIterator CreateTableSourceIterator(TableSource source, WitSqlExpression? whereClause = null)
    {
        return source switch
        {
            TableSourceSimple simple => CreateSimpleTableIterator(simple, whereClause),
            TableSourceJoin join => CreateJoinIterator(join),
            TableSourceSubquery subquery => CreateSubqueryIterator(subquery),
            TableSourceLateral lateral => CreateLateralIterator(lateral),
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

    #region View Iterator

    private IResultIterator CreateViewIterator(DefinitionView view, string alias)
    {
        var viewSelect = view.ResolveQuery();

        // Recursively plan the view query
        var viewIterator = Plan(viewSelect);
        return WrapWithAlias(viewIterator, alias);
    }

    #endregion

    #region Join and Subquery Iterators

    private IResultIterator CreateJoinIterator(TableSourceJoin join)
    {
        var left = CreateTableSourceIterator(join.Left);
        var right = CreateTableSourceIterator(join.Right);

        // Analyze join condition for hash join eligibility
        var analysis = OptimizerJoinCondition.Analyze(join.OnCondition);

        // Try hash join for INNER and LEFT joins with equi-join conditions
        if (analysis.HasEquiJoinKeys && 
            join.JoinType is JoinType.Inner or JoinType.Left)
        {
            var leftRowCount = left.EstimatedRowCount;
            var rightRowCount = right.EstimatedRowCount;

            if (OptimizerJoinCondition.ShouldUseHashJoin(leftRowCount, rightRowCount, analysis))
            {
                var buildLeft = OptimizerJoinCondition.ShouldBuildLeft(leftRowCount, rightRowCount);
                return new IteratorHashJoin(
                    left, 
                    right, 
                    join.JoinType, 
                    analysis.EquiJoinKeys, 
                    analysis.ResidualCondition,
                    m_context,
                    buildLeft);
            }
        }

        // Fall back to nested loop join
        // Optimize join side order for INNER and CROSS joins
        var joinOptimizer = new OptimizerJoinOrder(m_context.Database);
        if (joinOptimizer.ShouldSwapJoinSides(join))
        {
            // Swap left and right for better performance
            return new IteratorJoin(right, left, join.JoinType, join.OnCondition, m_context);
        }

        return new IteratorJoin(left, right, join.JoinType, join.OnCondition, m_context);
    }

    /// <summary>
    /// A correlated subquery in FROM - LATERAL, CROSS APPLY, OUTER APPLY.
    /// </summary>
    /// <remarks>
    /// The APPLY spelling writes the source it correlates with on its left; the LATERAL spelling
    /// takes it from the FROM list, and the caller has already built that when it gets here.
    /// </remarks>
    private IResultIterator CreateLateralIterator(TableSourceLateral lateral)
    {
        var left = lateral.Left is not null
            ? CreateTableSourceIterator(lateral.Left)
            : throw new InvalidOperationException(
                "LATERAL must follow a table source it can correlate with - write it after one in "
                + "the FROM list, or use CROSS APPLY / OUTER APPLY.");

        return new IteratorLateral(left, lateral.Subquery, m_context, lateral.IsOuter,
            lateral.Alias, lateral.ColumnAliases);
    }

    private IResultIterator CreateSubqueryIterator(TableSourceSubquery subquery)
    {
        var subqueryIterator = Plan(subquery.Subquery);
        var alias = subquery.Alias ?? throw new InvalidOperationException("Subquery in FROM must have an alias");

        if (subquery.ColumnAliases is { Count: > 0 } names)
            subqueryIterator = new IteratorRenameColumns(subqueryIterator, names);

        return WrapWithAlias(subqueryIterator, alias);
    }

    private static IResultIterator WrapWithAlias(IResultIterator iterator, string alias)
    {
        return new IteratorAlias(iterator, alias);
    }

    #endregion
}
