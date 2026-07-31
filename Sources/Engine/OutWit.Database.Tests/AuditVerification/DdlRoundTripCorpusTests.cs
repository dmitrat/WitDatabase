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
        string[] Setup,
        string Property,
        string Violation,
        Func<DdlRoundTripCorpusTests, bool> Recorded);

    /// <summary>One `CREATE TABLE T (...)` - the common shape, kept short at the call site.</summary>
    private static Entry Table(string name, string columns, string property, string violation,
        Func<DdlRoundTripCorpusTests, bool> recorded) =>
        new(name, [$"CREATE TABLE T ({columns})"], property, violation, recorded);

    /// <summary>
    /// The corpus. Every entry is a shape the grammar accepts, so "it does not parse" is never the
    /// answer here - what is in question is what happens to it afterwards.
    /// </summary>
    private static readonly Entry[] CORPUS =
    [
        Table("varchar-length", "S VARCHAR(5)", "MaxLength = 5",
            "INSERT INTO T (S) VALUES ('123456')",
            t => t.Column("S").MaxLength == 5),

        Table("char-length", "S CHAR(3)", "MaxLength = 3",
            "INSERT INTO T (S) VALUES ('1234')",
            t => t.Column("S").MaxLength == 3),

        Table("decimal-precision-scale", "V DECIMAL(5,2)", "Precision = 5, Scale = 2",
            "INSERT INTO T (V) VALUES (123456.789)",
            t => t.Column("V").Precision == 5 && t.Column("V").Scale == 2),

        Table("numeric-precision-scale", "V NUMERIC(4,1)", "Precision = 4, Scale = 1",
            "INSERT INTO T (V) VALUES (99999.99)",
            t => t.Column("V").Precision == 4 && t.Column("V").Scale == 1),

        Table("not-null", "S TEXT NOT NULL", "IsNullable = false",
            "INSERT INTO T (S) VALUES (NULL)",
            t => !t.Column("S").Nullable),

        Table("default", "S TEXT DEFAULT 'x'", "DefaultValue = 'x'",
            "",
            t => !string.IsNullOrEmpty(t.Column("S").DefaultValue)),

        Table("primary-key", "Id INTEGER PRIMARY KEY", "IsPrimaryKey = true",
            "INSERT INTO T (Id) VALUES (1)",
            t => t.Column("Id").IsPrimaryKey),

        Table("unique", "S TEXT UNIQUE", "IsUnique = true",
            "INSERT INTO T (S) VALUES ('a')",
            t => t.Column("S").IsUnique),

        // A COLUMN-level check lands on the column, a TABLE-level one on the table, so the corpus asks
        // both rather than one - the first version asked only the table and reported a check that is
        // enforced as unrecorded, which would have been the instrument's mistake, not a finding.
        Table("check-column", "V INTEGER CHECK (V > 0)", "a column CHECK",
            "INSERT INTO T (V) VALUES (0)",
            t => !string.IsNullOrEmpty(t.Column("V").CheckExpression)),

        Table("check-table", "V INTEGER, CHECK (V > 0)", "a table CHECK",
            "INSERT INTO T (V) VALUES (0)",
            t => t.HasTableCheck("T")),

        // Named constraints declared inline. The interesting split here is enforced-but-anonymous: the
        // constraint works and cannot be dropped, because the name never reached the catalog.
        Table("named-check", "V INTEGER, CONSTRAINT ck_v CHECK (V > 0)", "a constraint named ck_v",
            "INSERT INTO T (V) VALUES (0)",
            t => t.HasNamedConstraint("ck_v")),

        Table("named-unique", "S TEXT, CONSTRAINT uq_s UNIQUE (S)", "a constraint named uq_s",
            "INSERT INTO T (S) VALUES ('a')",
            t => t.HasNamedConstraint("uq_s")),

        new("named-foreign-key",
            [
                "CREATE TABLE P (Id INTEGER PRIMARY KEY)",
                "CREATE TABLE T (Id INTEGER PRIMARY KEY, Pid INTEGER, CONSTRAINT fk_p FOREIGN KEY (Pid) REFERENCES P (Id))"
            ],
            "a constraint named fk_p",
            "INSERT INTO T (Id, Pid) VALUES (1, 999)",
            t => t.HasNamedConstraint("fk_p")),

        // ALTER TABLE ADD COLUMN, which the markers say discards everything but the type.
        new("add-column-unique",
            ["CREATE TABLE T (Id INTEGER PRIMARY KEY)", "ALTER TABLE T ADD COLUMN S TEXT UNIQUE"],
            "IsUnique = true on the added column",
            "INSERT INTO T (Id, S) VALUES (2, 'a')",
            t => t.Column("S").IsUnique),

        new("add-column-check",
            ["CREATE TABLE T (Id INTEGER PRIMARY KEY)", "ALTER TABLE T ADD COLUMN V INTEGER CHECK (V > 0)"],
            "a CHECK on the added column",
            "INSERT INTO T (Id, V) VALUES (1, 0)",
            t => !string.IsNullOrEmpty(t.Column("V").CheckExpression)),

        new("add-column-references",
            [
                "CREATE TABLE P (Id INTEGER PRIMARY KEY)",
                "CREATE TABLE T (Id INTEGER PRIMARY KEY)",
                "ALTER TABLE T ADD COLUMN Pid INTEGER REFERENCES P (Id)"
            ],
            "a foreign key on the added column",
            "INSERT INTO T (Id, Pid) VALUES (1, 999)",
            t => t.Column("Pid").ForeignKey != null),
    ];

    /// <summary>
    /// The INSERT that must succeed before the violating one is tried, so that a refusal cannot be
    /// mistaken for a table that never worked.
    /// </summary>
    private static readonly Dictionary<string, string> SEED = new()
    {
        ["primary-key"] = "INSERT INTO T (Id) VALUES (1)",
        ["unique"] = "INSERT INTO T (S) VALUES ('a')",
        ["named-unique"] = "INSERT INTO T (S) VALUES ('a')",
        ["add-column-unique"] = "INSERT INTO T (Id, S) VALUES (1, 'a')",
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

    /// <summary>
    /// Probe: what <c>DROP COLUMN</c> leaves behind. Not a corpus entry, because the question is not
    /// "did a declaration survive" but "did removing one corrupt the metadata of the others".
    /// </summary>
    /// <remarks>
    /// The recorded finding says foreign-key and primary-key metadata keep pointing at the dropped
    /// column while index and UNIQUE metadata are cleaned up - two of four. Measured here rather than
    /// taken on trust, because a claim about which half is broken is exactly the kind that drifts.
    /// </remarks>
    [Test]
    public void ProbeWhatDropColumnLeavesBehindTest()
    {
        Execute("DROP TABLE IF EXISTS T");
        Execute("DROP TABLE IF EXISTS P");
        Execute("CREATE TABLE P (Id INTEGER PRIMARY KEY)");
        Execute("CREATE TABLE T (Id INTEGER PRIMARY KEY, Pid INTEGER REFERENCES P (Id), S TEXT UNIQUE, N INTEGER)");
        Execute("CREATE INDEX ix_n ON T (N)");

        Execute("ALTER TABLE T DROP COLUMN Pid");
        Execute("ALTER TABLE T DROP COLUMN S");
        Execute("ALTER TABLE T DROP COLUMN N");

        var table = m_engine.Catalog.GetTable("T")!;
        var columns = table.Columns.Select(c => c.Name).ToArray();

        var foreignKeysLeft = table.ForeignKeys?.Count(fk => !fk.Columns.All(columns.Contains)) ?? 0;
        var uniqueLeft = table.UniqueConstraints?.Count(u => !u.All(columns.Contains)) ?? 0;
        var primaryKeyIsSound = table.PrimaryKey?.All(columns.Contains) ?? true;

        string insert;
        try
        {
            Execute("INSERT INTO T (Id) VALUES (1)");
            insert = "accepted";
        }
        catch (Exception e)
        {
            insert = $"threw {e.GetType().Name}";
        }

        TestContext.Out.WriteLine(
            $"PROBE  after dropping three columns  ->  stale foreign keys={foreignKeysLeft}, "
            + $"stale unique={uniqueLeft}, primary key sound={primaryKeyIsSound}, next insert {insert}");

        // ASSERTS CORRECT BEHAVIOUR - a failure here confirms the finding. Dropping a column must take
        // its metadata with it, and the table must still accept rows afterwards.
        Assert.Multiple(() =>
        {
            Assert.That(foreignKeysLeft, Is.Zero, "a foreign key still points at a dropped column");
            Assert.That(uniqueLeft, Is.Zero, "a unique constraint still points at a dropped column");
            Assert.That(primaryKeyIsSound, Is.True, "the primary key points at a dropped column");
            Assert.That(insert, Is.EqualTo("accepted"), "the table stopped accepting rows");
        });
    }

    /// <summary>
    /// The sharper shapes of the same question: dropping the column the PRIMARY KEY is on, and dropping
    /// a column another table's foreign key points AT.
    /// </summary>
    /// <remarks>
    /// The first probe drops columns that only their own table refers to, and it comes back clean. That
    /// is not enough to call the recorded finding stale: the two shapes below are where metadata is held
    /// by something other than the column being dropped, and they are the ones a claim about "leaves
    /// metadata behind" would have to survive.
    /// </remarks>
    [Test]
    [TestCase("primary-key-column", "CREATE TABLE T (Id INTEGER PRIMARY KEY, S TEXT)", "Id")]
    [TestCase("column-a-foreign-key-points-at", "CREATE TABLE T (Id INTEGER PRIMARY KEY, S TEXT)", "Id")]
    public void ProbeDroppingAColumnSomethingElseDependsOnTest(string shape, string create, string column)
    {
        Execute("DROP TABLE IF EXISTS C");
        Execute("DROP TABLE IF EXISTS T");
        Execute(create);

        if (shape == "column-a-foreign-key-points-at")
            Execute("CREATE TABLE C (Id INTEGER PRIMARY KEY, Tid INTEGER REFERENCES T (Id))");

        string drop;
        try
        {
            Execute($"ALTER TABLE T DROP COLUMN {column}");
            drop = "accepted";
        }
        catch (Exception e)
        {
            drop = $"refused with {e.GetType().Name}";
        }

        var table = m_engine.Catalog.GetTable("T");
        var columns = table?.Columns.Select(c => c.Name).ToArray() ?? [];
        var pkSound = table?.PrimaryKey?.All(columns.Contains) ?? true;

        string insert;
        try
        {
            Execute("INSERT INTO T (S) VALUES ('a')");
            insert = "accepted";
        }
        catch (Exception e)
        {
            insert = $"threw {e.GetType().Name}";
        }

        TestContext.Out.WriteLine(
            $"PROBE  [{shape}] drop {drop}; primary key sound={pkSound}; next insert {insert}");

        // PINS CORRECT BEHAVIOUR, measured 2026-07-31. Dropping a column something else depends on is
        // REFUSED rather than accepted-and-corrupting, which is what PostgreSQL and SQL Server do too.
        // This is the half of the recorded DROP COLUMN finding that turned out to be stale: phase 1
        // fixed the metadata half in 2.2.0, and the plan carried the pre-fix wording forward.
        Assert.Multiple(() =>
        {
            Assert.That(drop, Does.StartWith("refused"),
                "dropping a column the primary key or a foreign key depends on must be refused");
            Assert.That(pkSound, Is.True, "the primary key must not point at a column that is gone");
            Assert.That(insert, Is.EqualTo("accepted"), "and the table must still accept rows");
        });
    }

    /// <summary>
    /// The declared-size rules, executed rather than only described. They follow PostgreSQL and SQL
    /// Server rather than being stricter than them, because drop-in is the target - and a rule chosen
    /// deliberately deserves a test that fails if someone later chooses differently.
    /// </summary>
    [Test]
    public void ProbeTheDeclaredSizeRulesTest()
    {
        Execute("DROP TABLE IF EXISTS T");
        Execute("CREATE TABLE T (Id INTEGER PRIMARY KEY, S VARCHAR(5), V DECIMAL(5,2))");

        Assert.Multiple(() =>
        {
            // A string that fits is accepted, and one that does not is REFUSED rather than truncated:
            // silently losing the end of a value is the one outcome nobody can want.
            Assert.That(() => Execute("INSERT INTO T (Id, S) VALUES (1, '12345')"), Throws.Nothing,
                "a string of exactly the declared length must be accepted");

            Assert.That(() => Execute("INSERT INTO T (Id, S) VALUES (2, '123456')"),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("too long"),
                "a string longer than its column must be refused, not truncated");

            // More decimals than the scale are ACCEPTED rather than refused, which is what PostgreSQL
            // does - it rounds. Rounding the stored value is the half still missing; see the pin below.
            Assert.That(() => Execute("INSERT INTO T (Id, V) VALUES (3, 123.456)"), Throws.Nothing,
                "more decimals than the scale must not be an error");

            // But an integer part that does not fit precision - scale is overflow, and no rounding
            // saves it.
            Assert.That(() => Execute("INSERT INTO T (Id, V) VALUES (4, 12345.67)"),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("out of range"),
                "an integer part too large for the precision must be refused");
        });

        var stored = m_engine.Execute("SELECT V FROM T WHERE Id = 3");
        stored.Read();

        TestContext.Out.WriteLine($"PROBE  123.456 into DECIMAL(5,2) is stored as  ->  {stored.CurrentRow[0].ToObject()}");

        // INVERTED BY THE FIX, and the inversion is the proof it landed. This used to read 123.456 and
        // pass: precision was checked against a rounded value while the original was stored, so the
        // declared scale was a thing the catalog said and the data ignored.
        Assert.That(stored.CurrentRow[0].AsDecimal(), Is.EqualTo(123.46m),
            "the value must be stored at the scale its column declared");
    }

    /// <summary>
    /// The scale is applied on every path that writes a row, not only on the one that was tested first.
    /// </summary>
    /// <remarks>
    /// Six call sites reach the write path - two inserts, an upsert, two merge branches and an update -
    /// and a missed one would store an unrounded value in silence. One test per path is the only way to
    /// know they were all found; the alternative is trusting a grep.
    /// </remarks>
    [Test]
    public void ProbeTheScaleIsAppliedOnEveryWritePathTest()
    {
        Execute("DROP TABLE IF EXISTS T");
        Execute("CREATE TABLE T (Id INTEGER PRIMARY KEY, V DECIMAL(6,2))");

        Execute("INSERT INTO T (Id, V) VALUES (1, 1.111)");
        Execute("UPDATE T SET V = 2.222 WHERE Id = 1");

        Execute("INSERT INTO T (Id, V) VALUES (2, 3.333)");

        Assert.Multiple(() =>
        {
            Assert.That(Scalar("SELECT V FROM T WHERE Id = 1"), Is.EqualTo(2.22m), "after UPDATE");
            Assert.That(Scalar("SELECT V FROM T WHERE Id = 2"), Is.EqualTo(3.33m), "after INSERT");
        });
    }

    /// <summary>
    /// The declared size is enforced on UPDATE as well as on INSERT.
    /// </summary>
    /// <remarks>
    /// It was not, and nothing said so. UPDATE has a fast path with its own validation entry point -
    /// a third one beside the two the insert paths use - and the size check added with the rest of this
    /// phase reached it nowhere, so a hundred characters could be written into a VARCHAR(5) by updating
    /// a row that already existed. Found by the scale test above rather than by looking, which is the
    /// argument for one test per write path instead of one per feature.
    /// </remarks>
    [Test]
    public void ProbeTheDeclaredLengthIsEnforcedOnUpdateTest()
    {
        Execute("DROP TABLE IF EXISTS T");
        Execute("CREATE TABLE T (Id INTEGER PRIMARY KEY, S VARCHAR(5))");
        Execute("INSERT INTO T (Id, S) VALUES (1, 'ok')");

        Assert.Multiple(() =>
        {
            Assert.That(() => Execute("UPDATE T SET S = '12345' WHERE Id = 1"), Throws.Nothing,
                "a value of exactly the declared length must still be accepted");

            Assert.That(() => Execute("UPDATE T SET S = '123456' WHERE Id = 1"),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("too long"),
                "an over-long value must be refused on UPDATE, not only on INSERT");
        });
    }

    private decimal Scalar(string sql)
    {
        var result = m_engine.Execute(sql);
        result.Read();

        return result.CurrentRow[0].AsDecimal();
    }

    #endregion

    #region Measurement

    /// <summary>
    /// Runs one entry through all three questions, on its own table.
    /// </summary>
    private string Measure(Entry entry)
    {
        Execute("DROP TABLE IF EXISTS T");
        Execute("DROP TABLE IF EXISTS P");

        foreach (var statement in entry.Setup)
            Execute(statement);

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

        if (entry.Name.StartsWith("named-", StringComparison.Ordinal))
            return AskConstraintIsListed(entry.Property.Split(' ').Last());

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
    /// Whether INFORMATION_SCHEMA.TABLE_CONSTRAINTS lists the constraint under the name it was given.
    /// </summary>
    private string AskConstraintIsListed(string name) => Ask(() =>
    {
        var result = m_engine.Execute(
            $"SELECT CONSTRAINT_NAME FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE TABLE_NAME = 'T'");

        while (result.Read())
        {
            if (string.Equals(result.CurrentRow[0].ToObject()?.ToString(), name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    });

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
    private bool HasNamedConstraint(string name) =>
        m_engine.Catalog.GetTable("T")?.GetConstraint(name) != null;

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
