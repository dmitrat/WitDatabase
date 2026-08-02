using OutWit.Database.Definitions;

namespace OutWit.Database.Engine;

/// <summary>
/// Catalog access for functions and procedures.
/// </summary>
/// <remarks>
/// Passthrough to <c>SchemaCatalog</c>, in the shape the trigger, view and sequence members already
/// have. It exists so the executor and the tests reach routines the same way they reach every other
/// schema object, rather than through <c>engine.Catalog</c> - one route to a fact, not two.
/// </remarks>
public sealed partial class WitSqlEngine
{
    #region Functions

    /// <summary>
    /// Gets a function by name, or null.
    /// </summary>
    public DefinitionFunction? GetFunction(string name) => m_schema.GetFunction(name);

    /// <summary>
    /// Gets every function.
    /// </summary>
    public IEnumerable<DefinitionFunction> GetFunctions() => m_schema.GetFunctions();

    /// <summary>
    /// Creates a function.
    /// </summary>
    public void CreateFunction(DefinitionFunction function) => m_schema.CreateFunction(function);

    /// <summary>
    /// Drops a function. Returns false when there was none of that name.
    /// </summary>
    public bool DropFunction(string name) => m_schema.DropFunction(name);

    #endregion

    #region Procedures

    /// <summary>
    /// Gets a procedure by name, or null.
    /// </summary>
    public DefinitionProcedure? GetProcedure(string name) => m_schema.GetProcedure(name);

    /// <summary>
    /// Gets every procedure.
    /// </summary>
    public IEnumerable<DefinitionProcedure> GetProcedures() => m_schema.GetProcedures();

    /// <summary>
    /// Creates a procedure.
    /// </summary>
    public void CreateProcedure(DefinitionProcedure procedure) => m_schema.CreateProcedure(procedure);

    /// <summary>
    /// Drops a procedure. Returns false when there was none of that name.
    /// </summary>
    public bool DropProcedure(string name) => m_schema.DropProcedure(name);

    #endregion
}
