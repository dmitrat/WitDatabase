using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Specs;
using OutWit.Database.Values;

namespace OutWit.Database.Iterators;

/// <summary>
/// Value window functions: FIRST_VALUE, LAST_VALUE, NTH_VALUE, LAG, LEAD.
/// </summary>
public sealed partial class IteratorWindow
{
    #region Value Functions

    private void EvaluateValueFunction(
        int selectIndex,
        string funcName,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices,
        SpecFrame? frame)
    {
        switch (funcName)
        {
            case "FIRST_VALUE":
                EvaluateFirstValue(selectIndex, func, sortedIndices, frame);
                break;

            case "LAST_VALUE":
                EvaluateLastValue(selectIndex, func, sortedIndices, frame);
                break;

            case "NTH_VALUE":
                EvaluateNthValue(selectIndex, func, sortedIndices, frame);
                break;

            case "LAG":
                EvaluateLag(selectIndex, func, sortedIndices);
                break;

            case "LEAD":
                EvaluateLead(selectIndex, func, sortedIndices);
                break;
        }
    }

    private void EvaluateFirstValue(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices,
        SpecFrame? frame)
    {
        if (sortedIndices.Count == 0 || func.Arguments == null || func.Arguments.Count == 0)
            return;

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            var (start, end) = GetFrameBounds(i, sortedIndices.Count, frame);
            
            if (start > end)
            {
                // Empty frame
                m_windowedRows![sortedIndices[i]].WindowValues[selectIndex] = WitSqlValue.Null;
                continue;
            }

            var firstRow = m_windowedRows![sortedIndices[start]].SourceRow;
            var firstValue = m_evaluator.Evaluate(func.Arguments[0], firstRow);
            m_windowedRows[sortedIndices[i]].WindowValues[selectIndex] = firstValue;
        }
    }

    private void EvaluateLastValue(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices,
        SpecFrame? frame)
    {
        if (sortedIndices.Count == 0 || func.Arguments == null || func.Arguments.Count == 0)
            return;

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            var (start, end) = GetFrameBounds(i, sortedIndices.Count, frame);
            
            if (start > end)
            {
                // Empty frame
                m_windowedRows![sortedIndices[i]].WindowValues[selectIndex] = WitSqlValue.Null;
                continue;
            }

            var lastRow = m_windowedRows![sortedIndices[end]].SourceRow;
            var lastValue = m_evaluator.Evaluate(func.Arguments[0], lastRow);
            m_windowedRows[sortedIndices[i]].WindowValues[selectIndex] = lastValue;
        }
    }

    private void EvaluateNthValue(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices,
        SpecFrame? frame)
    {
        if (sortedIndices.Count == 0 || func.Arguments == null || func.Arguments.Count < 2)
            return;

        // Get N from second argument
        var n = (int)m_evaluator.Evaluate(func.Arguments[1],
            m_windowedRows![sortedIndices[0]].SourceRow).AsInt64();

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            var (start, end) = GetFrameBounds(i, sortedIndices.Count, frame);
            
            if (start > end)
            {
                m_windowedRows![sortedIndices[i]].WindowValues[selectIndex] = WitSqlValue.Null;
                continue;
            }

            int frameSize = end - start + 1;
            if (n >= 1 && n <= frameSize)
            {
                var nthRow = m_windowedRows[sortedIndices[start + n - 1]].SourceRow;
                var nthValue = m_evaluator.Evaluate(func.Arguments[0], nthRow);
                m_windowedRows[sortedIndices[i]].WindowValues[selectIndex] = nthValue;
            }
            else
            {
                m_windowedRows[sortedIndices[i]].WindowValues[selectIndex] = WitSqlValue.Null;
            }
        }
    }

    private void EvaluateLag(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices)
    {
        if (sortedIndices.Count == 0 || func.Arguments == null || func.Arguments.Count == 0)
            return;

        // Get offset (default 1) and default value
        int offset = func.Arguments.Count > 1
            ? (int)m_evaluator.Evaluate(func.Arguments[1],
                m_windowedRows![sortedIndices[0]].SourceRow).AsInt64()
            : 1;

        WitSqlValue defaultValue = func.Arguments.Count > 2
            ? m_evaluator.Evaluate(func.Arguments[2],
                m_windowedRows![sortedIndices[0]].SourceRow)
            : WitSqlValue.Null;

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            WitSqlValue value;
            if (i - offset >= 0)
            {
                var lagRow = m_windowedRows![sortedIndices[i - offset]].SourceRow;
                value = m_evaluator.Evaluate(func.Arguments[0], lagRow);
            }
            else
            {
                value = defaultValue;
            }

            m_windowedRows![sortedIndices[i]].WindowValues[selectIndex] = value;
        }
    }

    private void EvaluateLead(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices)
    {
        if (sortedIndices.Count == 0 || func.Arguments == null || func.Arguments.Count == 0)
            return;

        // Get offset (default 1) and default value
        int offset = func.Arguments.Count > 1
            ? (int)m_evaluator.Evaluate(func.Arguments[1],
                m_windowedRows![sortedIndices[0]].SourceRow).AsInt64()
            : 1;

        WitSqlValue defaultValue = func.Arguments.Count > 2
            ? m_evaluator.Evaluate(func.Arguments[2],
                m_windowedRows![sortedIndices[0]].SourceRow)
            : WitSqlValue.Null;

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            WitSqlValue value;
            if (i + offset < sortedIndices.Count)
            {
                var leadRow = m_windowedRows![sortedIndices[i + offset]].SourceRow;
                value = m_evaluator.Evaluate(func.Arguments[0], leadRow);
            }
            else
            {
                value = defaultValue;
            }

            m_windowedRows![sortedIndices[i]].WindowValues[selectIndex] = value;
        }
    }

    #endregion
}
