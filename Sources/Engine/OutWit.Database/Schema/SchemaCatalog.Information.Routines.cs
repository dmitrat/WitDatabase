using OutWit.Database.Definitions;
using OutWit.Database.Sql;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Schema;

/// <summary>
/// INFORMATION_SCHEMA.ROUTINES and INFORMATION_SCHEMA.PARAMETERS.
/// </summary>
/// <remarks>
/// <para>
/// These are the two views the standard exposes for routines and the two a scaffolding tool reads.
/// Until now the planner refused them by name - <i>"Unknown INFORMATION_SCHEMA view: ROUTINES"</i> -
/// which was the right failure for something unbuilt: nothing was reading an empty result and
/// believing the database had no routines.
/// </para>
/// <para>
/// <c>ROUTINE_DEFINITION</c> is <b>rendered from the stored tree on demand</b> and is null when the
/// renderer cannot express the body faithfully. Never a placeholder comment: a comment reads as
/// rendered SQL to whatever consumes the column, and "something was emitted" is the mistake to avoid.
/// Nothing here asks the rendering a question either - <c>IS_DETERMINISTIC</c> comes from the
/// definition, which decided it from the tree at declaration.
/// </para>
/// </remarks>
public sealed partial class SchemaCatalog
{
    #region Constants

    private static readonly string[] ROUTINES_COLUMNS = [
        "SPECIFIC_CATALOG", "SPECIFIC_SCHEMA", "SPECIFIC_NAME",
        "ROUTINE_CATALOG", "ROUTINE_SCHEMA", "ROUTINE_NAME", "ROUTINE_TYPE",
        "DATA_TYPE", "ROUTINE_BODY", "ROUTINE_DEFINITION",
        "IS_DETERMINISTIC", "SQL_DATA_ACCESS", "PARAMETER_STYLE", "IS_USER_DEFINED_CAST"
    ];

    private static readonly WitSqlType[] ROUTINES_TYPES = [
        WitSqlType.Text, WitSqlType.Text, WitSqlType.Text,
        WitSqlType.Text, WitSqlType.Text, WitSqlType.Text, WitSqlType.Text,
        WitSqlType.Text, WitSqlType.Text, WitSqlType.Text,
        WitSqlType.Text, WitSqlType.Text, WitSqlType.Text, WitSqlType.Text
    ];

    private static readonly string[] PARAMETERS_COLUMNS = [
        "SPECIFIC_CATALOG", "SPECIFIC_SCHEMA", "SPECIFIC_NAME",
        "ORDINAL_POSITION", "PARAMETER_MODE", "IS_RESULT", "PARAMETER_NAME",
        "DATA_TYPE", "CHARACTER_MAXIMUM_LENGTH", "NUMERIC_PRECISION", "NUMERIC_SCALE"
    ];

    private static readonly WitSqlType[] PARAMETERS_TYPES = [
        WitSqlType.Text, WitSqlType.Text, WitSqlType.Text,
        WitSqlType.Integer, WitSqlType.Text, WitSqlType.Text, WitSqlType.Text,
        WitSqlType.Text, WitSqlType.Integer, WitSqlType.Integer, WitSqlType.Integer
    ];

    #endregion

    #region INFORMATION_SCHEMA.ROUTINES

