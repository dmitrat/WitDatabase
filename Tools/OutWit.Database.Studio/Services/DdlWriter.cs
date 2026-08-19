using System.Text;
using System.Text.RegularExpressions;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// Writes the DDL the designer is about to run (WS-38).
///
/// Every shape here has been executed against the engine - <c>DdlWriterTests</c> runs each one on a
/// real database rather than comparing it with an expected string, because a generator that produces
/// SQL nobody has executed is a generator of plausible text. That is also how the quoting rule was
/// settled and how three shapes the design assumed were found not to parse.
///
/// The writer never binds parameters: DDL takes none, and the whole point of this panel is that the
/// text on screen is the text that runs.
/// </summary>
public static partial class DdlWriter
{
    #region Constants

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainIdentifier { get; }

    #endregion

    #region Identifiers

    /// <summary>
    /// An identifier, quoted only when it has to be.
    ///
    /// Quoting everything would be safer to write and worse to read, and the DDL panel exists to be
    /// read. A reserved word IS quoted: measured in stage 3, an unquoted <c>Blob BLOB</c> is refused,
    /// and in stage 6, <c>[Text]</c> and <c>"x"</c> are accepted - so the quotes are what make a
    /// column named after a type work at all.
    /// </summary>
    public static string Identifier(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        if (!PlainIdentifier.IsMatch(name) || IsReserved(name))
            return "\"" + name.Replace("\"", "\"\"") + "\"";

        return name;
    }

