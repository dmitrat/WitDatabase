using OutWit.Database.Definitions;
using OutWit.Database.Model;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Optimizers;

/// <summary>
/// Query optimizer that selects indexes and pushes predicates.
/// Analyzes WHERE clauses to find the best execution strategy.
/// </summary>
public sealed class OptimizerQuery
{
    #region Constants

    /// <summary>
    /// Estimated cost for a full table scan per row.
    /// </summary>
    private const double TABLE_SCAN_COST_PER_ROW = 1.0;

    /// <summary>
    /// Estimated cost for an index seek (equality lookup).
    /// Much cheaper than full scan for selective predicates.
    /// </summary>
    private const double INDEX_SEEK_BASE_COST = 5.0;

    /// <summary>
    /// Additional cost per estimated row returned.
    /// This makes unique indexes cheaper than non-unique ones.
    /// </summary>
    private const double INDEX_FETCH_COST_PER_ROW = 1.0;

    /// <summary>
    /// Estimated cost for an index range scan per row.
    /// Cheaper than table scan but more than equality lookup.
    /// </summary>
    private const double INDEX_RANGE_COST_PER_ROW = 0.5;

    /// <summary>
    /// Default selectivity estimate when we don't have statistics.
    /// Assumes 10% of rows match a typical predicate.
    /// </summary>
    private const double DEFAULT_SELECTIVITY = 0.1;

    /// <summary>
    /// Selectivity estimate for equality predicates.
    /// Assumes 1% of rows match (higher selectivity = more selective).
    /// </summary>
    private const double EQUALITY_SELECTIVITY = 0.01;

    /// <summary>
    /// Selectivity estimate for range predicates.
    /// Assumes 20% of rows match.
    /// </summary>
    private const double RANGE_SELECTIVITY = 0.2;

    #endregion

    #region Functions

    /// <summary>
    /// Analyzes a WHERE clause and finds the best index to use.
    /// </summary>
    /// <param name="tableName">The table being queried.</param>
    /// <param name="whereClause">The WHERE clause expression.</param>
    /// <param name="availableIndexes">Available indexes on the table.</param>
    /// <param name="estimatedRowCount">Estimated row count in the table.</param>
    /// <param name="statistics">
    /// Where a value sits inside the range an index holds, when that can be had cheaply. Omitted, every
    /// range predicate falls back to <see cref="RANGE_SELECTIVITY"/> - which is what the optimizer did
    /// before it could ask, and is wrong by up to 200x in either direction.
    /// </param>
    /// <returns>The best index strategy, or null if table scan is preferred.</returns>
    public IndexStrategy? FindBestIndex(
        string tableName,
        WitSqlExpression? whereClause,
        IEnumerable<DefinitionIndex> availableIndexes,
        long estimatedRowCount,
        IIndexRangeStatistics? statistics = null)
    {
        if (whereClause == null || estimatedRowCount <= 0)
            return null;

        // Extract predicates from WHERE clause
        var predicates = ExtractPredicates(whereClause);
        if (predicates.Count == 0)
            return null;

        // Calculate cost for each index
        IndexStrategy? bestStrategy = null;
        double bestCost = estimatedRowCount * TABLE_SCAN_COST_PER_ROW; // Base case: full table scan

        foreach (var index in availableIndexes)
        {
            // Skip primary key index - we use row ID directly
            if (index.IsPrimaryKey)
                continue;

            var strategy = EvaluateIndex(index, predicates, estimatedRowCount, statistics);
            if (strategy != null && strategy.EstimatedCost < bestCost)
            {
                bestStrategy = strategy;
                bestCost = strategy.EstimatedCost;
            }
        }

        return bestStrategy;
    }

