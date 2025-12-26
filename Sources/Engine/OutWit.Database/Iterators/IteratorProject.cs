using OutWit.Database.Context;
using OutWit.Database.Expressions;
using OutWit.Database.Interfaces;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Iterators;

/// <summary>
/// Iterator that projects columns from source rows based on a SELECT list.
/// Evaluates expressions for each selected column.
/// </summary>
public sealed class IteratorProject : IteratorBase
{
    #region Fields

    private readonly IResultIterator m_source;
    private readonly IReadOnlyList<ClauseSelectItem> m_selectList;
    private readonly ExpressionEvaluator m_evaluator;
    private readonly IReadOnlyList<WitSqlColumnInfo> m_schema;
    private WitSqlRow m_current;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new projection iterator.
    /// </summary>
    /// <param name="source">The source iterator.</param>
    /// <param name="selectList">The SELECT list defining columns to project.</param>
    /// <param name="context">The execution context.</param>
    public IteratorProject(IResultIterator source, IReadOnlyList<ClauseSelectItem> selectList, ContextExecution context)
    {
        m_source = source;
        m_selectList = selectList;
        m_evaluator = new ExpressionEvaluator(context);
        m_schema = BuildSchema(selectList);
    }

    #endregion

    #region Functions

    private static List<WitSqlColumnInfo> BuildSchema(IReadOnlyList<ClauseSelectItem> selectList)
    {
        var schema = new List<WitSqlColumnInfo>(selectList.Count);
        for (int i = 0; i < selectList.Count; i++)
        {
            var item = selectList[i];
            var name = item.Alias ?? GetColumnName(item.Expression, i);
            var type = InferColumnType(item.Expression);
            schema.Add(new WitSqlColumnInfo { Name = name, Type = type });
        }
        return schema;
    }

    private static string GetColumnName(WitSqlExpression? expression, int index)
    {
        return expression switch
        {
            WitSqlExpressionColumnRef col => col.ColumnName,
            _ => $"column{index}"
        };
    }

    private static WitSqlType InferColumnType(WitSqlExpression? expression)
    {
        return expression switch
        {
            WitSqlExpressionLiteral { Type: LiteralType.Integer } => WitSqlType.Integer,
            WitSqlExpressionLiteral { Type: LiteralType.Real } => WitSqlType.Real,
            WitSqlExpressionLiteral { Type: LiteralType.Boolean } => WitSqlType.Boolean,
            WitSqlExpressionLiteral { Type: LiteralType.String } => WitSqlType.Text,
            WitSqlExpressionLiteral { Type: LiteralType.Blob } => WitSqlType.Blob,
            WitSqlExpressionLiteral { Type: LiteralType.Null } => WitSqlType.Null,
            WitSqlExpressionUnary { Operator: UnaryOperatorType.Not } => WitSqlType.Boolean,
            WitSqlExpressionBinary { Operator: var op } when IsComparisonOperator(op) => WitSqlType.Boolean,
            WitSqlExpressionBinary { Operator: var op } when IsArithmeticOperator(op) => WitSqlType.Real,
            WitSqlExpressionBetween => WitSqlType.Boolean,
            WitSqlExpressionIn => WitSqlType.Boolean,
            WitSqlExpressionLike => WitSqlType.Boolean,
            WitSqlExpressionIsNull => WitSqlType.Boolean,
            _ => WitSqlType.Text
        };
    }

    private static bool IsComparisonOperator(BinaryOperatorType op)
    {
        return op is BinaryOperatorType.Equal or BinaryOperatorType.NotEqual
            or BinaryOperatorType.LessThan or BinaryOperatorType.LessOrEqual
            or BinaryOperatorType.GreaterThan or BinaryOperatorType.GreaterOrEqual
            or BinaryOperatorType.And or BinaryOperatorType.Or;
    }

    private static bool IsArithmeticOperator(BinaryOperatorType op)
    {
        return op is BinaryOperatorType.Add or BinaryOperatorType.Subtract
            or BinaryOperatorType.Multiply or BinaryOperatorType.Divide
            or BinaryOperatorType.Modulo;
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
        if (!m_source.MoveNext())
            return false;

        var values = new WitSqlValue[m_selectList.Count];
        var names = new string[m_selectList.Count];

        for (int i = 0; i < m_selectList.Count; i++)
        {
            var item = m_selectList[i];
            values[i] = item.Expression != null
                ? m_evaluator.Evaluate(item.Expression, m_source.Current)
                : WitSqlValue.Null;
            names[i] = m_schema[i].Name;
        }

        m_current = new WitSqlRow(values, names);
        return true;
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
    public override IReadOnlyList<WitSqlColumnInfo> Schema => m_schema;

    /// <inheritdoc/>
    public override WitSqlRow Current => m_current;

    #endregion
}