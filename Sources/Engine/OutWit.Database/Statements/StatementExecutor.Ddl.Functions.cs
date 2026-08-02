using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Parser.Analysis;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Sql;

namespace OutWit.Database.Statements;

/// <summary>
/// <c>CREATE FUNCTION</c> and <c>DROP FUNCTION</c>.
/// </summary>
/// <remarks>
/// Everything a function body can be wrong about is decided here, at declaration, and nothing is
/// left to be discovered when a row is evaluated. That is phase 7's rule - accepted, enforced, or
/// refused - and it matters more for a function than for anything else this engine stores, because a
/// function reaches the row path: a body that fails per row fails inside a <c>CHECK</c>, inside a
/// computed column, and inside an index key, where there is no caller left holding the statement
/// that was wrong.
/// </remarks>
public sealed partial class StatementExecutor
{
    #region CREATE FUNCTION

    private WitSqlResult ExecuteCreateFunction(WitSqlStatementCreateFunction create)
    {
        if (m_context.Database.GetFunction(create.FunctionName) != null)
        {
            if (create.IfNotExists)
                return new WitSqlResult();

            throw new InvalidOperationException($"A function named '{create.FunctionName}' already exists.");
        }

        RefuseForeignLanguage(create.Language, create.FunctionName);

        var parameters = BuildRoutineParameters(create.Parameters, create.FunctionName);

        RefuseUnboundNames(create.Body, parameters, create.FunctionName);

        // Recursion before unknown calls, and the order is load-bearing: a function is not in the
        // catalog while it is being declared, so a self-call IS an unknown call. Checked the other
        // way round, RefuseRecursion was unreachable and the caller was told the engine does not
        // have a function they were in the middle of writing. Caught by its own test.
        RefuseRecursion(create.Body, create.FunctionName);
        RefuseUnknownCalls(create.Body, create.FunctionName);

        m_context.Database.CreateFunction(new DefinitionFunction
        {
            Name = create.FunctionName,
            ReturnType = MapDataType(create.ReturnType),
            Parameters = parameters,
            Body = create.Body,

            // Decided once, from the tree, and stored. It cannot change afterwards: the body is
            // immutable, and a function may not call one that could - a cycle is refused above and a
            // callee's own determinism is folded in below.
            IsDeterministic = IsBodyDeterministic(create.Body)
        });

        return new WitSqlResult();
    }

    #endregion

    #region DROP FUNCTION

    private WitSqlResult ExecuteDropFunction(WitSqlStatementDropFunction drop)
    {
        if (m_context.Database.GetFunction(drop.FunctionName) == null)
        {
            if (drop.IfExists)
                return new WitSqlResult();

            throw new InvalidOperationException($"Function '{drop.FunctionName}' not found.");
        }

        m_context.Database.DropFunction(drop.FunctionName);
        return new WitSqlResult();
    }

    #endregion

    #region Validation

    /// <summary>
    /// Refuses a body that names something which is neither a parameter nor a literal.
    /// </summary>
    /// <remarks>
    /// A function body has no row to resolve a column against - it is evaluated over its arguments
    /// and nothing else. Measured before this check existed: an unbound name reached the evaluator
    /// and threw <c>Column 'M' not found</c> at call time, which is the wrong moment. The caller who
    /// wrote the typo is long gone by then, and the failure surfaces inside whatever row was being
    /// written.
    /// </remarks>
    private static void RefuseUnboundNames(
        WitSqlExpression body,
        IReadOnlyList<DefinitionRoutineParameter>? parameters,
        string functionName)
    {
        var bound = new HashSet<string>(
            parameters?.Select(p => p.Name) ?? [],
            StringComparer.OrdinalIgnoreCase);

        foreach (var node in WitSqlNodes.SelfAndDescendants(body))
        {
            if (node is not WitSqlExpressionColumnRef column)
                continue;

            if (bound.Contains(column.ColumnName))
                continue;

            throw new NotSupportedException(
                $"Function '{functionName}' uses the name {column.ColumnName}, which is not one of "
                + "its parameters. A function body is evaluated over its arguments and has no row "
                + "to read a column from.");
        }
    }

