using OutWit.Database.Core.Builder;
using OutWit.Database.Definitions;
using OutWit.Database.Engine;
using OutWit.Database.Parser;
using OutWit.Database.Types;

namespace OutWit.Database.Tests.Schema;

/// <summary>
/// The routine catalog: what a function and a procedure are once they are stored.
/// </summary>
/// <remarks>
/// <para>
/// Phase 9d, first step. There is no grammar for <c>CREATE FUNCTION</c> yet, so everything here goes
/// through the catalog API - which is deliberate: the catalog is the thing being built, and testing
/// it through a parser that does not exist yet would mean building two things before either could be
/// checked.
/// </para>
/// <para>
/// <b>The test that matters is the one that closes the database and opens it again.</b> Storing a
/// definition is easy; storing one that survives MemoryPack in both directions is the part phase 8
/// spent a whole audit on. A body that round-trips wrongly is not an error, it is a routine that
/// quietly does something else - measured in phase 8 as a view stored as half of itself, answering
/// queries from half its rows.
/// </para>
/// </remarks>
[TestFixture]
public sealed class RoutineCatalogTests
{
    #region Setup

    private string m_directory = null!;
    private string m_path = null!;

    [SetUp]
    public void Setup()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"WitDbRoutines_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
        m_path = Path.Combine(m_directory, "test.witdb");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_directory))
                Directory.Delete(m_directory, recursive: true);
        }
        catch (IOException)
        {
            // A file still held by the OS is not a test failure.
        }
    }

    #endregion

    #region In one session

    [Test]
    public void AFunctionIsFoundAfterItIsCreatedTest()
    {
        using var engine = InMemory();

        engine.CreateFunction(Doubled());

        var stored = engine.GetFunction("Doubled");

        Assert.Multiple(() =>
        {
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.ReturnType, Is.EqualTo(WitDataType.Int32));
            Assert.That(stored.Parameters, Has.Count.EqualTo(1));
            Assert.That(stored.Parameters![0].Name, Is.EqualTo("N"));
            Assert.That(stored.IsDeterministic, Is.True);
        });
    }

    [Test]
    public void AProcedureIsFoundAfterItIsCreatedTest()
    {
        using var engine = InMemory();

        engine.CreateProcedure(LogEverything());

        var stored = engine.GetProcedure("LogEverything");

        Assert.Multiple(() =>
        {
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.Statements, Has.Count.EqualTo(2));
            Assert.That(stored.Parameters, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void LookupIsCaseInsensitiveTest()
    {
        using var engine = InMemory();

        engine.CreateFunction(Doubled());

        Assert.That(engine.GetFunction("DOUBLED"), Is.Not.Null,
            "every other catalog dictionary is case-insensitive, and a routine name is an identifier "
            + "like any other");
    }

    /// <summary>
    /// One namespace for both kinds.
    /// </summary>
    /// <remarks>
    /// Both drop-in targets do this, and it is what keeps the resolver honest: a name must not mean
    /// one object in a <c>CALL</c> and a different one in an expression.
    /// </remarks>
    [Test]
    public void AFunctionAndAProcedureCannotShareANameTest()
    {
        using var engine = InMemory();

        engine.CreateFunction(Doubled());

        Assert.That(() => engine.CreateProcedure(ProcedureNamed("Doubled")),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("Doubled"));
    }

    [Test]
    public void CreatingTheSameFunctionTwiceIsRefusedTest()
    {
        using var engine = InMemory();

        engine.CreateFunction(Doubled());

        Assert.That(() => engine.CreateFunction(Doubled()),
            Throws.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public void DroppingSaysWhetherThereWasAnythingToDropTest()
    {
        using var engine = InMemory();

        engine.CreateFunction(Doubled());

        Assert.Multiple(() =>
        {
            Assert.That(engine.DropFunction("Doubled"), Is.True);
            Assert.That(engine.DropFunction("Doubled"), Is.False);
            Assert.That(engine.GetFunction("Doubled"), Is.Null);
            Assert.That(engine.DropProcedure("NeverExisted"), Is.False);
        });
    }

    /// <summary>
    /// A table and a routine may share a name: a routine is not a table source.
    /// </summary>
    [Test]
    public void ARoutineMayShareANameWithATableTest()
    {
        using var engine = InMemory();

        engine.Execute("CREATE TABLE Orders (Id INT PRIMARY KEY)");

        Assert.That(() => engine.CreateFunction(FunctionNamed("Orders")), Throws.Nothing,
            "refusing this would be a restriction with no failure behind it");
    }

    #endregion

    #region Across a close and reopen

    /// <summary>
    /// A function body survives being written to a file and read back.
    /// </summary>
    /// <remarks>
    /// The expression tree goes through MemoryPack in both directions. If it comes back as anything
    /// other than what went in, the routine quietly computes something else, and no error is raised
    /// anywhere - which is the shape phase 8 found in stored views.
    /// </remarks>
    [Test]
    public void AFunctionSurvivesCloseAndReopenTest()
    {
        using (var database = WitDatabase.Create(m_path))
        using (var engine = new WitSqlEngine(database))
        {
            engine.CreateFunction(Doubled());
        }

        using (var database = WitDatabase.Open(m_path))
        using (var engine = new WitSqlEngine(database))
        {
            var stored = engine.GetFunction("Doubled");

            Assert.Multiple(() =>
            {
                Assert.That(stored, Is.Not.Null);
                Assert.That(stored!.ReturnType, Is.EqualTo(WitDataType.Int32));
                Assert.That(stored.IsDeterministic, Is.True);
                Assert.That(stored.Parameters![0].Name, Is.EqualTo("N"));
                Assert.That(stored.Parameters[0].Type, Is.EqualTo(WitDataType.Int32));

                // The body itself, compared as a tree rather than as text - the text is a rendering
                // and this is the thing the engine will evaluate.
                Assert.That(stored.Body.Is(Doubled().Body), Is.True,
                    "the body must come back as the expression that went in");

                // And the comparison must be capable of saying no. Without this line the assertion
                // above passes for any body at all if Is() is ever loosened, and a round-trip test
                // that cannot fail is the instrument this project has shipped broken before.
                Assert.That(stored.Body.Is(WitSql.ParseExpression("N * 3")), Is.False,
                    "the tree comparison must distinguish one body from another");
            });
        }
    }

    /// <summary>
    /// And a procedure body, which is a list of statements rather than one expression.
    /// </summary>
    [Test]
    public void AProcedureSurvivesCloseAndReopenTest()
    {
        using (var database = WitDatabase.Create(m_path))
        using (var engine = new WitSqlEngine(database))
        {
            engine.Execute("CREATE TABLE Log (Id INT PRIMARY KEY, Note VARCHAR(100))");
            engine.CreateProcedure(LogEverything());
        }

        using (var database = WitDatabase.Open(m_path))
        using (var engine = new WitSqlEngine(database))
        {
            var stored = engine.GetProcedure("LogEverything");

            Assert.Multiple(() =>
            {
                Assert.That(stored, Is.Not.Null);
                Assert.That(stored!.Statements, Has.Count.EqualTo(2));
                Assert.That(stored.Statements[0].Is(LogEverything().Statements[0]), Is.True);
                Assert.That(stored.Statements[1].Is(LogEverything().Statements[1]), Is.True);
            });
        }
    }

    /// <summary>
    /// A body clause that the renderer would drop must still survive, because nothing renders it.
    /// </summary>
    /// <remarks>
    /// This is the phase-8 lesson applied to a new type before it can go wrong: a trigger body of
    /// <c>INSERT … ON CONFLICT DO NOTHING</c> was stored as a plain <c>INSERT</c>, so the conflict
    /// handling vanished silently. Storing trees rather than text is what prevents it, and this test
    /// is what proves the new type does store trees.
    /// </remarks>
    [Test]
    public void AProcedureBodyKeepsAClauseTheRendererCouldDropTest()
    {
        var body = WitSql.Parse("INSERT INTO Log (Id, Note) VALUES (1, 'once') ON CONFLICT DO NOTHING")
            .ToList();

        using (var database = WitDatabase.Create(m_path))
        using (var engine = new WitSqlEngine(database))
        {
            engine.Execute("CREATE TABLE Log (Id INT PRIMARY KEY, Note VARCHAR(100))");
            engine.CreateProcedure(new DefinitionProcedure { Name = "Once", Statements = body });
        }

        using (var database = WitDatabase.Open(m_path))
        using (var engine = new WitSqlEngine(database))
        {
            var stored = engine.GetProcedure("Once");

            Assert.That(stored!.Statements[0].Is(body[0]), Is.True,
                "the ON CONFLICT clause must survive storage - it is the clause that disappeared "
                + "from trigger bodies when they were stored as rendered text");
        }
    }

    /// <summary>
    /// A drop survives too, rather than coming back on the next open.
    /// </summary>
    [Test]
    public void ADroppedRoutineStaysDroppedTest()
    {
        using (var database = WitDatabase.Create(m_path))
        using (var engine = new WitSqlEngine(database))
        {
            engine.CreateFunction(Doubled());
            engine.DropFunction("Doubled");
        }

        using (var database = WitDatabase.Open(m_path))
        using (var engine = new WitSqlEngine(database))
        {
            Assert.That(engine.GetFunction("Doubled"), Is.Null);
        }
    }

    /// <summary>
    /// A database written before routines existed opens, and reports none.
    /// </summary>
    /// <remarks>
    /// The two store records are new keys, so an older file simply has neither. Asserted rather than
    /// assumed: "an added key cannot break an old file" is exactly the kind of claim this project has
    /// found false before, and the cost of checking is one test.
    /// </remarks>
    [Test]
    public void ADatabaseWithNoRoutineRecordsOpensTest()
    {
        using (var database = WitDatabase.Create(m_path))
        using (var engine = new WitSqlEngine(database))
        {
            engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY)");
            engine.Execute("INSERT INTO T (Id) VALUES (1)");
        }

        using (var database = WitDatabase.Open(m_path))
        using (var engine = new WitSqlEngine(database))
        {
            Assert.Multiple(() =>
            {
                Assert.That(engine.GetFunctions(), Is.Empty);
                Assert.That(engine.GetProcedures(), Is.Empty);
                Assert.That(engine.Query("SELECT COUNT(*) FROM T")[0][0].AsInt64(), Is.EqualTo(1));
            });
        }
    }

    #endregion

    #region Inside a transaction

    /// <summary>
    /// A routine created in a rolled-back transaction must not be there afterwards.
    /// </summary>
    /// <remarks>
    /// Free, in the sense that the routine records go through the same <c>PutSchemaRecord</c> as
    /// every other schema record and therefore through the caller's transaction. Asserted because
    /// "it should follow" is how the row counters got their transaction treatment while the schema
    /// blobs were left out of it for a whole release.
    /// </remarks>
    [Test]
    public void ARoutineCreatedInARolledBackTransactionIsGoneTest()
    {
        using var engine = InMemory();

        engine.Execute("BEGIN TRANSACTION");
        engine.CreateFunction(Doubled());
        engine.Execute("ROLLBACK");

        Assert.That(engine.GetFunction("Doubled"), Is.Null,
            "the rollback must discard the routine with everything else the transaction wrote");
    }

    [Test]
    public void ARoutineCreatedInACommittedTransactionStaysTest()
    {
        using var engine = InMemory();

        engine.Execute("BEGIN TRANSACTION");
        engine.CreateFunction(Doubled());
        engine.Execute("COMMIT");

        Assert.That(engine.GetFunction("Doubled"), Is.Not.Null);
    }

    #endregion

    #region Helpers

    private static WitSqlEngine InMemory() =>
        new(WitDatabase.CreateInMemory(), ownsStore: true);

    private static DefinitionFunction Doubled() => new()
    {
        Name = "Doubled",
        ReturnType = WitDataType.Int32,
        IsDeterministic = true,
        Parameters = [new DefinitionRoutineParameter { Name = "N", Type = WitDataType.Int32 }],
        Body = WitSql.ParseExpression("N * 2")
    };

    private static DefinitionFunction FunctionNamed(string name) => new()
    {
        Name = name,
        ReturnType = WitDataType.Int32,
        IsDeterministic = true,
        Body = WitSql.ParseExpression("1")
    };

    private static DefinitionProcedure LogEverything() => new()
    {
        Name = "LogEverything",
        Parameters = [new DefinitionRoutineParameter { Name = "Note", Type = WitDataType.StringVariable, MaxLength = 100 }],
        Statements = WitSql.Parse(
            "INSERT INTO Log (Id, Note) VALUES (1, 'first'); INSERT INTO Log (Id, Note) VALUES (2, 'second')").ToList()
    };

    private static DefinitionProcedure ProcedureNamed(string name) => new()
    {
        Name = name,
        Statements = WitSql.Parse("SELECT 1").ToList()
    };

    #endregion
}
