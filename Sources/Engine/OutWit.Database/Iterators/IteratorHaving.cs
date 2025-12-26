using OutWit.Database.Context;
using OutWit.Database.Expressions;
using OutWit.Database.Interfaces;
using OutWit.Database.Parser.Expressions;

namespace OutWit.Database.Iterators;

/// <summary>
/// Iterator that filters grouped rows based on a HAVING clause.
/// Similar to IteratorFilter but operates on aggregated results.
/// </summary>
public sealed class IteratorHaving : IteratorBase
{
    #region Fields

    private readonly IResultIterator m_source;
    private readonly WitSqlExpression m_havingPredicate;
    private readonly ExpressionEvaluator m_evaluator;
    private WitSqlRow m_current;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new HAVING filter iterator.
    /// </summary>
    /// <param name="source">The source iterator (typically from GROUP BY).</param>
    /// <param name="havingPredicate">The HAVING predicate expression.</param>
    /// <param name="context">The execution context.</param>
    public IteratorHaving(IResultIterator source, WitSqlExpression havingPredicate, ContextExecution context)
    {
        m_source = source;
        m_havingPredicate = havingPredicate;
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
            var result = m_evaluator.Evaluate(m_havingPredicate, m_source.Current);
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