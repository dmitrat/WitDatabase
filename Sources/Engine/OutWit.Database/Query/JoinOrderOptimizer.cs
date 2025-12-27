using OutWit.Database.Interfaces;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.TableSources;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Query;

/// <summary>
/// Optimizes join order to minimize intermediate result sizes.
/// Uses a greedy algorithm based on estimated table sizes.
/// </summary>
public sealed class JoinOrderOptimizer
{
    #region Constants

    /// <summary>
    /// Minimum number of tables to trigger join ordering optimization.
    /// </summary>
    private const int MIN_TABLES_FOR_OPTIMIZATION = 2;

    /// <summary>
    /// Default selectivity for join predicates (10% of rows match).
    /// </summary>
    private const double DEFAULT_JOIN_SELECTIVITY = 0.1;

    /// <summary>
    /// Selectivity for equality join on primary key or unique column.
    /// </summary>
    private const double PK_JOIN_SELECTIVITY = 0.01;

    #endregion

    #region Fields

    private readonly IDatabase m_database;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new join order optimizer.
    /// </summary>
    /// <param name="database">The database for table statistics.</param>
    public JoinOrderOptimizer(IDatabase database)
    {
        m_database = database;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Optimizes the order of tables in a join to minimize intermediate result sizes.
    /// Returns null if no optimization is needed or possible.
    /// </summary>
    /// <param name="tables">List of table sources to join.</param>
    /// <param name="joinConditions">Join conditions (ON clauses) if available.</param>
    /// <returns>Optimized table order, or null if no change needed.</returns>
    public IReadOnlyList<TableSource>? OptimizeJoinOrder(
        IReadOnlyList<TableSource> tables,
        IReadOnlyList<JoinConditionInfo>? joinConditions = null)
    {
        if (tables.Count < MIN_TABLES_FOR_OPTIMIZATION)
            return null;

        // Collect table statistics
        var tableStats = CollectTableStatistics(tables);
        if (tableStats.Count == 0)
            return null;

        // Use greedy algorithm to build optimal join order
        var optimizedOrder = GreedyJoinOrdering(tableStats, joinConditions);

        // Check if order actually changed
        if (IsOrderUnchanged(tables, optimizedOrder))
            return null;

        return optimizedOrder;
    }

    /// <summary>
    /// Optimizes a single join by potentially swapping left and right sides.
    /// Returns true if the sides should be swapped.
    /// </summary>
    /// <param name="join">The join to optimize.</param>
    /// <returns>True if left and right should be swapped.</returns>
    public bool ShouldSwapJoinSides(TableSourceJoin join)
    {
        // Only optimize INNER and CROSS joins (swapping LEFT/RIGHT changes semantics)
        if (join.JoinType != JoinType.Inner && join.JoinType != JoinType.Cross)
            return false;

        var leftSize = EstimateTableSourceSize(join.Left);
        var rightSize = EstimateTableSourceSize(join.Right);

        // For nested-loop join, smaller table should be on the left (outer loop)
        // This reduces the number of times we scan the right (buffered) table
        return leftSize > rightSize;
    }

    #endregion

    #region Statistics Collection

    private Dictionary<string, TableStatistics> CollectTableStatistics(IReadOnlyList<TableSource> tables)
    {
        var stats = new Dictionary<string, TableStatistics>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in tables)
        {
            var tableName = GetTableName(table);
            if (tableName == null)
                continue;

            var rowCount = EstimateTableRowCount(tableName);
            var alias = GetTableAlias(table) ?? tableName;

            stats[alias] = new TableStatistics
            {
                TableName = tableName,
                Alias = alias,
                EstimatedRowCount = rowCount,
                Source = table
            };
        }

        return stats;
    }

    private long EstimateTableRowCount(string tableName)
    {
        try
        {
            using var iterator = m_database.CreateTableScan(tableName);
            iterator.Open();
            
            long count = 0;
            const long sampleLimit = 1000;
            
            while (iterator.MoveNext() && count < sampleLimit)
            {
                count++;
            }
            
            // If we hit the limit, estimate based on sample
            if (count >= sampleLimit)
            {
                return count * 10;
            }
            
            return Math.Max(1, count);
        }
        catch
        {
            // If counting fails, return a default estimate
            return 100;
        }
    }

    private long EstimateTableSourceSize(TableSource source)
    {
        return source switch
        {
            TableSourceSimple simple => EstimateTableRowCount(simple.TableName),
            TableSourceJoin join => EstimateJoinSize(join),
            TableSourceSubquery => 100, // Default estimate for subqueries
            _ => 100
        };
    }

    private long EstimateJoinSize(TableSourceJoin join)
    {
        var leftSize = EstimateTableSourceSize(join.Left);
        var rightSize = EstimateTableSourceSize(join.Right);

        return join.JoinType switch
        {
            JoinType.Cross => leftSize * rightSize,
            JoinType.Inner => (long)(leftSize * rightSize * DEFAULT_JOIN_SELECTIVITY),
            JoinType.Left => leftSize,
            JoinType.Right => rightSize,
            JoinType.Full => leftSize + rightSize,
            _ => leftSize * rightSize
        };
    }