    /// <summary>
    /// Evaluates whether an index is useful for the given predicates.
    /// </summary>
    private IndexStrategy? EvaluateIndex(
        DefinitionIndex index,
        IReadOnlyList<PredicateInfo> predicates,
        long estimatedRowCount,
        IIndexRangeStatistics? statistics)
    {
        // Find predicates that match the index's first column
        // (For composite indexes, we need to match from the leftmost column)
        var firstColumn = index.Columns[0];
        var matchingPredicate = FindMatchingPredicate(firstColumn, index.GetColumnExpression(0), predicates);

        if (matchingPredicate == null)
            return null;

        // Check if index is filtered (partial) and predicate matches filter
        if (index.IsFiltered)
        {
            // For filtered indexes, we'd need to check if the predicate matches
            // For now, skip filtered indexes in automatic selection
            // They can still be used with explicit hints
            return null;
        }

        var strategy = new IndexStrategy
        {
            IndexName = index.Name,
            TableName = index.TableName,
            IndexDefinition = index,
            MatchedPredicate = matchingPredicate
        };

        // For composite indexes, check how many leading columns are covered by equality predicates.
        // Index seek requires ALL columns to be matched for correct results.
        bool isComposite = index.Columns.Count > 1;
        bool isPartialCompositeMatch = false;
        if (isComposite)
        {
            int matchedColumns = CountMatchedLeadingColumns(index, predicates);
            if (matchedColumns < index.Columns.Count)
            {
                isPartialCompositeMatch = true;
            }
        }

        // Determine access type based on predicate operator
        switch (matchingPredicate.Operator)
        {
            case BinaryOperatorType.Equal:
                if (isPartialCompositeMatch)
                {
                    // Partial composite key match: cannot use Seek (exact key lookup)
                    // because the stored key includes all index columns.
                    // Skip this index and let the table scan + filter handle it.
                    return null;
                }

                strategy.AccessType = IndexAccessType.Seek;

                if (isComposite)
                {
                    // Full composite match: collect equality values for all columns.
                    // The seek key must include all columns for correct lookup.
                    strategy.SeekValues = CollectCompositeSeekValues(index, predicates);
                }
                else
                {
                    strategy.SeekValue = matchingPredicate.CompareValue;
                }

                // For unique index, at most 1 row
                if (index.IsUnique)
                {
                    strategy.EstimatedRowsReturned = 1;
                }
                else
                {
                    strategy.EstimatedRowsReturned = Math.Max(1, (long)(estimatedRowCount * EQUALITY_SELECTIVITY));
                }
                
                // Cost = base seek cost + row fetch cost
                strategy.EstimatedCost = INDEX_SEEK_BASE_COST + (strategy.EstimatedRowsReturned * INDEX_FETCH_COST_PER_ROW);
                break;

            case BinaryOperatorType.LessThan:
            case BinaryOperatorType.LessOrEqual:
                strategy.AccessType = IndexAccessType.RangeScan;
                strategy.RangeEnd = matchingPredicate.CompareValue;
                strategy.RangeEndInclusive = matchingPredicate.Operator == BinaryOperatorType.LessOrEqual;
                strategy.EstimatedRowsReturned = RangeRows(
                    estimatedRowCount,
                    statistics?.FractionBelow(index.Name, LiteralValue(matchingPredicate.CompareValue)));
                strategy.EstimatedCost = strategy.EstimatedRowsReturned * INDEX_RANGE_COST_PER_ROW;
                break;

            case BinaryOperatorType.GreaterThan:
            case BinaryOperatorType.GreaterOrEqual:
                strategy.AccessType = IndexAccessType.RangeScan;
                strategy.RangeStart = matchingPredicate.CompareValue;
                strategy.RangeStartInclusive = matchingPredicate.Operator == BinaryOperatorType.GreaterOrEqual;

                // Above the bound rather than below it, which is the whole point of asking: the same
                // predicate shape can match one row or the entire table depending on where the bound
                // falls in the data.
                var below = statistics?.FractionBelow(index.Name, LiteralValue(matchingPredicate.CompareValue));
                strategy.EstimatedRowsReturned = RangeRows(estimatedRowCount, below.HasValue ? 1 - below.Value : null);
                strategy.EstimatedCost = strategy.EstimatedRowsReturned * INDEX_RANGE_COST_PER_ROW;
                break;

            default:
                // Operator not suitable for index scan
                return null;
        }

        // Check for BETWEEN (combined predicates on same column)
        TryOptimizeForBetween(strategy, firstColumn, predicates, estimatedRowCount, statistics, index.Name);

        return strategy;
    }

