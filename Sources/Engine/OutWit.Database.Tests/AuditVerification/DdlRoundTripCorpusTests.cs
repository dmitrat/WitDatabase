using OutWit.Database.Definitions;

namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// Phase 7 instrument — the DDL round-trip corpus.
/// </summary>
/// <remarks>
/// <para>
/// The phase's premise is that a declaration can fail in three independent ways, and the recorded
/// findings show all three diverging. So the corpus asks three questions of every entry, not one:
/// </para>
/// <list type="number">
/// <item><b>Recorded</b> — did the declaration reach the catalog?</item>
/// <item><b>Reported</b> — does <c>INFORMATION_SCHEMA</c> describe it?</item>
/// <item><b>Enforced</b> — is a value that violates it refused?</item>
/// </list>
/// <para>
/// <b>Why an instrument was needed at all.</b> A whole class — declared sizes — went unrecorded through
/// a 104-finding audit, and surfaced only when enforcement was written and never fired. Reading the DDL
/// path cannot find that; asking the database what it thinks it stored can.
/// </para>
/// <para>
/// <b>The result is pinned as data, not as expectations.</b> The corpus writes a table and compares it
/// against <c>Schema/ddl-round-trip-corpus.txt</c>, failing in <b>both</b> directions: a declaration that
/// stops being honoured is a regression, and one that starts being honoured means the pinned file is
/// stale. The file is a record of the current state and not a target - the same shape as phase 3's
/// keyword corpus, and for the same reason: a diff to it should read as "these declarations changed
/// status".
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class DdlRoundTripCorpusTests : WitSqlEngineTestsBase
{
    #region Corpus

    /// <summary>
    /// One declaration, and what it claims. <see cref="Violation"/> is the INSERT that the declaration,
    /// if enforced, must refuse.
    /// </summary>
    private sealed record Entry(
        string Name,
        string Columns,
        string Property,
        string Violation,
        Func<DdlRoundTripCorpusTests, bool> Recorded);

    /// <summary>
    /// The corpus. Every entry is a shape the grammar accepts, so "it does not parse" is never the
    /// answer here - what is in question is what happens to it afterwards.
    /// </summary>
    private static readonly Entry[] CORPUS =
    [
        new("varchar-length", "S VARCHAR(5)", "MaxLength = 5",
            "INSERT INTO T (S) VALUES ('123456')",
            t => t.Column("S").MaxLength == 5),

        new("char-length", "S CHAR(3)", "MaxLength = 3",
            "INSERT INTO T (S) VALUES ('1234')",
            t => t.Column("S").MaxLength == 3),

        new("decimal-precision-scale", "V DECIMAL(5,2)", "Precision = 5, Scale = 2",
            "INSERT INTO T (V) VALUES (123456.789)",
            t => t.Column("V").Precision == 5 && t.Column("V").Scale == 2),

        new("numeric-precision-scale", "V NUMERIC(4,1)", "Precision = 4, Scale = 1",
            "INSERT INTO T (V) VALUES (99999.99)",
            t => t.Column("V").Precision == 4 && t.Column("V").Scale == 1),

        new("not-null", "S TEXT NOT NULL", "IsNullable = false",
            "INSERT INTO T (S) VALUES (NULL)",
            t => !t.Column("S").Nullable),

        new("default", "S TEXT DEFAULT 'x'", "DefaultValue = 'x'",
            "",
            t => !string.IsNullOrEmpty(t.Column("S").DefaultValue)),

        new("primary-key", "Id INTEGER PRIMARY KEY", "IsPrimaryKey = true",
            "INSERT INTO T (Id) VALUES (1)",
            t => t.Column("Id").IsPrimaryKey),

        new("unique", "S TEXT UNIQUE", "IsUnique = true",
            "INSERT INTO T (S) VALUES ('a')",
            t => t.Column("S").IsUnique),

        // A COLUMN-level check lands on the column, a TABLE-level one on the table, so the corpus asks
        // both rather than one - the first version asked only the table and reported a check that is
        // enforced as unrecorded, which would have been the instrument's mistake, not a finding.
        new("check-column", "V INTEGER CHECK (V > 0)", "a column CHECK",
            "INSERT INTO T (V) VALUES (0)",
            t => !string.IsNullOrEmpty(t.Column("V").CheckExpression)),

        new("check-table", "V INTEGER, CHECK (V > 0)", "a table CHECK",
            "INSERT INTO T (V) VALUES (0)",
            t => t.HasTableCheck("T")),
    ];

    /// <summary>
    /// The INSERT that must succeed before the violating one is tried, so that a refusal cannot be
    /// mistaken for a table that never worked.
    /// </summary>
    private static readonly Dictionary<string, string> SEED = new()
    {
        ["primary-key"] = "INSERT INTO T (Id) VALUES (1)",
        ["unique"] = "INSERT INTO T (S) VALUES ('a')",
    };

    #endregion

    #region Tests

    /// <summary>
    /// The corpus itself: every declaration, round-tripped, and the table compared against the pinned
    /// record.
    /// </summary>
    [Test]
    public void EveryDeclarationIsRecordedReportedAndEnforcedOrPinnedTest()
    {
        var observed = CORPUS.Select(Measure).ToArray();

        foreach (var line in observed)
            TestContext.Out.WriteLine($"CORPUS  {line}");

        var pinned = LoadPinnedCorpus();

        var changed = observed.Except(pinned).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var missing = pinned.Except(observed).OrderBy(x => x, StringComparer.Ordinal).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.Empty,
                "these declarations changed status - if the change is a fix, update the pinned corpus");
            Assert.That(missing, Is.Empty,
                "these pinned declarations no longer appear - the corpus or the pinned file is stale");
        });
    }

    /// <summary>
    /// Control: the harness can see a property when there IS one. Without this, a corpus of "no, no,
    /// no" would be indistinguishable from a corpus that never asked.
    /// </summary>
    [Test]
    public void ControlThePlainColumnPropertiesDoArriveTest()
    {
        Execute("CREATE TABLE T (Id INTEGER PRIMARY KEY, S TEXT NOT NULL)");

        Assert.Multiple(() =>
        {
            Assert.That(Column("Id").IsPrimaryKey, Is.True, "a primary key must reach the catalog");
            Assert.That(Column("S").Nullable, Is.False, "NOT NULL must reach the catalog");
            Assert.That(Column("S").Name, Is.EqualTo("S"), "and the column must be found at all");
        });
    }

    #endregion

    #region Measurement

    /// <summary>
    /// Runs one entry through all three questions, on its own table.
    /// </summary>
    private string Measure(Entry entry)
    {
        Execute("DROP TABLE IF EXISTS T");
        Execute($"CREATE TABLE T ({entry.Columns})");

        var recorded = Ask(() => entry.Recorded(this));
        var reported = AskInformationSchema(entry);
        var enforced = AskEnforcement(entry);

        return $"{entry.Name,-26} {entry.Property,-28} recorded={recorded,-7} reported={reported,-7} enforced={enforced}";
    }

    /// <summary>
    /// What <c>INFORMATION_SCHEMA</c> says about the same declaration. Only the size columns have a
    /// place to say it, so the rest report <c>n/a</c> rather than a misleading "no".
    /// </summary>
    private string AskInformationSchema(Entry entry)
    {
        var column = entry.Name switch
        {
            "varchar-length" or "char-length" => "CHARACTER_MAXIMUM_LENGTH",
            "decimal-precision-scale" or "numeric-precision-scale" => "NUMERIC_PRECISION",
            "not-null" => "IS_NULLABLE",
            "default" => "COLUMN_DEFAULT",
            _ => null
        };

        if (column == null)
            return "n/a";

        return Ask(() =>
        {
            var result = m_engine.Execute(
                $"SELECT {column} FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'T'");

            if (!result.Read())
                return false;

            var value = result.CurrentRow[0];

            // "reported" means it says something other than nothing. IS_NULLABLE always answers, so for
            // that one the question is whether it answers NO.
            return entry.Name == "not-null"
                ? value.ToObject()?.ToString() == "NO"
                : !value.IsNull;
        });
    }

    /// <summary>
    /// Whether a value that violates the declaration is refused. The seed insert goes first where one
    /// is needed, so a refusal cannot be a table that never accepted anything.
    /// </summary>
    private string AskEnforcement(Entry entry)
    {
        if (string.IsNullOrEmpty(entry.Violation))
            return "n/a";

        if (SEED.TryGetValue(entry.Name, out var seed))
        {
            try
            {
                m_engine.Execute(seed);
            }
            catch (Exception e)
            {
                return $"seed-failed({e.GetType().Name})";
            }
        }

        try
        {
            m_engine.Execute(entry.Violation);
            return "no";
        }
        catch
        {
            return "yes";
        }
    }

    private static string Ask(Func<bool> question)
    {
        try
        {
            return question() ? "yes" : "no";
        }
        catch (Exception e)
        {
            return $"threw({e.GetType().Name})";
        }
    }

    #endregion

    #region Tools

    private void Execute(string sql) => m_engine.Execute(sql);

    private DefinitionColumn Column(string name)
    {
        var table = m_engine.Catalog.GetTable("T")
                    ?? throw new InvalidOperationException("table T is not in the catalog");

        return table.Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"column {name} is not in the catalog");
    }

    /// <summary>
    /// Whether the table carries a CHECK expression at all. Deliberately the weakest possible question:
    /// the corpus is asking whether the declaration survived, not what it survived as.
    /// </summary>
    private bool HasTableCheck(string table) =>
        m_engine.Catalog.GetTable(table)?.CheckExpressions?.Count > 0;

    /// <summary>
    /// The pinned record lives in a file because it is data rather than logic, and because a diff to it
    /// should read as "these declarations changed status" rather than as a code change.
    /// </summary>
    private static string[] LoadPinnedCorpus()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory,
            "Schema", "ddl-round-trip-corpus.txt");

        if (!File.Exists(path))
            throw new FileNotFoundException($"the pinned DDL corpus is missing at {path}", path);

        return File.ReadAllLines(path)
            .Select(line => line.TrimEnd())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();
    }

    #endregion
}
