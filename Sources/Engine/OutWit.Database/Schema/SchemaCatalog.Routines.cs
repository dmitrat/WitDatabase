using OutWit.Common.MemoryPack;
using OutWit.Database.Definitions;

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
    public bool DropFunction(string name)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_functions.Remove(name))
                return false;

            SaveFunctions();
            return true;
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
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
    public bool DropProcedure(string name)
    {
        m_lock.EnterWriteLock();
        try
        {
            if (!m_procedures.Remove(name))
                return false;

            SaveProcedures();
            return true;
        }
        finally
        {
            m_lock.ExitWriteLock();
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