    /// <summary>
    /// The constant a predicate compares against, or null when it does not compare against one.
    /// </summary>
    /// <remarks>
    /// A predicate carries an <b>expression</b>, not a value - <c>WHERE a &gt; b</c> is as legal as
    /// <c>WHERE a &gt; 5</c> - and only a literal says where in the data the bound falls. Anything else
    /// gets no estimate rather than a wrong one, which is why this returns null instead of guessing.
    /// </remarks>
    private static object? LiteralValue(WitSqlExpression? expression)
    {
        return expression is WitSqlExpressionLiteral literal ? literal.Value : null;
    }

    /// <summary>
    /// Rows a one-sided range is expected to return, from a measured fraction where there is one.
    /// </summary>
    /// <remarks>
    /// The floor of one row is deliberate and matters more than it looks: an estimate of zero makes an
    /// index look free, and the optimizer would then choose it for a predicate that matches nothing and
    /// for one that matches everything alike.
    /// </remarks>
    private static long RangeRows(long estimatedRowCount, double? fraction)
    {
        var selectivity = fraction.HasValue
            ? Math.Clamp(fraction.Value, 0.0, 1.0)
            : RANGE_SELECTIVITY;

        return Math.Max(1, (long)(estimatedRowCount * selectivity));
    }

    /// <summary>
    /// Rows a two-sided range is expected to return: what is below the upper bound, less what is below
    /// the lower one.
    /// </summary>
    /// <remarks>
    /// Falls back to half the one-sided constant, which is what this did before there was anything to
    /// measure. The bounds arrive in whichever order the predicates were written, so the difference is
    /// taken by absolute value rather than by assuming which is which.
    /// </remarks>
    private static long BetweenRows(
        long estimatedRowCount,
        IIndexRangeStatistics? statistics,
        string indexName,
        object? firstBound,
        object? secondBound)
    {
        var first = statistics?.FractionBelow(indexName, firstBound);
        var second = statistics?.FractionBelow(indexName, secondBound);

        if (!first.HasValue || !second.HasValue)
            return Math.Max(1, (long)(estimatedRowCount * RANGE_SELECTIVITY * 0.5));

        return RangeRows(estimatedRowCount, Math.Abs(second.Value - first.Value));
    }

    /// <summary>
    /// Tries to optimize the strategy if there are both lower and upper bounds (BETWEEN).
    /// </summary>
    private void TryOptimizeForBetween(
        IndexStrategy strategy,
        string columnName,
        IReadOnlyList<PredicateInfo> predicates,
        long estimatedRowCount,
        IIndexRangeStatistics? statistics,
        string indexName)
    {
        // If we already have a seek, skip
        if (strategy.AccessType == IndexAccessType.Seek)
            return;

        // Look for complementary predicate
        foreach (var pred in predicates)
        {
            if (!pred.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (pred == strategy.MatchedPredicate)
                continue;

            bool isLowerBound = pred.Operator == BinaryOperatorType.GreaterThan || 
                               pred.Operator == BinaryOperatorType.GreaterOrEqual;
            bool isUpperBound = pred.Operator == BinaryOperatorType.LessThan || 
                               pred.Operator == BinaryOperatorType.LessOrEqual;

            // If we have range end and found lower bound
            if (strategy.RangeEnd != null && isLowerBound)
            {
                strategy.RangeStart = pred.CompareValue;
                strategy.RangeStartInclusive = pred.Operator == BinaryOperatorType.GreaterOrEqual;
                strategy.EstimatedRowsReturned = BetweenRows(
                    estimatedRowCount, statistics, indexName,
                    LiteralValue(strategy.MatchedPredicate?.CompareValue), LiteralValue(pred.CompareValue));
                strategy.EstimatedCost = strategy.EstimatedRowsReturned * INDEX_RANGE_COST_PER_ROW;
                break;
            }

            // If we have range start and found upper bound
            if (strategy.RangeStart != null && isUpperBound)
            {
                strategy.RangeEnd = pred.CompareValue;
                strategy.RangeEndInclusive = pred.Operator == BinaryOperatorType.LessOrEqual;
                strategy.EstimatedRowsReturned = BetweenRows(
                    estimatedRowCount, statistics, indexName,
                    LiteralValue(strategy.MatchedPredicate?.CompareValue), LiteralValue(pred.CompareValue));
                strategy.EstimatedCost = strategy.EstimatedRowsReturned * INDEX_RANGE_COST_PER_ROW;
                break;
            }
        }
    }

    /// <summary>
    /// Finds a predicate that matches an index column.
    /// </summary>
    private PredicateInfo? FindMatchingPredicate(
        string columnName,
        string? expressionText,
        IReadOnlyList<PredicateInfo> predicates)
    {
        foreach (var pred in predicates)
        {
            // Match column name (simple index)
            if (pred.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase))
            {
                // Ensure the column is on the left side of the comparison
                // (we already normalize this in ExtractPredicates)
                return pred;
            }

            // For expression indexes (e.g., LOWER(email)), match the expression
            if (expressionText != null && pred.ExpressionText != null)
            {
                if (pred.ExpressionText.Equals(expressionText, StringComparison.OrdinalIgnoreCase))
                    return pred;
            }
        }

        return null;
    }