    /// <summary>
    /// Refuses a body calling a function that neither the engine nor the catalog has.
    /// </summary>
    private void RefuseUnknownCalls(WitSqlExpression body, string functionName)
    {
        foreach (var node in WitSqlNodes.SelfAndDescendants(body))
        {
            if (node is not WitSqlExpressionFunctionCall call)
                continue;

            if (ExpressionFunctions.IsKnown(call.FunctionName))
                continue;

            if (m_context.Database.GetFunction(call.FunctionName) != null)
                continue;

            throw new NotSupportedException(
                $"Function '{functionName}' calls {call.FunctionName}(), which this engine does not "
                + "have and the database does not define.");
        }
    }

    /// <summary>
    /// Refuses a function that calls itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An expression-bodied function has no terminating construct - no condition it can stop on that
    /// does not itself re-enter - so recursion in one is always unbounded, and unbounded here means
    /// the process dies: a stack overflow cannot be caught in .NET. The nesting limit of § 2 does not
    /// help, because a function is evaluated inside an expression and never passes through
    /// <c>StatementExecutor.Execute</c>.
    /// </para>
    /// <para>
    /// A cycle through other functions cannot form: each of them was checked when it was declared,
    /// and a function that does not exist yet cannot be called, so the call graph is acyclic by
    /// construction. Only the direct case has to be refused here.
    /// </para>
    /// </remarks>
    private static void RefuseRecursion(WitSqlExpression body, string functionName)
    {
        if (!NamesFunction(body, functionName))
            return;

        throw new NotSupportedException(
            $"Function '{functionName}' calls itself. A function body is one expression and has "
            + "nothing to stop the recursion with, so it would run until the process ran out of "
            + "stack - which cannot be caught.");
    }

    private void RefuseForeignLanguage(string? language, string routineName)
    {
        if (language is null || string.Equals(language, "SQL", StringComparison.OrdinalIgnoreCase))
            return;

        throw new NotSupportedException(
            $"'{routineName}' declares LANGUAGE {language}. This engine runs SQL bodies only - it "
            + "loads no assemblies and executes no external code. Use LANGUAGE SQL, or omit the "
            + "clause.");
    }

    private List<DefinitionRoutineParameter>? BuildRoutineParameters(
        IReadOnlyList<WitSqlRoutineParameter>? parameters,
        string routineName)
    {
        if (parameters is null)
            return null;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in parameters)
        {
            if (!seen.Add(parameter.Name))
            {
                throw new NotSupportedException(
                    $"'{routineName}' declares the parameter {parameter.Name} twice.");
            }
        }

        return parameters
            .Select(parameter => new DefinitionRoutineParameter
            {
                Name = parameter.Name,
                Type = MapDataType(parameter.DataType),
                MaxLength = parameter.DataType.Length,
                Precision = parameter.DataType.Precision,
                Scale = parameter.DataType.Scale
            })
            .ToList();
    }

    /// <summary>
    /// Whether a body always gives the same answer for the same arguments.
    /// </summary>
    /// <remarks>
    /// The body's own tree decides it, and a call to another user-defined function folds in that
    /// function's stored answer - so a deterministic function calling a non-deterministic one is
    /// non-deterministic, which is the only way the property composes correctly.
    /// </remarks>
    private bool IsBodyDeterministic(WitSqlExpression body)
    {
        if (ExpressionDeterminism.ReasonItIsNotDeterministic(body) != null)
            return false;

        foreach (var node in WitSqlNodes.SelfAndDescendants(body))
        {
            if (node is WitSqlExpressionFunctionCall call
                && m_context.Database.GetFunction(call.FunctionName) is { IsDeterministic: false })
            {
                return false;
            }
        }

        return true;
    }

    private static bool NamesFunction(WitSqlExpression expression, string functionName)
    {
        return WitSqlNodes.SelfAndDescendants(expression)
            .OfType<WitSqlExpressionFunctionCall>()
            .Any(call => string.Equals(call.FunctionName, functionName, StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}
