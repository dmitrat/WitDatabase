using OutWit.Database.Definitions;
using OutWit.Database.Engine;
using OutWit.Database.Expressions;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Sql;
using OutWit.Database.Values;

namespace OutWit.Database.Statements;

/// <summary>
/// <c>CREATE PROCEDURE</c>, <c>DROP PROCEDURE</c> and <c>CALL</c>.
/// </summary>
public sealed partial class StatementExecutor
{
    #region CREATE PROCEDURE

    private WitSqlResult ExecuteCreateProcedure(WitSqlStatementCreateProcedure create)
    {
        if (m_context.Database.GetProcedure(create.ProcedureName) != null)
        {
            if (create.IfNotExists)
                return new WitSqlResult();

            throw new InvalidOperationException($"A procedure named '{create.ProcedureName}' already exists.");
        }

        RefuseForeignLanguage(create.Language, create.ProcedureName);

        var parameters = BuildRoutineParameters(create.Parameters, create.ProcedureName);

        foreach (var statement in create.Body)
            RefuseStatementNotAllowedInAProcedure(statement, create.ProcedureName);

        m_context.Database.CreateProcedure(new DefinitionProcedure
        {
            Name = create.ProcedureName,
            Parameters = parameters,
            Statements = create.Body
        });

        return new WitSqlResult();
    }

    /// <summary>
    /// Refuses a body statement a procedure may not contain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The allowed set is DML, DDL and <c>CALL</c>, decided against measurement in
    /// <c>Docs/PHASE9D-ROUTINE-SUBSYSTEM-DESIGN.md</c> § 3. DDL is allowed here and refused in a
    /// trigger body, and the asymmetry is not arbitrary: a trigger runs inside a loop over rows, and
    /// <c>DROP TABLE</c> against the table that loop is walking reports success and destroys it. A
    /// <c>CALL</c> at the top level is a statement, not a row loop - which is exactly why a trigger
    /// body may not contain one.
    /// </para>
    /// <para>
    /// <b>Transaction control is refused for a stronger reason than DDL ever had.</b> It is stopped
    /// by nothing at runtime: a nested <c>COMMIT</c> commits the calling statement's transaction, so
    /// the rest of that statement runs outside one. Measured as a three-row <c>INSERT</c> leaving two
    /// rows behind after its third failed, raising only the key violation. DDL fails loudly; this
    /// does not fail.
    /// </para>
    /// <para>
    /// Declaring a routine inside a routine is refused as self-modification during execution -
    /// a body that rewrites the catalog it is being run from.
    /// </para>
    /// </remarks>
    private static void RefuseStatementNotAllowedInAProcedure(WitSqlStatement statement, string procedureName)
    {
        switch (statement)
        {
            case WitSqlStatementBeginTransaction:
            case WitSqlStatementCommit:
            case WitSqlStatementRollback:
            case WitSqlStatementSavepoint:
            case WitSqlStatementReleaseSavepoint:
            case WitSqlStatementSetTransaction:
                throw new NotSupportedException(
                    $"Procedure '{procedureName}' contains {Describe(statement)}. A routine body may "
                    + "not control transactions: committing inside one commits the statement that "
                    + "called it, and the rest of that statement then runs outside any transaction - "
                    + "silently, with no error raised anywhere.");

            case WitSqlStatementCreateFunction:
            case WitSqlStatementDropFunction:
            case WitSqlStatementCreateProcedure:
            case WitSqlStatementDropProcedure:
                throw new NotSupportedException(
                    $"Procedure '{procedureName}' declares or drops a routine. A body may not change "
                    + "the catalog it is being run from.");
        }
    }

    #endregion

    #region DROP PROCEDURE

    private WitSqlResult ExecuteDropProcedure(WitSqlStatementDropProcedure drop)
    {
        if (m_context.Database.GetProcedure(drop.ProcedureName) == null)
        {
            if (drop.IfExists)
                return new WitSqlResult();

            throw new InvalidOperationException($"Procedure '{drop.ProcedureName}' not found.");
        }

        m_context.Database.DropProcedure(drop.ProcedureName);
        return new WitSqlResult();
    }

    #endregion

    #region CALL

