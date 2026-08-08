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
/// <b>The cause was two layers down, in the MVCC key encoding.</b> A version lives at
/// <c>[key][8-byte inverted timestamp]</c> and one key's versions are scanned as
/// <c>[key]00·8 … [key]FF·8</c> - a range that also contains every version of any LONGER key
/// beginning with it, and 0x41 (<c>'A'</c> of "Audit") sorts before a typical inverted timestamp, so
/// the foreign record came first. Writing <c>Orders</c>' counter marked <c>OrdersAudit</c>'s record
/// deleted. Fixed in <c>MvccKeyValueStore</c> with a length test at each single-key version scan; see
/// <c>MvccPrefixKeyTests</c>, where a read of a key that does not exist also used to answer with
/// another key's value.
/// </para>
/// <para>
/// <b>These cases were written as pins and inverted when the fix landed</b>, which is what makes them
/// evidence rather than description: the two that named the defect went red on the first run against
/// the fixed store and now assert the ordinary thing.
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
    /// A counter survives the reopen whatever the other tables in the database are called.
    /// </summary>
    /// <remarks>
    /// Red before the fix: <c>OrdersAudit</c>'s counter was gone, and the only thing that made it
    /// different from any other table here is that <c>Orders</c> is a prefix of its name.
    /// </remarks>
    [Test]
    public void ACounterSurvivesEvenWhenAnotherTableNameIsAPrefixOfItTest()
    {
        var path = Fill("OrdersAudit", "Orders");

        Assert.Multiple(() =>
        {
            Assert.That(NextGeneratedInsert(path, "OrdersAudit"), Is.Empty,
                "the longer name's counter has to survive a write to the shorter one");

            Assert.That(NextGeneratedInsert(path, "Orders"), Is.Empty,
                "and the shorter name's own counter is fine either way, which is what ruled out "
                + "'the last table wins' and 'explicit keys are never persisted'");
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
    /// Writing the SHORTER name's counter leaves the longer one's alone - which is the defect in its
    /// sharpest form, and it had nothing to do with restoring a dump.
    /// </summary>
    /// <remarks>
    /// This starts from an arrangement the case above proves healthy and then does one ordinary thing:
    /// a generated insert into <c>Orders</c>, which writes <c>Orders</c>' counter and nothing else.
    /// Before the fix <c>OrdersAudit</c>'s counter was gone afterwards - so no dump and no explicit
    /// keys were needed, and every database holding a table whose name begins with another table's
    /// name was one insert away from it.
    /// </remarks>
    [Test]
    public void WritingTheShorterNamesCounterLeavesTheLongerOneAloneTest()
    {
        var path = Fill("Orders", "OrdersAudit");

        // Healthy at this point - the case above measures exactly that.
        Assert.That(NextGeneratedInsert(path, "OrdersAudit"), Is.Empty, "the fixture starts healthy");

        // One generated insert into the SHORTER name, on its own connection, and nothing else.
        Assert.That(NextGeneratedInsert(path, "Orders"), Is.Empty);

        Assert.That(NextGeneratedInsert(path, "OrdersAudit"), Is.Empty,
            "writing Orders' counter must not take OrdersAudit's with it");
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
