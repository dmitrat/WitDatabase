using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// A UNIQUE constraint is enforced by an INDEX, because without one it was enforced by a full table
/// scan on every insert.
/// </summary>
/// <remarks>
/// <para>
/// Measured in memory on 2026-08-09, so neither the disk nor the per-statement commit is in the
/// numbers. Four thousand inserts into a table with one UNIQUE column: <b>4.43 ms per row, and 240x
/// the cost for 16x the rows</b> - quadratic, which is the signature of a scan per insert. With an
/// index created by hand over the same column: 0.41 ms and 88x. So the uniqueness check's index seek
/// was never the problem - there was nothing to seek, because nothing created an index for a UNIQUE
/// constraint. Creating one takes the same measurement to 0.42 ms and 82x.
/// </para>
/// <para>
/// <b>The primary key already had one and that is why it was fast</b> - <c>_PK_&lt;table&gt;</c>,
/// created implicitly since long before this. Its inserts measured 0.40 ms per row, and adding a
/// SECOND index over the same column made them <b>slower</b> - 0.65 ms - which is the maintenance
/// cost of an index nothing reads. So a UNIQUE constraint is given one and a PRIMARY KEY is not given
/// another. (The first reading of this measurement said the key's check had a special path that did
/// not scan; it does not, it has an index, and the test below is what said so.)
/// </para>
/// <para>
/// Asserted through the CATALOGUE rather than through a stopwatch. A wall-clock bound is a claim
/// about the machine, which is exactly why the four timing tests in <c>Performance</c> have been red
/// on the development machine and excluded from CI since they were written.
/// </para>
/// </remarks>
[TestFixture]
public sealed class UniqueConstraintIndexTests
{
    #region Fields

    private WitSqlEngine m_engine = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_engine = new WitSqlEngine(WitDatabase.CreateInMemory(), ownsStore: true);
    }

    [TearDown]
    public void TearDown()
    {
        m_engine?.Dispose();
        m_engine = null!;
    }

    #endregion

    #region Tests

    [Test]
    public void AUniqueColumnIsGivenAnIndexTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, Email VARCHAR(50) UNIQUE)");

        var ours = Unique("T");

        Assert.Multiple(() =>
        {
            Assert.That(ours, Has.Count.EqualTo(1), "one constraint, one index");
            Assert.That(ours[0].Columns, Is.EqualTo(new[] { "Email" }));
            Assert.That(ours[0].IsUnique, Is.True);
        });
    }

    [Test]
    public void ATableLevelUniqueConstraintIsGivenOneTooTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, A INT, B INT, UNIQUE (A, B))");

        var ours = Unique("T");

        Assert.Multiple(() =>
        {
            Assert.That(ours, Has.Count.EqualTo(1));
            Assert.That(ours[0].Columns, Is.EquivalentTo(new[] { "A", "B" }));
        });
    }

    /// <summary>
    /// CONTROL, and it corrected the explanation: a primary key is not given a SECOND index, because
    /// it already has <c>_PK_&lt;table&gt;</c>. Adding another was measured to make inserts slower.
    /// </summary>
    [Test]
    public void APrimaryKeyIsNotGivenASecondIndexTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, Name VARCHAR(50))");

        Assert.Multiple(() =>
        {
            Assert.That(Indexes("T").Select(index => index.Name), Is.EqualTo(new[] { "_PK_T" }),
                "the implicit key index, and nothing else");
            Assert.That(Unique("T"), Is.Empty, "no UQ_ index of ours");
        });
    }

    /// <summary>
    /// CONTROL: a table with no UNIQUE constraint gets no index at all. Without it, "a unique column
    /// gets an index" would be satisfied by an engine that indexes everything.
    /// </summary>
    [Test]
    public void ATableWithNoUniqueConstraintGetsNoIndexTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT, Name VARCHAR(50))");

        Assert.That(Indexes("T"), Is.Empty);
    }

    /// <summary>
    /// The index enforces the constraint, so dropping the constraint has to take it with it - and the
    /// engine looked for it under a name nothing creates (<c>UQ_&lt;table&gt;_&lt;constraint&gt;</c>).
    /// That cost nothing while no index existed and became a wrong answer the moment one did: the
    /// duplicate the drop was meant to allow was still refused, by the index.
    /// </summary>
    [Test]
    public void DroppingTheConstraintDropsItsIndexTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, Email VARCHAR(50), "
                         + "CONSTRAINT UQ_Email UNIQUE (Email))");

        Assert.That(Unique("T"), Has.Count.EqualTo(1), "there is one to drop");

        m_engine.Execute("ALTER TABLE T DROP CONSTRAINT UQ_Email");

        Assert.Multiple(() =>
        {
            Assert.That(Unique("T"), Is.Empty);

            Assert.That(() =>
            {
                m_engine.Execute("INSERT INTO T (Id, Email) VALUES (1, 'a@b.c')");
                m_engine.Execute("INSERT INTO T (Id, Email) VALUES (2, 'a@b.c')");
            }, Throws.Nothing, "and the duplicate the drop exists to allow is allowed");
        });
    }

    /// <summary>
    /// The constraint still REFUSES a duplicate - the index is how it is enforced, not a replacement
    /// for enforcing it. This is the case that would go red if the index were created and the check
    /// stopped consulting it.
    /// </summary>
    [Test]
    public void TheConstraintStillRefusesADuplicateTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, Email VARCHAR(50) UNIQUE)");
        m_engine.Execute("INSERT INTO T (Id, Email) VALUES (1, 'a@b.c')");

        Assert.That(() => m_engine.Execute("INSERT INTO T (Id, Email) VALUES (2, 'a@b.c')"),
            Throws.TypeOf<InvalidOperationException>());
    }

    /// <summary>
    /// An index the user creates over the same column keeps its own name and is not duplicated - a
    /// restored dump carries one explicitly, and creating a second would fail the CREATE TABLE for a
    /// reason that has nothing to do with the SQL that was written.
    /// </summary>
    [Test]
    public void AnIndexTheUserCreatesIsNotDuplicatedTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, Email VARCHAR(50) UNIQUE)");
        m_engine.Execute("CREATE UNIQUE INDEX IX_Mine ON T (Email)");

        Assert.That(Indexes("T").Select(index => index.Name),
            Is.EquivalentTo(new[] { "_PK_T", "UQ_T_Email", "IX_Mine" }));
    }

    #endregion

    #region Tools

    private List<OutWit.Database.Definitions.DefinitionIndex> Indexes(string table) =>
        m_engine.GetTableIndexes(table).ToList();

    /// <summary>The indexes THIS change creates, told apart from the implicit key index by name.</summary>
    private List<OutWit.Database.Definitions.DefinitionIndex> Unique(string table) =>
        Indexes(table).Where(index => index.Name.StartsWith("UQ_", StringComparison.Ordinal)).ToList();

    #endregion
}