    /// <summary>
    /// Runs a procedure body and returns the result of its last statement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The last statement's result is the call's result</b>, so a body ending in a <c>SELECT</c>
    /// hands rows back to the caller. Without that, <c>CommandType.StoredProcedure</c> on the ADO
    /// surface would have nothing to read and the subsystem would exist without being reachable the
    /// way consumers actually reach one.
    /// </para>
    /// <para>
    /// One result set, not several: <c>WitDbDataReader.NextResult</c> is hard-coded false, so a body
    /// with two <c>SELECT</c>s hands back the second and the first is discarded. That is stated
    /// rather than hidden, and it is why the earlier results are disposed here instead of leaked.
    /// </para>
    /// <para>
    /// <b>Nesting is counted, and that is what makes recursion safe here where it is refused for a
    /// function.</b> Every body statement goes through <c>Execute</c>, which raises the depth and
    /// refuses past 32 with a catchable error. A function has no such door - it is evaluated inside
    /// an expression - which is why a self-calling function is refused at declaration and a
    /// self-calling procedure is not.
    /// </para>
    /// </remarks>
    private WitSqlResult ExecuteCall(WitSqlStatementCall call)
    {
        var procedure = m_context.Database.GetProcedure(call.ProcedureName)
            ?? throw new InvalidOperationException($"Procedure '{call.ProcedureName}' not found.");

        var parameters = procedure.Parameters ?? [];
        var arguments = call.Arguments ?? [];

        if (parameters.Count != arguments.Count)
        {
            throw new InvalidOperationException(
                $"Procedure {procedure.Name} takes {parameters.Count} argument(s) but was given "
                + $"{arguments.Count}.");
        }

        // Evaluated in the CALLER's scope, before anything is bound - so an argument naming a
        // parameter of an enclosing routine means that one, not the one about to be created.
        var evaluator = new ExpressionEvaluator(m_context);
        var values = new WitSqlValue[arguments.Count];

        for (var i = 0; i < arguments.Count; i++)
            values[i] = evaluator.Evaluate(arguments[i], new WitSqlRow([], []));

        var saved = BindArguments(parameters, values);

        try
        {
            WitSqlResult? result = null;

            foreach (var statement in procedure.Statements)
            {
                result?.Dispose();
                result = Execute(statement);
            }

            return result ?? new WitSqlResult();
        }
        finally
        {
            RestoreArguments(saved);
        }
    }

    /// <summary>
    /// Binds the arguments as named parameters, and returns what to put back afterwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The evaluator already resolves a bare name it cannot find anywhere else against
    /// <c>Parameters["@name"]</c>, so binding there is all a body statement needs to see its
    /// arguments - no new resolution path, and nothing new to keep in step with the existing one.
    /// </para>
    /// <para>
    /// <b>A column of the row being processed wins over a parameter of the same name.</b> That is the
    /// existing precedence and it is the safe direction: a parameter cannot shadow a column and
    /// silently change what a body statement means. It does mean a parameter named after a column is
    /// invisible inside statements that read that column, which is inherent to parameters being
    /// plain identifiers rather than <c>@names</c>, and is why the previous value is saved and
    /// restored rather than the dictionary being replaced.
    /// </para>
    /// </remarks>
    private List<(string Key, WitSqlValue? Previous)> BindArguments(
        IReadOnlyList<DefinitionRoutineParameter> parameters,
        WitSqlValue[] values)
    {
        var saved = new List<(string, WitSqlValue?)>(parameters.Count);

        for (var i = 0; i < parameters.Count; i++)
        {
            var key = WitSqlParameterKeys.ToContextKey(parameters[i].Name);

            // The cast is load-bearing. WitSqlValue is a struct with an implicit conversion from
            // string, so without it the ternary picks that conversion, reads the null as a string,
            // and "there was no previous value" becomes a text value that gets restored over the
            // caller's parameter. Found by this file's own test, as an ArgumentNullException from
            // WitSqlValue.FromText.
            saved.Add((key, m_context.Parameters.TryGetValue(key, out var previous)
                ? (WitSqlValue?)previous
                : null));
            m_context.Parameters[key] = values[i];
        }

        return saved;
    }

    private void RestoreArguments(List<(string Key, WitSqlValue? Previous)> saved)
    {
        foreach (var (key, previous) in saved)
        {
            if (previous is { } value)
                m_context.Parameters[key] = value;
            else
                m_context.Parameters.Remove(key);
        }
    }

    #endregion
}
