using OutWit.Database.Iterators;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Sql;

namespace OutWit.Database.Optimizers;

/// <summary>
/// Analyzes join conditions to extract equi-join keys and residual conditions.
/// Used by query planner to select optimal join algorithm.
/// </summary>
public static class OptimizerJoinCondition
{
    #region Public Methods

    /// <summary>
    /// Analyzes a join ON condition and extracts equi-join keys.
    /// </summary>
    /// <param name="onCondition">The ON condition expression.</param>
    /// <param name="leftSchema">
    /// The schema of the join's LEFT input, which is what says where each column of a key comes
    /// from.
    /// </param>
    /// <returns>Result containing equi-join keys and any residual conditions.</returns>
    /// <remarks>
    /// <para>
    /// <b><paramref name="leftSchema"/> is required, and that is the whole of a defect shipped in
    /// 14.0.0.</b> A key pair used to be built as <c>LeftKey = binary.Left, RightKey = binary.Right</c>
    /// - taking the ORDER THE CONDITION WAS WRITTEN IN for the order of the join's inputs. So
    /// <c>ON c.Id = o.CustomerId</c> worked and <c>ON o.CustomerId = c.Id</c>, the same condition,
    /// failed at execution with <c>Column 'CustomerId' not found</c>: the hash join looked for the
    /// right table's column in rows of the left one.
    /// </para>
    /// <para>
    /// A schema is the only thing that can answer which side a column is on, so it is a parameter
    /// rather than an option - the compiler then asks the question at every call site.
    /// </para>
    /// </remarks>
    public static JoinConditionAnalysis Analyze(
        WitSqlExpression? onCondition,
        IReadOnlyList<WitSqlColumnInfo> leftSchema)
    {
        if (onCondition == null)
        {
            return new JoinConditionAnalysis
            {
                EquiJoinKeys = [],
                ResidualCondition = null
            };
        }

        var equiKeys = new List<IteratorHashJoin.JoinKeyPair>();
        var residualParts = new List<WitSqlExpression>();

        var leftTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in leftSchema)
        {
            if (!string.IsNullOrEmpty(column.TableName))
                leftTables.Add(column.TableName);
        }

        AnalyzeRecursive(onCondition, leftTables, equiKeys, residualParts);