    /// <summary>
    /// Every function and procedure, as <c>INFORMATION_SCHEMA.ROUTINES</c> rows.
    /// </summary>
    public IEnumerable<WitSqlRow> GetInformationSchemaRoutines()
    {
        m_lock.EnterReadLock();
        try
        {
            var results = new List<WitSqlRow>();

            foreach (var function in m_functions.Values)
            {
                results.Add(RoutineRow(
                    function.Name,
                    "FUNCTION",
                    GetDataTypeName(function.ReturnType),
                    function.DisplayBody(),
                    function.IsDeterministic));
            }

            foreach (var procedure in m_procedures.Values)
            {
                // DATA_TYPE is null for a procedure: the standard uses it for the return type, and a
                // procedure has none. Reporting an empty string instead would be a value, and a
                // consumer cannot tell an empty type from no type.
                results.Add(RoutineRow(
                    procedure.Name,
                    "PROCEDURE",
                    returnType: null,
                    procedure.DisplayBody(),
                    // A procedure's body is statements, so the question does not apply to it. NO is
                    // the honest answer of the two available, and the one both targets give.
                    isDeterministic: false));
            }

            return results;
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>Column names of <c>INFORMATION_SCHEMA.ROUTINES</c>.</summary>
    public static IReadOnlyList<string> GetInformationSchemaRoutinesColumns() => ROUTINES_COLUMNS;

    /// <summary>Column types of <c>INFORMATION_SCHEMA.ROUTINES</c>.</summary>
    public static IReadOnlyList<WitSqlType> GetInformationSchemaRoutinesColumnTypes() => ROUTINES_TYPES;

    #endregion

    #region INFORMATION_SCHEMA.PARAMETERS

    /// <summary>
    /// Every routine parameter, as <c>INFORMATION_SCHEMA.PARAMETERS</c> rows.
    /// </summary>
    public IEnumerable<WitSqlRow> GetInformationSchemaParameters()
    {
        m_lock.EnterReadLock();
        try
        {
            var results = new List<WitSqlRow>();

            foreach (var function in m_functions.Values)
                AddParameters(results, function.Name, function.Parameters);

            foreach (var procedure in m_procedures.Values)
                AddParameters(results, procedure.Name, procedure.Parameters);

            return results;
        }
        finally
        {
            m_lock.ExitReadLock();
        }
    }

    /// <summary>Column names of <c>INFORMATION_SCHEMA.PARAMETERS</c>.</summary>
    public static IReadOnlyList<string> GetInformationSchemaParametersColumns() => PARAMETERS_COLUMNS;

    /// <summary>Column types of <c>INFORMATION_SCHEMA.PARAMETERS</c>.</summary>
    public static IReadOnlyList<WitSqlType> GetInformationSchemaParametersColumnTypes() => PARAMETERS_TYPES;

    #endregion

    #region Helpers

    private static WitSqlRow RoutineRow(
        string name,
        string routineType,
        string? returnType,
        string? definition,
        bool isDeterministic)
    {
        return new WitSqlRow([
            WitSqlValue.FromText("WitDB"),                                          // SPECIFIC_CATALOG
            WitSqlValue.FromText("public"),                                         // SPECIFIC_SCHEMA
            WitSqlValue.FromText(name),                                             // SPECIFIC_NAME
            WitSqlValue.FromText("WitDB"),                                          // ROUTINE_CATALOG
            WitSqlValue.FromText("public"),                                         // ROUTINE_SCHEMA
            WitSqlValue.FromText(name),                                             // ROUTINE_NAME
            WitSqlValue.FromText(routineType),                                      // ROUTINE_TYPE
            returnType is null ? WitSqlValue.Null : WitSqlValue.FromText(returnType), // DATA_TYPE
            WitSqlValue.FromText("SQL"),                                            // ROUTINE_BODY
            definition is null ? WitSqlValue.Null : WitSqlValue.FromText(definition), // ROUTINE_DEFINITION
            WitSqlValue.FromText(isDeterministic ? "YES" : "NO"),                   // IS_DETERMINISTIC
            WitSqlValue.FromText("MODIFIES"),                                       // SQL_DATA_ACCESS
            WitSqlValue.FromText("SQL"),                                            // PARAMETER_STYLE
            WitSqlValue.FromText("NO")                                              // IS_USER_DEFINED_CAST
        ], ROUTINES_COLUMNS);
    }

    private static void AddParameters(
        List<WitSqlRow> results,
        string routineName,
        IReadOnlyList<DefinitionRoutineParameter>? parameters)
    {
        if (parameters is null)
            return;

        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];

            results.Add(new WitSqlRow([
                WitSqlValue.FromText("WitDB"),                                      // SPECIFIC_CATALOG
                WitSqlValue.FromText("public"),                                     // SPECIFIC_SCHEMA
                WitSqlValue.FromText(routineName),                                  // SPECIFIC_NAME
                WitSqlValue.FromInt(i + 1),                                         // ORDINAL_POSITION
                // IN and nothing else: there is no protocol here for handing a value back, so any
                // other value in this column would be a claim the engine cannot honour.
                WitSqlValue.FromText("IN"),                                         // PARAMETER_MODE
                WitSqlValue.FromText("NO"),                                         // IS_RESULT
                WitSqlValue.FromText(parameter.Name),                               // PARAMETER_NAME
                WitSqlValue.FromText(GetDataTypeName(parameter.Type)), // DATA_TYPE
                parameter.MaxLength is { } length
                    ? WitSqlValue.FromInt(length)
                    : WitSqlValue.Null,                                             // CHARACTER_MAXIMUM_LENGTH
                parameter.Precision is { } precision
                    ? WitSqlValue.FromInt(precision)
                    : WitSqlValue.Null,                                             // NUMERIC_PRECISION
                parameter.Scale is { } scale
                    ? WitSqlValue.FromInt(scale)
                    : WitSqlValue.Null                                              // NUMERIC_SCALE
            ], PARAMETERS_COLUMNS));
        }
    }

    #endregion
}
