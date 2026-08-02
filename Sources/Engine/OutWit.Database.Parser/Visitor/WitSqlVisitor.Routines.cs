using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Generated;
using OutWit.Database.Parser.Schema;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Visitor;

/// <summary>
/// Builds routine statements - functions, procedures and CALL - from the parse tree.
/// </summary>
internal sealed partial class WitSqlVisitor
{
    #region CREATE/DROP FUNCTION

    public override WitSqlStatementCreateFunction VisitCreateFunctionStatement(
        WitSqlParser.CreateFunctionStatementContext context)
    {
        return new WitSqlStatementCreateFunction
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            FunctionName = NormalizeIdentifier(context.routineName().GetText()),
            IfNotExists = context.EXISTS() != null,
            Parameters = BuildParameters(context.routineParameters()),
            ReturnType = VisitDataType(context.dataType()),

            // The body is the expression after RETURN, and nothing else. See
            // WitSqlStatementCreateFunction for why RETURN has no statement type of its own.
            Body = VisitExpression(context.expression()),
            Language = context.routineLanguage() is { } language
                ? NormalizeIdentifier(language.GetText())
                : null
        };
    }

    public override WitSqlStatementDropFunction VisitDropFunctionStatement(
        WitSqlParser.DropFunctionStatementContext context)
    {
        return new WitSqlStatementDropFunction
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            FunctionName = NormalizeIdentifier(context.routineName().GetText()),
            IfExists = context.EXISTS() != null
        };
    }

    #endregion

    #region CREATE/DROP PROCEDURE

    public override WitSqlStatementCreateProcedure VisitCreateProcedureStatement(
        WitSqlParser.CreateProcedureStatementContext context)
    {
        var body = new List<WitSqlStatement>();

        foreach (var statement in context.statement())
        {
            if (VisitStatement(statement) is { } built)
                body.Add(built);
        }

        return new WitSqlStatementCreateProcedure
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            ProcedureName = NormalizeIdentifier(context.routineName().GetText()),
            IfNotExists = context.EXISTS() != null,
            Parameters = BuildParameters(context.routineParameters()),
            Body = body,
            Language = context.routineLanguage() is { } language
                ? NormalizeIdentifier(language.GetText())
                : null
        };
    }

    public override WitSqlStatementDropProcedure VisitDropProcedureStatement(
        WitSqlParser.DropProcedureStatementContext context)
    {
        return new WitSqlStatementDropProcedure
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            ProcedureName = NormalizeIdentifier(context.routineName().GetText()),
            IfExists = context.EXISTS() != null
        };
    }

    #endregion

    #region CALL

    public override WitSqlStatementCall VisitCallStatement(WitSqlParser.CallStatementContext context)
    {
        var arguments = context.expression()
            .Select(VisitExpression)
            .ToList();

        return new WitSqlStatementCall
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            ProcedureName = NormalizeIdentifier(context.routineName().GetText()),
            Arguments = arguments.Count > 0 ? arguments : null
        };
    }

    #endregion

    #region Helpers

    /// <summary>
    /// The declared parameters, or null when the list is absent or empty.
    /// </summary>
    /// <remarks>
    /// Null rather than an empty list, so that "declared with no parameters" and "declared without a
    /// parameter list at all" - which is SQL Server's procedure spelling - are the same thing to
    /// everything downstream. They mean the same thing, and two representations of one fact is how
    /// the catalog ends up disagreeing with itself.
    /// </remarks>
    private List<WitSqlRoutineParameter>? BuildParameters(WitSqlParser.RoutineParametersContext? context)
    {
        if (context == null)
            return null;

        var parameters = context.routineParameter()
            .Select(parameter => new WitSqlRoutineParameter
            {
                Name = NormalizeIdentifier(parameter.routineParameterName().GetText()),
                DataType = VisitDataType(parameter.dataType())
            })
            .ToList();

        return parameters.Count > 0 ? parameters : null;
    }

    #endregion
}