        return new JoinConditionAnalysis
        {
            EquiJoinKeys = equiKeys,
            ResidualCondition = CombineWithAnd(residualParts)
        };
    }

    /// <summary>
    /// Determines if hash join should be used based on table sizes and join condition.
    /// </summary>
    /// <param name="leftRowCount">Estimated row count of left table.</param>
    /// <param name="rightRowCount">Estimated row count of right table.</param>
    /// <param name="analysis">The join condition analysis.</param>
    /// <returns>True if hash join is preferred over nested loop.</returns>
    public static bool ShouldUseHashJoin(long leftRowCount, long rightRowCount, JoinConditionAnalysis analysis)
    {
        // Hash join requires at least one equi-join key
        if (analysis.EquiJoinKeys.Count == 0)
            return false;

        // Hash join has build overhead, only beneficial for larger tables
        // Threshold: if nested loop would do more than ~1000 comparisons
        const long HASH_JOIN_THRESHOLD = 32;

        var smallerTable = Math.Min(leftRowCount, rightRowCount);
        var largerTable = Math.Max(leftRowCount, rightRowCount);

        // Nested loop cost: O(N � M)
        // Hash join cost: O(N + M) + hash overhead
        // Use hash join when N � M > threshold � (N + M)
        var nestedLoopCost = leftRowCount * rightRowCount;
        var hashJoinCost = (leftRowCount + rightRowCount) * HASH_JOIN_THRESHOLD;

        return nestedLoopCost > hashJoinCost;
    }

    /// <summary>
    /// Determines which side should be the build side for hash join.
    /// Generally the smaller table should be build side.
    /// </summary>
    /// <param name="leftRowCount">Estimated row count of left table.</param>
    /// <param name="rightRowCount">Estimated row count of right table.</param>
    /// <returns>True if left should be build side, false for right.</returns>
    public static bool ShouldBuildLeft(long leftRowCount, long rightRowCount)
    {
        // Build the smaller table into hash table
        // This minimizes memory usage and hash table lookups
        return leftRowCount <= rightRowCount;
    }

    #endregion

    #region Private Methods

    private static void AnalyzeRecursive(
        WitSqlExpression expression,
        HashSet<string> leftTables,
        List<IteratorHashJoin.JoinKeyPair> equiKeys,
        List<WitSqlExpression> residualParts)
    {
        switch (expression)
        {
            case WitSqlExpressionBinary binary:
                if (binary.Operator == BinaryOperatorType.And)
                {
                    // Recursively process AND conditions
                    AnalyzeRecursive(binary.Left, leftTables, equiKeys, residualParts);
                    AnalyzeRecursive(binary.Right, leftTables, equiKeys, residualParts);
                }
                else if (binary.Operator == BinaryOperatorType.Equal)
                {
                    // Check if this is an equi-join condition (column = column)
                    if (TryExtractEquiJoinKey(binary, leftTables, out var keyPair))
                    {
                        equiKeys.Add(keyPair!);
                    }
                    else
                    {
                        // Not a simple column = column, add to residual
                        residualParts.Add(expression);
                    }
                }
                else
                {
                    // Other operators go to residual
                    residualParts.Add(expression);
                }
                break;

            default:
                // All other expressions are residual
                residualParts.Add(expression);
                break;
        }
    }

    private static bool TryExtractEquiJoinKey(
        WitSqlExpressionBinary binary,
        HashSet<string> leftTables,
        out IteratorHashJoin.JoinKeyPair? keyPair)
    {
        keyPair = null;

        if (binary.Operator != BinaryOperatorType.Equal)
            return false;

        // Both sides must be column references from different tables
        if (binary.Left is not WitSqlExpressionColumnRef leftCol ||
            binary.Right is not WitSqlExpressionColumnRef rightCol)
            return false;

        // Must have table qualifiers to distinguish join sides
        // If no table qualifier, we can't determine which side the column belongs to
        // In that case, treat as residual and let IteratorJoin handle it
        if (leftCol.TableName == null || rightCol.TableName == null)
            return false;

        // If both have same table name, it's not a join condition
        if (leftCol.TableName.Equals(rightCol.TableName, StringComparison.OrdinalIgnoreCase))
            return false;

        // WHICH SIDE each column is on, which is not the same question as which side of the
        // EQUALS SIGN it was written on. A condition means the same thing either way round, and
        // until 14.0.1 this pair was built from the written order alone - so half of every join
        // anyone wrote naturally failed with "Column not found" (KnownIssues 25).
        var leftIsFromTheLeft = leftTables.Contains(leftCol.TableName);
        var rightIsFromTheLeft = leftTables.Contains(rightCol.TableName);

        // Both from the left input is not a join key at all - it is a filter on that input, and
        // hashing on it would look for the second column in the rows of the other side. The whole
        // condition goes to the residual, where it is evaluated over the joined row instead.
        if (leftIsFromTheLeft && rightIsFromTheLeft)
            return false;

        // NEITHER attributable is a different case, and it must keep the old behaviour rather than
        // join the one above. A source can report no table name at all - INFORMATION_SCHEMA does,
        // and its columns then resolve by name over the joined row, where the same name appears
        // once per side. Sending those to the residual turned Studio's primary-key query into a
        // cross product: measured, five of its cases went red before this branch was written.
        if (!leftIsFromTheLeft && !rightIsFromTheLeft)
        {
            keyPair = new IteratorHashJoin.JoinKeyPair { LeftKey = binary.Left, RightKey = binary.Right };
            return true;
        }

        keyPair = leftIsFromTheLeft
            ? new IteratorHashJoin.JoinKeyPair { LeftKey = binary.Left, RightKey = binary.Right }
            : new IteratorHashJoin.JoinKeyPair { LeftKey = binary.Right, RightKey = binary.Left };

        return true;
    }

    private static WitSqlExpression? CombineWithAnd(List<WitSqlExpression> parts)
    {
        if (parts.Count == 0)
            return null;

        if (parts.Count == 1)
            return parts[0];

        // Build right-associative AND chain
        var result = parts[^1];
        for (int i = parts.Count - 2; i >= 0; i--)
        {
            result = new WitSqlExpressionBinary
            {
                Left = parts[i],
                Operator = BinaryOperatorType.And,
                Right = result
            };
        }

        return result;
    }

    #endregion
}

/// <summary>
/// Result of analyzing a join condition.
/// </summary>
public sealed class JoinConditionAnalysis
{
    /// <summary>
    /// Equi-join key pairs extracted from the condition.
    /// </summary>
    public required IReadOnlyList<IteratorHashJoin.JoinKeyPair> EquiJoinKeys { get; init; }

    /// <summary>
    /// Remaining conditions that couldn't be converted to equi-join keys.
    /// </summary>
    public WitSqlExpression? ResidualCondition { get; init; }

    /// <summary>
    /// True if any equi-join keys were found.
    /// </summary>
    public bool HasEquiJoinKeys => EquiJoinKeys.Count > 0;
}
