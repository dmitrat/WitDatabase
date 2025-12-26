using OutWit.Database.Context;
using OutWit.Database.Expressions;
using OutWit.Database.Interfaces;
using OutWit.Database.Parser.Expressions;

namespace OutWit.Database.Iterators;

/// <summary>
/// Iterator that filters rows from a source iterator based on a predicate expression.
/// Returns only rows where the predicate evaluates to true.
/// </summary>
public sealed class IteratorFilter : IteratorBase
{
    #region Fields

    private readonly IResultIterator m_source;
    private readonly WitSqlExpression m_predicate;
    private readonly ExpressionEvaluator m_evaluator;
    private WitSqlRow m_current;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new filter iterator.
    /// </summary>
    /// <param name="source">The source iterator to filter.</param>
    /// <param name="predicate">The predicate expression to evaluate for each row.</param>
    /// <param name="context">The execution context.</param>
    public IteratorFilter(IResultIterator source, WitSqlExpression predicate, ContextExecution context)
    {
        m_source = source;
        m_predicate = predicate;
        m_evaluator = new ExpressionEvaluator(context);
    }

    #endregion

    #region IResultIterator

    /// <inheritdoc/>
    public override void Open()
    {
        base.Open();
        m_source.Open();
    }

    /// <inheritdoc/>
    public override bool MoveNext()
    {
        while (m_source.MoveNext())
        {
            var result = m_evaluator.Evaluate(m_predicate, m_source.Current);
            if (!result.IsNull && result.AsBool())
            {
                m_current = m_source.Current;
                return true;
            }
        }
        return false;
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        m_source.Reset();
        m_current = default;
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public override void Dispose()
    {
        m_source.Dispose();
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    public override IReadOnlyList<WitSqlColumnInfo> Schema => m_source.Schema;

    /// <inheritdoc/>
    public override WitSqlRow Current => m_current;

    #endregion
}