using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// A table's AUTOINCREMENT counter is lost across a reopen when another table's name is a PREFIX of
/// its own (<c>KnownIssues.md</c> issue 11).
/// </summary>
/// <remarks>
/// <para>
/// <b>Found from the far end.</b> Studio's dump could not be executed back into a database: after a
/// restore the first generated key was refused with <i>"the table's key counter is behind its rows"</i>.
/// Fifteen controlled cases built up from nothing failed to reproduce it; bisecting DOWN from the
/// fixture that did reproduce it - four tables, a view, a trigger, an index - left this, and it has
/// nothing to do with any of them. <c>Customers</c> came through the same transfer with its counter
/// intact. <c>OrdersAudit</c> did not, and the only thing that distinguishes them is that the database
/// also holds a table called <c>Orders</c>.
/// </para>
/// <para>
/// <b>What is ruled out.</b> The bare <c>StoreBTree</c> keeps two records whose keys are byte-prefixes
/// of one another perfectly well, in either write order - so it is not the store. The catalogue builds
/// <c>$schema:_rowid:{table}</c> by concatenation and reads, writes and deletes it by exact key, with
/// no range scan anywhere near it - so it is not obviously the catalogue either. It is somewhere
/// between the two, and it is NOT in the dump, the provider or Studio: this case is the engine on its
/// own.
/// </para>
/// <para>
/// PINS A DEFECT, NOT CORRECT BEHAVIOUR. When it is fixed the first case goes RED and should be
/// replaced by the plain statement: a counter advanced by explicit keys survives a reopen whatever the
/// other tables are called.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class RowIdCounterPrefixTests
{
    #region Setup

    private string m_root = null!;

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), "rowid-prefix-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(m_root);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(m_root, recursive: true);
        }
        catch
        {
            // A file the runner still holds is not this fixture's business.
        }
    }

    #endregion

    #region Tests

    /// <summary>
    /// PINS A DEFECT. The longer name loses its counter when the shorter one is written after it.
    /// </summary>
    [Test]
    public void ACounterIsLostWhenAnotherTableNameIsAPrefixOfItTest()
    {
        var path = Fill("OrdersAudit", "Orders");

        Assert.Multiple(() =>
        {
            Assert.That(NextGeneratedInsert(path, "OrdersAudit"),
                Does.Contain("key counter is behind its rows"),
                "PINS A DEFECT: OrdersAudit's counter did not survive the reopen, and the only "
                + "thing that makes it different from any other table here is that 'Orders' is a "
                + "prefix of its name");

            Assert.That(NextGeneratedInsert(path, "Orders"), Is.Empty,
                "and the shorter name's own counter is fine, which is what rules out 'the last "
                + "table wins' and 'explicit keys are never persisted'");
        });
    }

    /// <summary>
    /// CONTROL. The same sequence with two unrelated names: both counters survive.
    /// </summary>
    /// <remarks>
    /// Without this the case above measures "an explicit key's advance is never persisted", which is
    /// false and was the first four probes' answer.
    /// </remarks>
    [Test]
    public void TwoUnrelatedNamesBothKeepTheirCountersTest()
    {
        var path = Fill("Alpha", "Beta");

        Assert.Multiple(() =>
        {
            Assert.That(NextGeneratedInsert(path, "Alpha"), Is.Empty);
            Assert.That(NextGeneratedInsert(path, "Beta"), Is.Empty);
        });
    }

    /// <summary>
    /// CONTROL, and it is what makes the case above about the WRITE rather than about the pair: with
    /// the shorter name's counter written first and the longer one's last, both survive.
    /// </summary>
    [Test]
    public void TheLongerNameWrittenLastKeepsItsCounterTest()
    {
        var path = Fill("Orders", "OrdersAudit");

        Assert.That(NextGeneratedInsert(path, "OrdersAudit"), Is.Empty,
            "nothing wrote 'Orders' counter after it, so it is still there");
    }

    /// <summary>
    /// The sharpest statement of it, and it has nothing to do with restoring a dump: writing the
    /// SHORTER name's counter destroys the longer one's, at any time, on a healthy database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This starts from the arrangement the case above proves healthy, then does one ordinary thing -
    /// a generated insert into <c>Orders</c>, which writes <c>Orders</c>' counter and nothing else -
    /// and <c>OrdersAudit</c>'s counter is gone afterwards. So a dump is not needed, explicit keys are
    /// not needed, and any database holding a table whose name begins with another table's name is one
    /// insert away from this.
    /// </para>
    /// <para>
    /// It is the shape of a delete by PREFIX rather than by key, somewhere on the counter's write path.
    /// The bare <c>StoreBTree</c> is ruled out - it keeps two such records perfectly - and the
    /// catalogue's own four key builders are all exact concatenations with no scan near them.
    /// </para>
    /// </remarks>
    [Test]
    public void WritingTheShorterNamesCounterDestroysTheLongerOnesTest()
    {
        var path = Fill("Orders", "OrdersAudit");

        // Healthy at this point - the case above measures exactly that.
        Assert.That(NextGeneratedInsert(path, "OrdersAudit"), Is.Empty, "the fixture starts healthy");

        // One generated insert into the SHORTER name, on its own connection, and nothing else.
        Assert.That(NextGeneratedInsert(path, "Orders"), Is.Empty);

        Assert.That(NextGeneratedInsert(path, "OrdersAudit"),
            Does.Contain("key counter is behind its rows"),
            "PINS A DEFECT: writing Orders' counter took OrdersAudit's with it");
    }

    #endregion

    #region Tools

    /// <summary>
    /// Two tables, each created and then filled with rows carrying EXPLICIT keys - which is what a
    /// dump's INSERT statements do - and the database closed. Nothing is generated before the close:
    /// a generated insert here advances the counter by the ordinary path and hides the whole thing.
    /// </summary>
    private string Fill(string first, string second)
    {
        var path = Path.Combine(m_root, $"{first}-{second}.witdb");

        using var database = new WitDatabaseBuilder().WithFilePath(path).WithBTree().WithMvcc().Build();

        var engine = new WitSqlEngine(database);

        foreach (var table in new[] { first, second })
        {
            engine.Execute($"CREATE TABLE {table} (Id INTEGER PRIMARY KEY AUTOINCREMENT, V INTEGER)");

            for (var i = 1; i <= 3; i++)
                engine.Execute($"INSERT INTO {table} (Id, V) VALUES ({i}, {i})");
        }

        return path;
    }

    /// <summary>The message the next generated insert fails with, or empty when it is accepted.</summary>
    private static string NextGeneratedInsert(string path, string table)
    {
        using var database = new WitDatabaseBuilder().WithFilePath(path).WithBTree().WithMvcc().Build();

        var engine = new WitSqlEngine(database);

        try
        {
            engine.Execute($"INSERT INTO {table} (V) VALUES (99)");

            return string.Empty;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    #endregion
}