    /// <summary>
    /// Counts how many leading columns of a composite index have equality predicates.
    /// For a correct index seek, all columns must be covered.
    /// </summary>
    private int CountMatchedLeadingColumns(DefinitionIndex index, IReadOnlyList<PredicateInfo> predicates)
    {
        int matched = 0;
        for (int i = 0; i < index.Columns.Count; i++)
        {
            var columnName = index.Columns[i];
            var expressionText = index.GetColumnExpression(i);
            bool found = false;

            foreach (var pred in predicates)
            {
                if (pred.Operator != BinaryOperatorType.Equal)
                    continue;

                if (pred.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }

                if (expressionText != null && pred.ExpressionText != null &&
                    pred.ExpressionText.Equals(expressionText, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                break;

            matched++;
        }

        return matched;
    }

    /// <summary>
    /// Collects equality predicate values for all columns of a composite index, in column order.
    /// Called only when <see cref="CountMatchedLeadingColumns"/> confirmed all columns are covered.
    /// </summary>
    private static List<WitSqlExpression> CollectCompositeSeekValues(
        DefinitionIndex index, IReadOnlyList<PredicateInfo> predicates)
    {
        var values = new List<WitSqlExpression>(index.Columns.Count);

        for (int i = 0; i < index.Columns.Count; i++)
        {
            var columnName = index.Columns[i];
            var expressionText = index.GetColumnExpression(i);

            foreach (var pred in predicates)
            {
                if (pred.Operator != BinaryOperatorType.Equal)
                    continue;

                if (pred.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase) ||
                    (expressionText != null && pred.ExpressionText != null &&
                     pred.ExpressionText.Equals(expressionText, StringComparison.OrdinalIgnoreCase)))
                {
                    values.Add(pred.CompareValue);
                    break;
                }
            }
        }

        return values;
    }

    /// <summary>
    /// Extracts usable predicates from a WHERE clause expression.
    /// </summary>
    private List<PredicateInfo> ExtractPredicates(WitSqlExpression expression)
    {
        var predicates = new List<PredicateInfo>();
        ExtractPredicatesRecursive(expression, predicates);
        return predicates;
    }

    private void ExtractPredicatesRecursive(WitSqlExpression expression, List<PredicateInfo> predicates)
    {
        switch (expression)
        {
            case WitSqlExpressionBinary binary:
                // AND - extract predicates from both sides
                if (binary.Operator == BinaryOperatorType.And)
                {
                    ExtractPredicatesRecursive(binary.Left, predicates);
                    ExtractPredicatesRecursive(binary.Right, predicates);
                    return;
                }

                // Comparison operators
                if (IsComparisonOperator(binary.Operator))
                {
                    var predicate = TryExtractPredicate(binary);
                    if (predicate != null)
                    {
                        predicates.Add(predicate);
                    }
                }
                break;

            case WitSqlExpressionBetween between:
                // BETWEEN is equivalent to two range predicates
                if (between.Expression is WitSqlExpressionColumnRef col && !between.IsNot)
                {
                    if (IsConstant(between.Low))
                    {
                        predicates.Add(new PredicateInfo
                        {
                            ColumnName = col.ColumnName,
                            TableAlias = col.TableName,
                            Operator = BinaryOperatorType.GreaterOrEqual,
                            CompareValue = between.Low,
                            OriginalExpression = between
                        });
                    }
                    if (IsConstant(between.High))
                    {
                        predicates.Add(new PredicateInfo
                        {
                            ColumnName = col.ColumnName,
                            TableAlias = col.TableName,
                            Operator = BinaryOperatorType.LessOrEqual,
                            CompareValue = between.High,
                            OriginalExpression = between
                        });
                    }
                }
                break;

            case WitSqlExpressionIn inExpr:
                // IN with small value list can use index
                if (inExpr.Expression is WitSqlExpressionColumnRef inCol && 
                    !inExpr.IsNot && 
                    inExpr.Values is { Count: 1 } && 
                    IsConstant(inExpr.Values[0]))
                {
                    // Single value IN is equivalent to equality
                    predicates.Add(new PredicateInfo
                    {
                        ColumnName = inCol.ColumnName,
                        TableAlias = inCol.TableName,
                        Operator = BinaryOperatorType.Equal,
                        CompareValue = inExpr.Values[0],
                        OriginalExpression = inExpr
                    });
                }
                break;
        }
    }

    private PredicateInfo? TryExtractPredicate(WitSqlExpressionBinary binary)
    {
        // Check if one side is a column reference and the other is a constant
        WitSqlExpressionColumnRef? column = null;
        WitSqlExpression? value = null;
        var op = binary.Operator;

        if (binary.Left is WitSqlExpressionColumnRef leftCol && IsConstant(binary.Right))
        {
            column = leftCol;
            value = binary.Right;
        }
        else if (binary.Right is WitSqlExpressionColumnRef rightCol && IsConstant(binary.Left))
        {
            column = rightCol;
            value = binary.Left;
            // Flip operator when column is on right
            op = FlipOperator(op);
        }

        // Check for function calls (expression indexes)
        string? expressionText = null;
        if (binary.Left is WitSqlExpressionFunctionCall funcCall && IsConstant(binary.Right))
        {
            // For function indexes like LOWER(email), extract the expression
            expressionText = ExtractFunctionExpression(funcCall);
            if (expressionText != null && funcCall.Arguments is { Count: 1 } && 
                funcCall.Arguments[0] is WitSqlExpressionColumnRef funcColRef)
            {
                column = funcColRef;
                value = binary.Right;
            }
        }

        if (column == null || value == null)
            return null;

        return new PredicateInfo
        {
            ColumnName = column.ColumnName,
            TableAlias = column.TableName,
            Operator = op,
            CompareValue = value,
            OriginalExpression = binary,
            ExpressionText = expressionText
        };
    }

    private static string? ExtractFunctionExpression(WitSqlExpressionFunctionCall func)
    {
        if (func.Arguments is not { Count: 1 } || 
            func.Arguments[0] is not WitSqlExpressionColumnRef colRef)
            return null;

        // Return normalized expression like "LOWER(columnName)"
        return $"{func.FunctionName.ToUpperInvariant()}({colRef.ColumnName})";
    }

    private static bool IsComparisonOperator(BinaryOperatorType op)
    {
        return op switch
        {
            BinaryOperatorType.Equal => true,
            BinaryOperatorType.NotEqual => false, // Not useful for index
            BinaryOperatorType.LessThan => true,
            BinaryOperatorType.LessOrEqual => true,
            BinaryOperatorType.GreaterThan => true,
            BinaryOperatorType.GreaterOrEqual => true,
            _ => false
        };
    }

    private static BinaryOperatorType FlipOperator(BinaryOperatorType op)
    {
        return op switch
        {
            BinaryOperatorType.LessThan => BinaryOperatorType.GreaterThan,
            BinaryOperatorType.LessOrEqual => BinaryOperatorType.GreaterOrEqual,
            BinaryOperatorType.GreaterThan => BinaryOperatorType.LessThan,
            BinaryOperatorType.GreaterOrEqual => BinaryOperatorType.LessOrEqual,
            _ => op // Equal and NotEqual are symmetric
        };
    }

    private static bool IsConstant(WitSqlExpression expression)
    {
        return expression switch
        {
            WitSqlExpressionLiteral => true,
            WitSqlExpressionParameter => true, // Parameters are constant at query time
            _ => false
        };
    }

    #endregion
}