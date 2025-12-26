using OutWit.Database.Context;
using OutWit.Database.Definitions;
using OutWit.Database.Interfaces;
using OutWit.Database.Types;
using OutWit.Database.Values;
using NSubstitute;

namespace OutWit.Database.Tests.Expressions;

/// <summary>
/// Base class for ExpressionEvaluator tests providing common test infrastructure.
/// </summary>
public abstract class ExpressionEvaluatorTestsBase
{
    #region Fields

    protected ContextExecution m_context = null!;
    protected IDatabase m_database = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_database = Substitute.For<IDatabase>();
        m_context = new ContextExecution
        {
            Database = m_database,
            CancellationToken = CancellationToken.None
        };
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates an empty row for expression evaluation.
    /// </summary>
    protected static WitSqlRow CreateEmptyRow()
    {
        return new WitSqlRow([], []);
    }

    /// <summary>
    /// Creates a row with the specified columns and values.
    /// </summary>
    protected static WitSqlRow CreateRow(params (string name, WitSqlValue value)[] columns)
    {
        var names = columns.Select(c => c.name).ToArray();
        var values = columns.Select(c => c.value).ToArray();
        return new WitSqlRow(values, names);
    }

    /// <summary>
    /// Creates a row with integer values.
    /// </summary>
    protected static WitSqlRow CreateRowWithInts(params (string name, long value)[] columns)
    {
        var names = columns.Select(c => c.name).ToArray();
        var values = columns.Select(c => WitSqlValue.FromInt(c.value)).ToArray();
        return new WitSqlRow(values, names);
    }

    /// <summary>
    /// Creates a row with string values.
    /// </summary>
    protected static WitSqlRow CreateRowWithStrings(params (string name, string value)[] columns)
    {
        var names = columns.Select(c => c.name).ToArray();
        var values = columns.Select(c => WitSqlValue.FromText(c.value)).ToArray();
        return new WitSqlRow(values, names);
    }

    #endregion
}