    private static bool IsReserved(string name) =>
        SqlVocabulary.Keywords.Contains(name, StringComparer.OrdinalIgnoreCase) ||
        SqlVocabulary.DataTypes.Contains(name, StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Columns

    /// <summary>
    /// One column, as it is written inside CREATE TABLE.
    /// </summary>
    public static string ColumnDefinition(ColumnDraft column, bool inlinePrimaryKey)
    {
        if (column.IsComputed)
            return $"{Identifier(column.Name)} AS ({column.ComputedExpression})";

        var sb = new StringBuilder();
        sb.Append(Identifier(column.Name));
        sb.Append(' ');
        sb.Append(column.TypeText);

        // The engine writes a single-column autoincrement key inline and nowhere else - and an
        // AUTOINCREMENT key is the one case where an insert does not have to scan for uniqueness
        // (WS-44), so the designer keeps the two words together.
        if (inlinePrimaryKey && column.IsPrimaryKey)
        {
            sb.Append(" PRIMARY KEY");

            if (column.IsAutoIncrement)
                sb.Append(" AUTOINCREMENT");
        }
        else if (!column.IsNullable)
        {
            sb.Append(" NOT NULL");
        }

        if (column.IsUnique && !column.IsPrimaryKey)
            sb.Append(" UNIQUE");

        if (!string.IsNullOrWhiteSpace(column.DefaultValue))
            sb.Append($" DEFAULT {column.DefaultValue}");

        if (!string.IsNullOrWhiteSpace(column.CheckExpression))
            sb.Append($" CHECK ({column.CheckExpression})");

        if (!string.IsNullOrWhiteSpace(column.ReferencesTable) && !string.IsNullOrWhiteSpace(column.ReferencesColumn))
            sb.Append($" REFERENCES {Identifier(column.ReferencesTable)}({Identifier(column.ReferencesColumn)})");

        return sb.ToString();
    }

    #endregion

    #region Tables

    public static string CreateTable(string table, IReadOnlyList<ColumnDraft> columns)
    {
        var keys = columns.Where(c => c.IsPrimaryKey && !c.IsDeleted).ToList();
        var inlineKey = keys.Count == 1;

        var lines = columns
            .Where(c => !c.IsDeleted)
            .Select(c => "    " + ColumnDefinition(c, inlineKey))
            .ToList();

        if (keys.Count > 1)
            lines.Add($"    PRIMARY KEY ({string.Join(", ", keys.Select(k => Identifier(k.Name)))})");

        return $"CREATE TABLE {Identifier(table)} (\n{string.Join(",\n", lines)}\n);";
    }

    public static string DropTable(string table) => $"DROP TABLE {Identifier(table)};";

    public static string RenameTable(string table, string newName) =>
        $"ALTER TABLE {Identifier(table)} RENAME TO {Identifier(newName)};";

    /// <summary>
    /// Empties a table. TRUNCATE is in this grammar and it works - deferred here from stage 5.
    /// </summary>
    public static string Truncate(string table) => $"TRUNCATE TABLE {Identifier(table)};";

    #endregion

    #region Column edits

    public static string AddColumn(string table, ColumnDraft column) =>
        $"ALTER TABLE {Identifier(table)} ADD COLUMN {ColumnDefinition(column, inlinePrimaryKey: false)};";

    public static string DropColumn(string table, string column) =>
        $"ALTER TABLE {Identifier(table)} DROP COLUMN {Identifier(column)};";

    public static string RenameColumn(string table, string column, string newName) =>
        $"ALTER TABLE {Identifier(table)} RENAME COLUMN {Identifier(column)} TO {Identifier(newName)};";

    public static string SetDefault(string table, string column, string value) =>
        $"ALTER TABLE {Identifier(table)} ALTER COLUMN {Identifier(column)} SET DEFAULT {value};";

    public static string DropDefault(string table, string column) =>
        $"ALTER TABLE {Identifier(table)} ALTER COLUMN {Identifier(column)} DROP DEFAULT;";

    public static string SetNotNull(string table, string column) =>
        $"ALTER TABLE {Identifier(table)} ALTER COLUMN {Identifier(column)} SET NOT NULL;";

    public static string DropNotNull(string table, string column) =>
        $"ALTER TABLE {Identifier(table)} ALTER COLUMN {Identifier(column)} DROP NOT NULL;";

    #endregion

    #region Constraints

    /// <summary>
    /// The engine names its own constraints PK_t, UQ_t_c, CK_t_c, FK_from_to_c; Studio writes the same
    /// shapes so a constraint it created is not obviously foreign in the catalogue.
    ///
    /// A name is not optional: <c>ALTER TABLE t ADD PRIMARY KEY (c)</c> answers "ADD CONSTRAINT
    /// requires a constraint name".
    /// </summary>
    public static string UniqueName(string table, string column) => $"UQ_{table}_{column}";

    public static string CheckName(string table, string column) => $"CK_{table}_{column}";

    public static string ForeignKeyName(string table, string toTable, string column) => $"FK_{table}_{toTable}_{column}";

    public static string AddUnique(string table, string name, params string[] columns) =>
        $"ALTER TABLE {Identifier(table)} ADD CONSTRAINT {Identifier(name)} UNIQUE ({Columns(columns)});";

    public static string AddCheck(string table, string name, string expression) =>
        $"ALTER TABLE {Identifier(table)} ADD CONSTRAINT {Identifier(name)} CHECK ({expression});";

    public static string AddForeignKey(string table, string name, string column, string toTable, string toColumn) =>
        $"ALTER TABLE {Identifier(table)} ADD CONSTRAINT {Identifier(name)} " +
        $"FOREIGN KEY ({Identifier(column)}) REFERENCES {Identifier(toTable)}({Identifier(toColumn)});";

    public static string DropConstraint(string table, string name) =>
        $"ALTER TABLE {Identifier(table)} DROP CONSTRAINT {Identifier(name)};";

    #endregion

    #region Indexes

    public static string CreateIndex(IndexDraft index)
    {
        var sb = new StringBuilder("CREATE ");

        if (index.IsUnique)
            sb.Append("UNIQUE ");

        sb.Append($"INDEX {Identifier(index.Name)} ON {Identifier(index.Table)} (");
        sb.Append(string.Join(", ", index.Columns.Select(WriteIndexColumn)));
        sb.Append(')');

        if (index.HasIncluded)
            sb.Append($" INCLUDE ({Columns(index.IncludedColumns)})");

        if (index.HasFilter)
            sb.Append($" WHERE {index.FilterCondition}");

        sb.Append(';');

        return sb.ToString();
    }

    /// <summary>
    /// An index column is a name or an expression, and only a name may be quoted - quoting
    /// <c>LOWER(Name)</c> would turn a call into an identifier.
    /// </summary>
    private static string WriteIndexColumn(IndexColumn column)
    {
        var text = PlainIdentifier.IsMatch(column.Expression)
            ? Identifier(column.Expression)
            : column.Expression;

        return column.IsDescending ? text + " DESC" : text;
    }

    /// <summary>
    /// The name alone. <c>DROP INDEX x ON t</c> is a syntax error on this engine - measured, because
    /// it is what most dialects want.
    /// </summary>
    public static string DropIndex(string name) => $"DROP INDEX {Identifier(name)};";

    #endregion

    #region Triggers and views

    public static string CreateTrigger(TriggerDraft trigger)
    {
        var sb = new StringBuilder($"CREATE TRIGGER {Identifier(trigger.Name)} ");
        sb.Append($"{trigger.Timing} {trigger.Event}");

        // UPDATE OF a, b - the columns the trigger watches. The engine honours it since 2026-08-09, so
        // leaving it out of a rebuilt trigger widens the trigger to every column. It is only legal
        // after UPDATE, and an empty list is how "every column" is written.
        if (trigger.UpdateColumns.Count > 0 &&
            trigger.Event.Trim().Equals("UPDATE", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append($" OF {Columns(trigger.UpdateColumns)}");
        }

        sb.Append($" ON {Identifier(trigger.Table)}");

        // Omitting the clause is how a statement trigger is written here; FOR EACH STATEMENT is a
        // parse error.
        if (trigger.ForEachRow)
            sb.Append(" FOR EACH ROW");

        // The brackets are not decoration: WHEN NEW.Total > 100 does not parse, WHEN (NEW.Total > 100)
        // does.
        if (!string.IsNullOrWhiteSpace(trigger.Condition))
            sb.Append($" WHEN ({Unbracket(trigger.Condition)})");

        sb.Append("\nBEGIN\n");
        sb.Append(Indent(trigger.Body.Trim()));
        sb.Append("\nEND;");

        return sb.ToString();
    }

    public static string DropTrigger(string name) => $"DROP TRIGGER {Identifier(name)};";

    public static string CreateView(string name, string body) =>
        $"CREATE VIEW {Identifier(name)} AS\n{body.Trim().TrimEnd(';')};";

    public static string DropView(string name) => $"DROP VIEW {Identifier(name)};";

    #endregion

    #region Routines

    /// <summary>
    /// A whole <c>CREATE FUNCTION</c> or <c>CREATE PROCEDURE</c>, from what the catalogue answers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The brackets after the name are written even when there are no parameters: this grammar has
    /// <c>LPAREN routineParameters? RPAREN</c>, so they are not optional and a function written
    /// without them does not parse.
    /// </para>
    /// <para>
    /// A function’s body is one expression and is written after <c>RETURN</c>; a procedure’s is a
    /// statement list and stands on its own, exactly as a trigger’s does.
    /// </para>
    /// </remarks>
    public static string CreateRoutine(RoutineDraft routine)
    {
        var keyword = routine.IsFunction ? "FUNCTION" : "PROCEDURE";

        var parameters = string.Join(", ", routine.Parameters
            .Select(parameter => $"{Identifier(parameter.Name)} {parameter.Type}"));

        var sb = new StringBuilder($"CREATE {keyword} {Identifier(routine.Name)}({parameters})");

        if (routine.IsFunction)
            sb.Append($" RETURNS {routine.ReturnType}");

        sb.Append("\nAS BEGIN\n");

        var body = routine.Body.Trim().TrimEnd(';');

        sb.Append(Indent(routine.IsFunction ? $"RETURN {body};" : body + ";"));

        sb.Append("\nEND;");

        return sb.ToString();
    }

    public static string DropRoutine(string name, bool isFunction) =>
        $"DROP {(isFunction ? "FUNCTION" : "PROCEDURE")} {Identifier(name)};";

    #endregion

    #region Tools

    private static string Columns(IEnumerable<string> columns) =>
        string.Join(", ", columns.Select(Identifier));

    /// <summary>
    /// Takes one layer of brackets off a condition the user may have written them into, so the writer
    /// does not produce ((x)).
    /// </summary>
    private static string Unbracket(string condition)
    {
        var text = condition.Trim();

        if (text.Length > 1 && text[0] == '(' && text[^1] == ')' && Balanced(text[1..^1]))
            return text[1..^1].Trim();

        return text;
    }

    private static bool Balanced(string text)
    {
        var depth = 0;

        foreach (var c in text)
        {
            if (c == '(')
                depth++;
            else if (c == ')' && --depth < 0)
                return false;
        }

        return depth == 0;
    }

    private static string Indent(string body) =>
        string.Join("\n", body.Split('\n').Select(line => "    " + line.TrimEnd()));

    #endregion
}
