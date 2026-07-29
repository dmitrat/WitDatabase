using OutWit.Database.Core.Wal;
using LsmWriteAheadLog = OutWit.Database.Core.LSM.WriteAheadLog;

namespace OutWit.Database.Core.Tests.Durability;

/// <summary>
/// The LSM engine has a write-ahead log of its own, and it replayed damage exactly as silently as the
/// transactional one did.
/// </summary>
/// <remarks>
/// The audit's finding named <c>TransactionalStore.cs:403</c> and only that path. Fixing it alone
/// would have left the identical shape in <c>Core/LSM/WriteAheadLog.cs</c> - stop at the first record
/// that fails verification, return the count as though it were the whole log, and let the caller
/// truncate. This project has already paid for that kind of half-fix once: the 2.0.0 <c>DropTable</c>
/// change fixed the schema half of a defect and left the storage half in place, and a comment saying
/// it was handled survived to be believed.
///
/// A torn tail is deliberately <b>not</b> an error here. It is what an ordinary crash leaves, and the
/// control below pins that it still replays cleanly - without it this fixture could not tell a fix
/// from a refusal to open any crashed database.
/// </remarks>
[TestFixture]
[Category("Durability")]
public sealed class LsmWalPartialReplayTests
{
    #region Fields

    private const int ENTRIES = 8;

    private string m_directory = null!;
    private string m_walPath = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witdb-lsmwal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
        m_walPath = Path.Combine(m_directory, "lsm.wal");
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_directory, recursive: true); } catch { /* best effort */ }
    }

    #endregion

    #region Tests

    [Test]
    public void CorruptRecordDoesNotSilentlyDiscardLaterEntriesTest()
    {
        WriteEntries(ENTRIES);

        // Damage a record in the middle, the way a bad sector would.
        var bytes = File.ReadAllBytes(m_walPath);
        var midpoint = bytes.Length / 2;
        for (int i = midpoint; i < Math.Min(midpoint + 16, bytes.Length); i++)
            bytes[i] ^= 0xFF;
        File.WriteAllBytes(m_walPath, bytes);

        var replayed = new List<string>();
        Exception? reported = null;

        try
        {
            using var wal = new LsmWriteAheadLog(m_walPath);
            wal.Replay(
                onPut: (key, _) => replayed.Add(System.Text.Encoding.UTF8.GetString(key)),
                onDelete: _ => { });
        }
        catch (Exception e)
        {
            reported = e;
        }

        TestContext.Out.WriteLine(
            $"after corrupting one mid-log record: {replayed.Count}/{ENTRIES} entries replayed, "
            + $"error reported: {reported?.GetType().Name ?? "none"}");

        Assert.That(replayed.Count == ENTRIES || reported != null, Is.True,
            "either every entry is replayed, or the loss is reported. Dropping the entries behind a "
            + "damaged record and returning a count as though the log ended there is how a database "
            + "loses data quietly - and the caller truncates the log immediately afterwards");
    }

    /// <summary>
    /// The control: a crash <i>during</i> an append leaves a half-written trailing record, and
    /// everything before it must still replay without complaint.
    /// </summary>
    /// <remarks>
    /// The distinction this control turns on is what makes the fix safe rather than merely loud. A
    /// torn tail is a record the log never acknowledged - the header counter is written on sync, so
    /// it does not include the record that was in flight when the power went. Truncating a log
    /// <i>after</i> a successful sync is a different thing entirely: those records were
    /// acknowledged, and losing them quietly is the defect.
    ///
    /// The first version of this control got that wrong. It synced and then truncated, which is
    /// damage rather than a torn tail, and it would have pinned "say nothing about acknowledged
    /// records that vanished" as correct behaviour - the exact thing being fixed.
    /// </remarks>
    [Test]
    public void ControlTornTailReplaysWhatCameBeforeItTest()
    {
        WriteEntries(ENTRIES);

        // The half-written record a crash leaves: bytes on the end of the file that no sync ever
        // acknowledged, so the header still counts only the eight before it.
        //
        // Written as raw bytes on purpose. Appending through the WAL and disposing it would not
        // model a crash - `Dispose` calls `UpdateHeader`, so the header would count the torn record
        // too and this control would be asserting on something a power failure never produces.
        using (var file = new FileStream(m_walPath, FileMode.Append, FileAccess.Write))
            file.Write(new byte[] { 0x11, 0x22, 0x33, 0x44, 0x01, 0x05, 0x00, 0x00 });

        var replayed = new List<string>();

        using var wal2 = new LsmWriteAheadLog(m_walPath);

        Assert.DoesNotThrow(
            () => wal2.Replay(
                onPut: (key, _) => replayed.Add(System.Text.Encoding.UTF8.GetString(key)),
                onDelete: _ => { }),
            "a half-written trailing record is what an ordinary crash leaves, and recovering from it "
            + "is the WAL's job - if this throws, the fix has turned every crashed database into an "
            + "unopenable one");

        TestContext.Out.WriteLine(
            $"torn tail: {replayed.Count} entries replayed, {ENTRIES} had been synced");

        Assert.That(replayed, Has.Count.GreaterThanOrEqualTo(ENTRIES),
            "every record the log acknowledged must still come back - only the torn one is lost");
    }

    #endregion

    #region Tools

    private void WriteEntries(int count)
    {
        using var wal = new LsmWriteAheadLog(m_walPath, createNew: true);

        for (int i = 0; i < count; i++)
        {
            wal.AppendPut(
                System.Text.Encoding.UTF8.GetBytes($"k{i:D3}"),
                System.Text.Encoding.UTF8.GetBytes($"value-{i:D3}-padding-to-make-the-record-wider"));
        }

        wal.Sync();
    }

    #endregion
}
