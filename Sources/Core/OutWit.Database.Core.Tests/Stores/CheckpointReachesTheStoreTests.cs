using OutWit.Database.Core.Builder;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.Core.Tests.Stores;

/// <summary>
/// A checkpoint asked of a database reaches the store underneath it.
/// </summary>
/// <remarks>
/// <para>
/// <b>It did not, and the hop it was lost at is the default one.</b> <c>IKeyValueStore</c> separates
/// <c>Flush</c> - make durable - from <c>Checkpoint</c> - force the accumulated state out - and says
/// in so many words that conflating them "is what made the LSM store behave nothing like an LSM
/// tree". <c>BTreeConcurrentStore</c> forwards <c>Checkpoint</c> and carries that reasoning in a
/// comment. The transactional wrappers above it did not: <c>TransactionalStore</c> called
/// <c>Flush</c> on the inner store, and <c>MvccTransactionalStore</c>, <c>MvccKeyValueStore</c> and
/// <c>VersionedKeyValueStore</c> had no override at all, so they took the interface default - which
/// is <c>Flush</c>.
/// </para>
/// <para>
/// Measured 2026-08-07 before the fix: 200 puts into an LSM database, then <c>Checkpoint()</c> on the
/// store the database hands out, left the directory holding <c>provider.meta</c> and <c>wal.log</c>
/// and <b>no SSTable</b>, in every transaction model. The data was durable and the LSM tree had never
/// been given a chance to become one.
/// </para>
/// <para>
/// <b>Why this fixture is about the whole chain rather than one class.</b> The wrappers are stacked -
/// MVCC over versioned over concurrent over the store - and a checkpoint has to survive every hop.
/// One of them was already right, which is what makes the others worth pinning: the same mistake was
/// found and fixed once, in one place, and the shape was not looked for anywhere else.
/// </para>
/// </remarks>
[TestFixture]
public sealed class CheckpointReachesTheStoreTests
{
    #region Setup

    private string m_root = null!;

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"checkpoint_reach_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_root, recursive: true); } catch { /* best effort */ }
    }

    #endregion

    #region Tests

    /// <summary>
    /// Every transaction model, because each one is a different first hop and the default is the one
    /// every ADO.NET and EF Core consumer gets.
    /// </summary>
    [TestCase(TransactionModel.Mvcc)]
    [TestCase(TransactionModel.Lock)]
    [TestCase(TransactionModel.None)]
    public void ACheckpointForcesTheMemTableOutTest(TransactionModel model)
    {
        var directory = Directory.CreateDirectory(Path.Combine(m_root, model.ToString())).FullName;

        using var database = Build(model, directory);

        Write(database, rows: 200);

        // CONTROL: nothing has asked for a checkpoint, so there must be no SSTable yet. Without it,
        // "an SSTable exists" could be the size threshold firing rather than the call under test.
        Assert.That(SstableCount(directory), Is.Zero,
            "CONTROL: an SSTable appeared before anything asked for a checkpoint, so this case cannot "
            + "attribute the one below to the call");

        database.Store.Checkpoint();

        Assert.That(SstableCount(directory), Is.EqualTo(1),
            "the checkpoint did not reach the LSM store: the memtable is still only in the write-ahead "
            + "log, so the store was made durable rather than reorganised. A wrapper in the chain is "
            + "answering Checkpoint with Flush.");
    }

    /// <summary>
    /// And the rows survive it, because a checkpoint that loses data would satisfy the case above.
    /// </summary>
    [Test]
    public void ACheckpointKeepsTheRowsTest()
    {
        var directory = Directory.CreateDirectory(Path.Combine(m_root, "rows")).FullName;

        using (var database = Build(TransactionModel.Mvcc, directory))
        {
            Write(database, rows: 200);
            database.Store.Checkpoint();
        }

        using var reopened = Build(TransactionModel.Mvcc, directory);

        var scanned = reopened.Store.Scan(null, null).Count();

        Assert.That(scanned, Is.EqualTo(200),
            $"{scanned} of 200 rows came back after a checkpoint and a reopen");
    }

    #endregion

    #region Tools

    public enum TransactionModel
    {
        Mvcc,
        Lock,
        None
    }

    private static WitDatabase Build(TransactionModel model, string directory)
    {
        var builder = new WitDatabaseBuilder().WithLsmTree(directory);

        return model switch
        {
            TransactionModel.Mvcc => builder.WithMvcc().Build(),
            TransactionModel.Lock => builder.WithTransactions().Build(),
            _ => builder.Build()
        };
    }

    private static void Write(WitDatabase database, int rows)
    {
        for (var i = 0; i < rows; i++)
            database.Store.Put(Bytes($"k{i:D4}"), Bytes($"value {i}"));
    }

    private static int SstableCount(string directory) =>
        Directory.GetFiles(directory, "*.sst").Length;

    private static byte[] Bytes(string text) => TextEncoding.UTF8.GetBytes(text);

    #endregion
}