    #endregion

    #region Greedy Ordering Algorithm

    private List<TableSource> GreedyJoinOrdering(
        Dictionary<string, TableStatistics> tableStats,
        IReadOnlyList<JoinConditionInfo>? joinConditions)
    {
        var result = new List<TableSource>(tableStats.Count);
        var remaining = new HashSet<string>(tableStats.Keys, StringComparer.OrdinalIgnoreCase);

        // Start with the smallest table
        var first = tableStats.Values
            .OrderBy(t => t.EstimatedRowCount)
            .First();
        
        result.Add(first.Source);
        remaining.Remove(first.Alias);

        // Greedily add tables that minimize intermediate result size
        while (remaining.Count > 0)
        {
            var bestCandidate = FindBestNextTable(result, remaining, tableStats, joinConditions);
            result.Add(tableStats[bestCandidate].Source);
            remaining.Remove(bestCandidate);
        }

        return result;
    }

    private string FindBestNextTable(
        List<TableSource> currentOrder,
        HashSet<string> remaining,
        Dictionary<string, TableStatistics> tableStats,
        IReadOnlyList<JoinConditionInfo>? joinConditions)
    {
        string? bestTable = null;
        double bestCost = double.MaxValue;

        // Get aliases of tables already in the join
        var joinedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in currentOrder)
        {
            var alias = GetTableAlias(source) ?? GetTableName(source);
            if (alias != null)
                joinedAliases.Add(alias);
        }

        foreach (var candidate in remaining)
        {
            var candidateStats = tableStats[candidate];
            var cost = EstimateJoinCost(joinedAliases, candidateStats, joinConditions);

            if (cost < bestCost)
            {
                bestCost = cost;
                bestTable = candidate;
            }
        }

        return bestTable ?? remaining.First();
    }

    private double EstimateJoinCost(
        HashSet<string> joinedTables,
        TableStatistics candidate,
        IReadOnlyList<JoinConditionInfo>? joinConditions)
    {
        // Base cost is the row count of the candidate table
        double cost = candidate.EstimatedRowCount;

        // If there's a join condition connecting this table to already-joined tables,
        // reduce the cost (better selectivity)
        if (joinConditions != null)
        {
            foreach (var condition in joinConditions)
            {
                bool connectsToJoined = 
                    (joinedTables.Contains(condition.LeftTableAlias) && 
                     condition.RightTableAlias.Equals(candidate.Alias, StringComparison.OrdinalIgnoreCase)) ||
                    (joinedTables.Contains(condition.RightTableAlias) && 
                     condition.LeftTableAlias.Equals(candidate.Alias, StringComparison.OrdinalIgnoreCase));

                if (connectsToJoined)
                {
                    // Apply selectivity based on join type
                    var selectivity = condition.IsPrimaryKeyJoin 
                        ? PK_JOIN_SELECTIVITY 
                        : DEFAULT_JOIN_SELECTIVITY;
                    cost *= selectivity;
                    break; // Only apply one condition
                }
            }
        }

        return cost;
    }

    #endregion

    #region Helper Methods

    private static string? GetTableName(TableSource source)
    {
        return source switch
        {
            TableSourceSimple simple => simple.TableName,
            _ => null
        };
    }

    private static string? GetTableAlias(TableSource source)
    {
        return source switch
        {
            TableSourceSimple simple => simple.Alias ?? simple.TableName,
            TableSourceSubquery subquery => subquery.Alias,
            _ => null
        };
    }

    private static bool IsOrderUnchanged(IReadOnlyList<TableSource> original, IReadOnlyList<TableSource> optimized)
    {
        if (original.Count != optimized.Count)
            return false;

        for (int i = 0; i < original.Count; i++)
        {
            if (!ReferenceEquals(original[i], optimized[i]))
                return false;
        }

        return true;
    }

    #endregion
}

/// <summary>
/// Statistics about a table for join optimization.
/// </summary>
public sealed class TableStatistics
{
    /// <summary>
    /// The actual table name.
    /// </summary>
    public required string TableName { get; init; }

    /// <summary>
    /// The alias used in the query (or table name if no alias).
    /// </summary>
    public required string Alias { get; init; }

    /// <summary>
    /// Estimated number of rows in the table.
    /// </summary>
    public long EstimatedRowCount { get; init; }

    /// <summary>
    /// The original table source.
    /// </summary>
    public required TableSource Source { get; init; }
}

/// <summary>
/// Information about a join condition for optimization.
/// </summary>
public sealed class JoinConditionInfo
{
    /// <summary>
    /// The left table alias in the join condition.
    /// </summary>
    public required string LeftTableAlias { get; init; }

    /// <summary>
    /// The left column name in the join condition.
    /// </summary>
    public required string LeftColumnName { get; init; }

    /// <summary>
    /// The right table alias in the join condition.
    /// </summary>
    public required string RightTableAlias { get; init; }

    /// <summary>
    /// The right column name in the join condition.
    /// </summary>
    public required string RightColumnName { get; init; }

    /// <summary>
    /// Whether this is an equality join on a primary key or unique column.
    /// </summary>
    public bool IsPrimaryKeyJoin { get; init; }
}
