using OutWit.Common.MemoryPack;
using OutWit.Database.Definitions;
using OutWit.Database.Parser.Analysis;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Schema;

/// <summary>
/// Functions and procedures - the routine part of SchemaCatalog.
/// </summary>
/// <remarks>
/// <para>
/// Two dictionaries and two store records, not one of each. A mixed routine list would need a
/// MemoryPack union tag of its own and would force every reader to discriminate, and the two have
/// different bodies - a function holds an expression, a procedure a statement list - so nothing ever
/// wants them together. <c>INFORMATION_SCHEMA.ROUTINES</c> is the one place that reads both, and it
/// reads them as two loops.
/// </para>
/// <para>
/// Both records are <b>new keys</b>. A database written before routines existed simply has neither,
/// and <see cref="LoadRoutines"/> finds nothing and leaves both dictionaries empty - which is why
/// this addition cannot break a file written by an earlier version. Pinned by
/// <c>CatalogBackwardCompatibilityTests</c>.
/// </para>
/// </remarks>
public sealed partial class SchemaCatalog
{
    #region Functions

    /// <summary>
    /// Gets a function by name, or null.
    /// </summary>
    public DefinitionFunction? GetFunction(string name)
    {
        m_lock.EnterReadLock();
        try
        {
            return m_functions.GetValueOrDefault(name);
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets every function.
    /// </summary>
    public IEnumerable<DefinitionFunction> GetFunctions()
    {
        m_lock.EnterReadLock();
        try
        {
            return m_functions.Values.ToList();
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Creates a function.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A routine of that name already exists. Functions and procedures share one namespace - see
    /// <see cref="RefuseNameInUse"/>.
    /// </exception>
    public void CreateFunction(DefinitionFunction function)
    {
        m_lock.EnterWriteLock();
        try
        {
            RefuseNameInUse(function.Name);

            m_functions[function.Name] = function;
            SaveFunctions();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Drops a function. Returns false when there was none of that name.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A stored expression still names it - see <see cref="RefuseDropWhileDependedOn"/>.
    /// </exception>
    public bool DropFunction(string name)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_functions.ContainsKey(name))
                return false;

            RefuseDropWhileDependedOn(name);

            m_functions.Remove(name);
            SaveFunctions();
            return true;
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Refuses to drop a function that a stored expression still names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>RESTRICT</c> semantics, with no <c>CASCADE</c> offered. The reason is already on the books
    /// in its worst form: <c>RENAME COLUMN</c> and <c>DROP COLUMN</c> leave expressions naming
    /// something that no longer exists, and after that <b>the table cannot be written to at all</b>.
    /// A dangling function reference is the same class, and refusing the drop is far cheaper than
    /// discovering the table is dead.
    /// </para>
    /// <para>
    /// <b>It walks the definitions rather than a dependency list.</b> A list is a second copy of a
    /// fact, and phase 8 spent an entire audit on what happens when two copies disagree - the one
    /// thing that actually broke came from storing a fact twice. Walking is slower and cannot go
    /// stale, and this is a <c>DROP</c>, which happens once.
    /// </para>
    /// <para>
    /// Here rather than in the executor because this is where the tables and indexes are. An
    /// invariant enforced beside the data it protects cannot be bypassed by a second caller who did
    /// not know to ask.
    /// </para>
    /// </remarks>
    private void RefuseDropWhileDependedOn(string functionName)
    {
        foreach (var table in m_tables.Values)
        {
            foreach (var (expression, where) in StoredExpressionsOf(table))
            {
                if (!NamesFunction(expression, functionName))
                    continue;

                throw new InvalidOperationException(
                    $"Function '{functionName}' cannot be dropped because {where} still uses it. "
                    + "Drop or alter that first - a schema expression left naming a function that "
                    + "does not exist makes the object it belongs to unusable.");
            }
        }

        foreach (var index in m_indexes.Values)
        {
            if (index.Expressions is null)
                continue;

            foreach (var expression in index.Expressions)
            {
                if (expression is not null && NamesFunction(expression, functionName))
                {
                    throw new InvalidOperationException(
                        $"Function '{functionName}' cannot be dropped because index '{index.Name}' "
                        + "is built on an expression that uses it.");
                }
            }
        }

        foreach (var function in m_functions.Values)
        {
            if (!string.Equals(function.Name, functionName, StringComparison.OrdinalIgnoreCase)
                && NamesFunction(function.Body, functionName))
            {
                throw new InvalidOperationException(
                    $"Function '{functionName}' cannot be dropped because function "
                    + $"'{function.Name}' calls it.");
            }
        }
    }

    private static IEnumerable<(WitSqlExpression Expression, string Where)> StoredExpressionsOf(
        DefinitionTable table)
    {
        foreach (var column in table.Columns)
        {
            if (column.Check is { } check)
                yield return (check, $"the CHECK on {table.Name}.{column.Name}");

            if (column.Computed is { } computed)
                yield return (computed, $"the computed column {table.Name}.{column.Name}");

            if (column.Default is { } @default)
                yield return (@default, $"the DEFAULT on {table.Name}.{column.Name}");
        }

        if (table.Checks is null)
            yield break;

        foreach (var check in table.Checks)
        {
            if (check is not null)
                yield return (check, $"a CHECK on {table.Name}");
        }
    }

    private static bool NamesFunction(WitSqlExpression expression, string functionName)
    {
        return WitSqlNodes.SelfAndDescendants(expression)
            .OfType<WitSqlExpressionFunctionCall>()
            .Any(call => string.Equals(call.FunctionName, functionName, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Procedures

    /// <summary>
    /// Gets a procedure by name, or null.
    /// </summary>
    public DefinitionProcedure? GetProcedure(string name)
    {
        m_lock.EnterReadLock();
        try
        {
            return m_procedures.GetValueOrDefault(name);
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets every procedure.
    /// </summary>
    public IEnumerable<DefinitionProcedure> GetProcedures()
    {
        m_lock.EnterReadLock();
        try
        {
            return m_procedures.Values.ToList();
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Creates a procedure.
    /// </summary>
    public void CreateProcedure(DefinitionProcedure procedure)
    {
        m_lock.EnterWriteLock();
        try
        {
            RefuseNameInUse(procedure.Name);

            m_procedures[procedure.Name] = procedure;
            SaveProcedures();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Drops a procedure. Returns false when there was none of that name.
    /// </summary>
    /// <exception cref="InvalidOperationException">Another procedure still calls it.</exception>
    public bool DropProcedure(string name)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_procedures.ContainsKey(name))
                return false;

            RefuseDropWhileCalled(name);

            m_procedures.Remove(name);
            SaveProcedures();
            return true;
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Refuses to drop a procedure another procedure's body calls.
    /// </summary>
    /// <remarks>
    /// The same <c>RESTRICT</c> rule as for functions and for the same reason: a body left calling
    /// something that does not exist is a routine that fails when it is run rather than when it was
    /// broken. Unlike a function, a procedure cannot be reached from a table's stored expressions -
    /// only another procedure can name one - so this search is over the procedures alone.
    /// </remarks>
    private void RefuseDropWhileCalled(string procedureName)
    {
        foreach (var procedure in m_procedures.Values)
        {
            if (string.Equals(procedure.Name, procedureName, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var statement in procedure.Statements)
            {
                if (WitSqlNodes.SelfAndDescendants(statement)
                    .OfType<WitSqlStatementCall>()
                    .Any(call => string.Equals(call.ProcedureName, procedureName, StringComparison.OrdinalIgnoreCase))
                    || (statement is WitSqlStatementCall direct
                        && string.Equals(direct.ProcedureName, procedureName, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException(
                        $"Procedure '{procedureName}' cannot be dropped because procedure "
                        + $"'{procedure.Name}' calls it.");
                }
            }
        }
    }

    #endregion

    #region Persistence

    private void SaveFunctions()
    {
        var functions = m_functions.Values.ToList();
        PutSchemaRecord(FUNCTIONS_KEY_BYTES.AsSpan(), functions.ToMemoryPackBytes());
    }

    private void SaveProcedures()
    {
        var procedures = m_procedures.Values.ToList();
        PutSchemaRecord(PROCEDURES_KEY_BYTES.AsSpan(), procedures.ToMemoryPackBytes());
    }

    private void LoadRoutines()
    {
        var functionsData = GetSchemaRecord(FUNCTIONS_KEY_BYTES.AsSpan());
        if (functionsData is { Length: > 0 })
        {
            foreach (var function in ReadSchemaRecord<List<DefinitionFunction>>(functionsData, "functions"))
                m_functions[function.Name] = function;
        }

        var proceduresData = GetSchemaRecord(PROCEDURES_KEY_BYTES.AsSpan());
        if (proceduresData is { Length: > 0 })
        {
            foreach (var procedure in ReadSchemaRecord<List<DefinitionProcedure>>(proceduresData, "procedures"))
                m_procedures[procedure.Name] = procedure;
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Refuses a routine name that is already taken by a routine of either kind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Functions and procedures share one namespace</b>, which is what PostgreSQL and SQL Server
    /// both do, and it is the answer that keeps the resolver honest: a <c>CALL X()</c> and an
    /// <c>X()</c> in an expression must not be able to reach two different objects. Separate
    /// namespaces would make the meaning of a name depend on the position it is written in, and this
    /// project has already paid for one identifier whose meaning depended on context.
    /// </para>
    /// <para>
    /// Tables and views are <i>not</i> checked against: a routine is not a table source, so
    /// <c>FUNCTION Orders</c> beside <c>TABLE Orders</c> is unambiguous everywhere either can appear.
    /// Refusing it would be a restriction with no failure behind it.
    /// </para>
    /// </remarks>
    private void RefuseNameInUse(string name)
    {
        if (m_functions.ContainsKey(name))
            throw new InvalidOperationException($"A function named '{name}' already exists.");

        if (m_procedures.ContainsKey(name))
            throw new InvalidOperationException($"A procedure named '{name}' already exists.");
    }

    #endregion
}
