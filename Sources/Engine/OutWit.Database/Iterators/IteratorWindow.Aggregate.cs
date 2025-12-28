using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Specs;
using OutWit.Database.Values;

namespace OutWit.Database.Iterators;

/// <summary>
/// Aggregate window functions: COUNT, SUM, AVG, MIN, MAX.
/// </summary>
public sealed partial class IteratorWindow
{
    #region Aggregate Window Functions

    private void EvaluateAggregateWindowFunction(
        int selectIndex,
        string funcName,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices,
        SpecFrame? frame)
    {
        if (sortedIndices.Count == 0)
            return;

        switch (funcName)
        {
            case "COUNT":
                EvaluateWindowCount(selectIndex, func, sortedIndices, frame);
                break;

            case "SUM":
                EvaluateWindowSum(selectIndex, func, sortedIndices, frame);
                break;

            case "AVG":
                EvaluateWindowAvg(selectIndex, func, sortedIndices, frame);
                break;

            case "MIN":
                EvaluateWindowMin(selectIndex, func, sortedIndices, frame);
                break;

            case "MAX":
                EvaluateWindowMax(selectIndex, func, sortedIndices, frame);
                break;
        }
    }

    private void EvaluateWindowCount(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices,
        SpecFrame? frame)
    {
        for (int i = 0; i < sortedIndices.Count; i++)
        {
            var (start, end) = GetFrameBounds(i, sortedIndices.Count, frame);
            
            long count = 0;
            if (start <= end)
            {
                if (func.IsStar)
                {
                    count = end - start + 1;
                }
                else if (func.Arguments != null && func.Arguments.Count > 0)
                {
                    for (int j = start; j <= end; j++)
                    {
                        var value = m_evaluator.Evaluate(func.Arguments[0],
                            m_windowedRows![sortedIndices[j]].SourceRow);
                        if (!value.IsNull)
                            count++;
                    }
                }
                else
                {
                    count = end - start + 1;
                }
            }

            m_windowedRows![sortedIndices[i]].WindowValues[selectIndex] = WitSqlValue.FromInt(count);
        }
    }

    private void EvaluateWindowSum(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices,
        SpecFrame? frame)
    {
        if (func.Arguments == null || func.Arguments.Count == 0)
            return;

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            var (start, end) = GetFrameBounds(i, sortedIndices.Count, frame);
            
            WitSqlValue? sum = null;
            if (start <= end)
            {
                for (int j = start; j <= end; j++)
                {
                    var value = m_evaluator.Evaluate(func.Arguments[0],
                        m_windowedRows![sortedIndices[j]].SourceRow);
                    if (!value.IsNull)
                    {
                        sum = sum == null ? value : sum.Value.Add(value);
                    }
                }
            }

            m_windowedRows![sortedIndices[i]].WindowValues[selectIndex] = sum ?? WitSqlValue.Null;
        }
    }

    private void EvaluateWindowAvg(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices,
        SpecFrame? frame)
    {
        if (func.Arguments == null || func.Arguments.Count == 0)
            return;

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            var (start, end) = GetFrameBounds(i, sortedIndices.Count, frame);
            
            WitSqlValue? sum = null;
            long count = 0;
            
            if (start <= end)
            {
                for (int j = start; j <= end; j++)
                {
                    var value = m_evaluator.Evaluate(func.Arguments[0],
                        m_windowedRows![sortedIndices[j]].SourceRow);
                    if (!value.IsNull)
                    {
                        sum = sum == null ? value : sum.Value.Add(value);
                        count++;
                    }
                }
            }

            var avgValue = count > 0 && sum != null
                ? sum.Value.Divide(WitSqlValue.FromInt(count))
                : WitSqlValue.Null;

            m_windowedRows![sortedIndices[i]].WindowValues[selectIndex] = avgValue;
        }
    }

    private void EvaluateWindowMin(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices,
        SpecFrame? frame)
    {
        if (func.Arguments == null || func.Arguments.Count == 0)
            return;

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            var (start, end) = GetFrameBounds(i, sortedIndices.Count, frame);
            
            WitSqlValue? min = null;
            if (start <= end)
            {
                for (int j = start; j <= end; j++)
                {
                    var value = m_evaluator.Evaluate(func.Arguments[0],
                        m_windowedRows![sortedIndices[j]].SourceRow);
                    if (!value.IsNull && (min == null || value < min.Value))
                    {
                        min = value;
                    }
                }
            }

            m_windowedRows![sortedIndices[i]].WindowValues[selectIndex] = min ?? WitSqlValue.Null;
        }
    }

    private void EvaluateWindowMax(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices,
        SpecFrame? frame)
    {
        if (func.Arguments == null || func.Arguments.Count == 0)
            return;

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            var (start, end) = GetFrameBounds(i, sortedIndices.Count, frame);
            
            WitSqlValue? max = null;
            if (start <= end)
            {
                for (int j = start; j <= end; j++)
                {
                    var value = m_evaluator.Evaluate(func.Arguments[0],
                        m_windowedRows![sortedIndices[j]].SourceRow);
                    if (!value.IsNull && (max == null || value > max.Value))
                    {
                        max = value;
                    }
                }
            }

            m_windowedRows![sortedIndices[i]].WindowValues[selectIndex] = max ?? WitSqlValue.Null;
        }
    }

    #endregion
}
