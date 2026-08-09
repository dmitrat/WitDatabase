using OutWit.Database.Context;
using OutWit.Database.Expressions;
using OutWit.Database.Interfaces;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Sql;
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
        m_schema = BuildSchema(selectList, source.Schema);
    }

    #endregion

    #region Functions

    private static List<WitSqlColumnInfo> BuildSchema(IReadOnlyList<ClauseSelectItem> selectList, IReadOnlyList<WitSqlColumnInfo> sourceSchema)
    {
        var schema = new List<WitSqlColumnInfo>(selectList.Count);
        for (int i = 0; i < selectList.Count; i++)
        {
            var item = selectList[i];
            var name = item.Alias ?? GetColumnName(item.Expression, i);
            var type = InferColumnType(item.Expression, sourceSchema);
            var isNullable = InferNullability(item.Expression, sourceSchema);
            var tableName = GetTableName(item.Expression, sourceSchema);
            schema.Add(new WitSqlColumnInfo 
            { 
                Name = name, 
                Type = type,
                IsNullable = isNullable,
                TableName = tableName
            });
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

    private static string? GetTableName(WitSqlExpression? expression, IReadOnlyList<WitSqlColumnInfo> sourceSchema)
    {
        if (expression is WitSqlExpressionColumnRef colRef)
        {
            // If table is specified in the expression, use it
            if (!string.IsNullOrEmpty(colRef.TableName))
                return colRef.TableName;
            
            // Otherwise, try to find it in source schema
            var sourceCol = FindSourceColumn(colRef.ColumnName, sourceSchema);
            return sourceCol?.TableName;
        }
        return null;
    }

    private static WitSqlType InferColumnType(WitSqlExpression? expression, IReadOnlyList<WitSqlColumnInfo> sourceSchema)
    {
        return expression switch
        {
            // Column reference - get type from source schema
            WitSqlExpressionColumnRef colRef => GetColumnTypeFromSchema(colRef, sourceSchema),
            
            // Literals
            WitSqlExpressionLiteral { Type: LiteralType.Integer } => WitSqlType.Integer,
            WitSqlExpressionLiteral { Type: LiteralType.Real } => WitSqlType.Real,
            WitSqlExpressionLiteral { Type: LiteralType.Decimal } => WitSqlType.Decimal,
            WitSqlExpressionLiteral { Type: LiteralType.Boolean } => WitSqlType.Boolean,
            WitSqlExpressionLiteral { Type: LiteralType.String } => WitSqlType.Text,
            WitSqlExpressionLiteral { Type: LiteralType.Blob } => WitSqlType.Blob,
            WitSqlExpressionLiteral { Type: LiteralType.Null } => WitSqlType.Null,
            WitSqlExpressionLiteral { Type: LiteralType.Date } => WitSqlType.DateOnly,
            WitSqlExpressionLiteral { Type: LiteralType.Time } => WitSqlType.TimeOnly,
            WitSqlExpressionLiteral { Type: LiteralType.Timestamp } => WitSqlType.DateTime,
            WitSqlExpressionLiteral { Type: LiteralType.TimestampOffset } => WitSqlType.DateTimeOffset,
            
            // Unary operations
            WitSqlExpressionUnary { Operator: UnaryOperatorType.Not } => WitSqlType.Boolean,
            WitSqlExpressionUnary { Operand: var op } => InferColumnType(op, sourceSchema),
            
            // Binary operations
            WitSqlExpressionBinary { Operator: var op } when IsComparisonOperator(op) => WitSqlType.Boolean,
            WitSqlExpressionBinary { Operator: var op, Left: var left, Right: var right } when IsArithmeticOperator(op) 
                => InferArithmeticResultType(InferColumnType(left, sourceSchema), InferColumnType(right, sourceSchema)),
            
            // Boolean predicates
            WitSqlExpressionBetween => WitSqlType.Boolean,
            WitSqlExpressionIn => WitSqlType.Boolean,
            WitSqlExpressionLike => WitSqlType.Boolean,
            WitSqlExpressionIsNull => WitSqlType.Boolean,
            
            // CASE expression - use type of first THEN clause
            WitSqlExpressionCase caseExpr when caseExpr.WhenClauses.Count > 0 
                => InferColumnType(caseExpr.WhenClauses[0].Then, sourceSchema),
            
            // IIF expression
            WitSqlExpressionIif iif => InferColumnType(iif.TrueValue, sourceSchema),
            
            // CAST expression - get target type
            WitSqlExpressionCast cast => WitTypeConverter.ParseSqlTypeName(cast.TargetType.TypeName),
            
            // Function calls - infer from function name
            WitSqlExpressionFunctionCall func => InferFunctionReturnType(func, sourceSchema),
            
            // Parameter - default to Text
            WitSqlExpressionParameter => WitSqlType.Text,
            
            // Default
            _ => WitSqlType.Text
        };
    }

    private static WitSqlType GetColumnTypeFromSchema(WitSqlExpressionColumnRef colRef, IReadOnlyList<WitSqlColumnInfo> sourceSchema)
    {
        var sourceCol = FindSourceColumn(colRef.ColumnName, sourceSchema, colRef.TableName);
        return sourceCol?.Type ?? WitSqlType.Text;
    }

    private static WitSqlColumnInfo? FindSourceColumn(string columnName, IReadOnlyList<WitSqlColumnInfo> sourceSchema, string? tableName = null)
    {
        // First try exact match with table name
        if (!string.IsNullOrEmpty(tableName))
        {
            foreach (var col in sourceSchema)
            {
                if (col.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase) &&
                    (col.TableName?.Equals(tableName, StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    return col;
                }
            }
        }

        // Try exact column name match
        foreach (var col in sourceSchema)
        {
            if (col.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase))
            {
                return col;
            }
        }

        // Try suffix match (e.g., "Name" matches "Users.Name")
        var suffix = "." + columnName;
        foreach (var col in sourceSchema)
        {
            if (col.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return col;
            }
        }

        return null;
    }

    private static bool InferNullability(WitSqlExpression? expression, IReadOnlyList<WitSqlColumnInfo> sourceSchema)
    {
        return expression switch
        {
            WitSqlExpressionColumnRef colRef => FindSourceColumn(colRef.ColumnName, sourceSchema, colRef.TableName)?.IsNullable ?? true,
            WitSqlExpressionLiteral { Type: LiteralType.Null } => true,
            _ => true // Conservative default
        };
    }

    private static WitSqlType InferArithmeticResultType(WitSqlType left, WitSqlType right)
    {
        // Decimal has highest precedence
        if (left == WitSqlType.Decimal || right == WitSqlType.Decimal)
            return WitSqlType.Decimal;
        
        // Real has higher precedence than Integer
        if (left == WitSqlType.Real || right == WitSqlType.Real)
            return WitSqlType.Real;
        
        // Integer + Integer = Integer
        if (left == WitSqlType.Integer && right == WitSqlType.Integer)
            return WitSqlType.Integer;
        
        // Default to Real for mixed types
        return WitSqlType.Real;
    }

    private static WitSqlType InferFunctionReturnType(WitSqlExpressionFunctionCall func, IReadOnlyList<WitSqlColumnInfo> sourceSchema)
    {
        var funcName = func.FunctionName.ToUpperInvariant();
        
        return funcName switch
        {
            // String functions
            "UPPER" or "LOWER" or "TRIM" or "LTRIM" or "RTRIM" or "SUBSTR" or "SUBSTRING" 
                or "REPLACE" or "CONCAT" or "LEFT" or "RIGHT" or "REVERSE" or "REPEAT"
                or "LPAD" or "RPAD" or "SPACE" or "CHAR" or "FORMAT" => WitSqlType.Text,
            
            // Numeric functions returning integer
            "LENGTH" or "LEN" or "CHARINDEX" or "INSTR" or "LOCATE" or "POSITION" => WitSqlType.Integer,
            
            // Numeric functions returning real
            "ABS" or "ROUND" or "FLOOR" or "CEILING" or "CEIL" or "TRUNC" or "TRUNCATE"
                or "SQRT" or "POWER" or "POW" or "EXP" or "LOG" or "LOG10" or "LN"
                or "SIN" or "COS" or "TAN" or "ASIN" or "ACOS" or "ATAN" or "ATAN2"
                or "SIGN" or "PI" or "RAND" or "RANDOM" => WitSqlType.Real,
            
            // Date/time functions
            "NOW" or "CURRENT_TIMESTAMP" or "GETDATE" or "DATETIME" => WitSqlType.DateTime,
            "CURRENT_DATE" or "DATE" or "TODAY" => WitSqlType.DateOnly,
            "CURRENT_TIME" or "TIME" => WitSqlType.TimeOnly,
            "YEAR" or "MONTH" or "DAY" or "HOUR" or "MINUTE" or "SECOND" 
                or "DAYOFWEEK" or "DAYOFYEAR" or "WEEK" or "QUARTER" => WitSqlType.Integer,
            "DATEADD" or "DATE_ADD" => WitSqlType.DateTime,
            "DATEDIFF" or "DATE_DIFF" => WitSqlType.Integer,
            
            // GUID functions
            "NEWGUID" or "NEWUUID" or "UUID" => WitSqlType.Guid,
            
            // Boolean functions
            "ISNULL" or "IFNULL" or "NVL" or "COALESCE" => func.Arguments?.Count > 0 
                ? InferColumnType(func.Arguments[0], sourceSchema) 
                : WitSqlType.Text,
            
            // Aggregate functions
            "COUNT" or "COUNT_BIG" => WitSqlType.Integer,
            "SUM" => func.Arguments?.Count > 0 
                ? InferColumnType(func.Arguments[0], sourceSchema) 
                : WitSqlType.Integer,
            "AVG" or "TOTAL" => WitSqlType.Real,
            "MIN" or "MAX" => func.Arguments?.Count > 0 
                ? InferColumnType(func.Arguments[0], sourceSchema) 
                : WitSqlType.Text,
            "GROUP_CONCAT" or "STRING_AGG" => WitSqlType.Text,
            
            // Default
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