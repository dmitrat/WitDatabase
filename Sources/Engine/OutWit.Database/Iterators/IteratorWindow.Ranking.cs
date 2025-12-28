using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.Specs;
using OutWit.Database.Values;

namespace OutWit.Database.Iterators;

/// <summary>
/// Ranking window functions: ROW_NUMBER, RANK, DENSE_RANK, NTILE, PERCENT_RANK, CUME_DIST.
/// </summary>
public sealed partial class IteratorWindow
{
    #region Ranking Functions

    private void EvaluateRankingFunction(
        int selectIndex,
        string funcName,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices,
        int partitionSize)
    {
        switch (funcName)
        {
            case "ROW_NUMBER":
                EvaluateRowNumber(selectIndex, sortedIndices);
                break;

            case "RANK":
                EvaluateRank(selectIndex, func, sortedIndices);
                break;

            case "DENSE_RANK":
                EvaluateDenseRank(selectIndex, func, sortedIndices);
                break;

            case "NTILE":
                EvaluateNtile(selectIndex, func, sortedIndices);
                break;

            case "PERCENT_RANK":
                EvaluatePercentRank(selectIndex, func, sortedIndices, partitionSize);
                break;

            case "CUME_DIST":
                EvaluateCumeDist(selectIndex, func, sortedIndices, partitionSize);
                break;
        }
    }

    private void EvaluateRowNumber(int selectIndex, List<int> sortedIndices)
    {
        for (int i = 0; i < sortedIndices.Count; i++)
        {
            m_windowedRows![sortedIndices[i]].WindowValues[selectIndex] =
                WitSqlValue.FromInt(i + 1);
        }
    }

    private void EvaluateRank(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices)
    {
        if (sortedIndices.Count == 0)
            return;

        var orderBy = func.Over?.OrderBy;
        int currentRank = 1;
        int sameRankCount = 0;
        WitSqlValue? previousKey = null;

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            var row = m_windowedRows![sortedIndices[i]].SourceRow;
            var currentKey = ComputeOrderKey(row, orderBy);

            if (previousKey != null && !currentKey.Equals(previousKey.Value))
            {
                currentRank += sameRankCount;
                sameRankCount = 1;
            }
            else
            {
                sameRankCount++;
            }

            m_windowedRows[sortedIndices[i]].WindowValues[selectIndex] =
                WitSqlValue.FromInt(currentRank);

            previousKey = currentKey;
        }
    }

    private void EvaluateDenseRank(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices)
    {
        if (sortedIndices.Count == 0)
            return;

        var orderBy = func.Over?.OrderBy;
        int currentRank = 0;
        WitSqlValue? previousKey = null;

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            var row = m_windowedRows![sortedIndices[i]].SourceRow;
            var currentKey = ComputeOrderKey(row, orderBy);

            if (previousKey == null || !currentKey.Equals(previousKey.Value))
            {
                currentRank++;
            }

            m_windowedRows[sortedIndices[i]].WindowValues[selectIndex] =
                WitSqlValue.FromInt(currentRank);

            previousKey = currentKey;
        }
    }

    private void EvaluateNtile(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices)
    {
        if (sortedIndices.Count == 0 || func.Arguments == null || func.Arguments.Count == 0)
            return;

        // Get the number of buckets from the first argument
        var buckets = (int)m_evaluator.Evaluate(func.Arguments[0],
            m_windowedRows![sortedIndices[0]].SourceRow).AsInt64();

        if (buckets <= 0)
            buckets = 1;

        int rowsPerBucket = sortedIndices.Count / buckets;
        int extraRows = sortedIndices.Count % buckets;

        int currentBucket = 1;
        int rowsInCurrentBucket = 0;
        int bucketSize = rowsPerBucket + (extraRows > 0 ? 1 : 0);

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            m_windowedRows![sortedIndices[i]].WindowValues[selectIndex] =
                WitSqlValue.FromInt(currentBucket);

            rowsInCurrentBucket++;

            if (rowsInCurrentBucket >= bucketSize && currentBucket < buckets)
            {
                currentBucket++;
                rowsInCurrentBucket = 0;
                extraRows--;
                bucketSize = rowsPerBucket + (extraRows > 0 ? 1 : 0);
            }
        }
    }

    private void EvaluatePercentRank(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices,
        int partitionSize)
    {
        if (partitionSize <= 1)
        {
            foreach (var idx in sortedIndices)
            {
                m_windowedRows![idx].WindowValues[selectIndex] = WitSqlValue.FromReal(0.0);
            }
            return;
        }

        var orderBy = func.Over?.OrderBy;
        int currentRank = 1;
        int sameRankCount = 0;
        WitSqlValue? previousKey = null;

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            var row = m_windowedRows![sortedIndices[i]].SourceRow;
            var currentKey = ComputeOrderKey(row, orderBy);

            if (previousKey != null && !currentKey.Equals(previousKey.Value))
            {
                currentRank += sameRankCount;
                sameRankCount = 1;
            }
            else
            {
                sameRankCount++;
            }

            double percentRank = (double)(currentRank - 1) / (partitionSize - 1);
            m_windowedRows[sortedIndices[i]].WindowValues[selectIndex] =
                WitSqlValue.FromReal(percentRank);

            previousKey = currentKey;
        }
    }

    private void EvaluateCumeDist(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices,
        int partitionSize)
    {
        if (partitionSize == 0)
            return;

        var orderBy = func.Over?.OrderBy;

        // For CUME_DIST, we need to count how many rows have values <= current row
        for (int i = 0; i < sortedIndices.Count; i++)
        {
            var currentRow = m_windowedRows![sortedIndices[i]].SourceRow;
            var currentKey = ComputeOrderKey(currentRow, orderBy);

            // Count rows with value <= current (including ties)
            int countLessOrEqual = 0;
            for (int j = 0; j < sortedIndices.Count; j++)
            {
                var compareRow = m_windowedRows[sortedIndices[j]].SourceRow;
                var compareKey = ComputeOrderKey(compareRow, orderBy);

                if (CompareOrderKeys(compareKey, currentKey, orderBy) <= 0)
                {
                    countLessOrEqual++;
                }
            }

            double cumeDist = (double)countLessOrEqual / partitionSize;
            m_windowedRows[sortedIndices[i]].WindowValues[selectIndex] =
                WitSqlValue.FromReal(cumeDist);
        }
    }

    #endregion
}
