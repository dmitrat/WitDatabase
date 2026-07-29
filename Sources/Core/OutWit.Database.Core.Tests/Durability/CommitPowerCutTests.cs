using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Storage;

namespace OutWit.Database.Core.Tests.Durability;

/// <summary>
/// What a commit actually asks of the media, and what a power failure takes back.
/// </summary>
/// <remarks>
/// The out-of-process runner (<c>Tools/OutWit.Database.CrashRunner</c>) settled the same question
/// for a <b>process</b> kill: a committed transaction survives it. That is a weaker result than it
/// looks, because after a process dies the operating system is still running and writes its cache
/// back. This fixture asks the harder question - was the write ever made durable, or was it merely
/// handed to something that would have died with the machine.
///
/// <see cref="PowerCutStorage"/>'s own controls must be green for anything here to be readable.
/// </remarks>
[TestFixture]
[Category("Durability")]
public sealed class CommitPowerCutTests
{
    #region Constants

    private const int PAGE_SIZE = 4096;
    private const int ROWS = 20;

    #endregion

    #region Tests

    [Test]
    public void CommitAsksForDurabilityTest()
    {
        using var media = new StorageMemory(PAGE_SIZE);
        var storage = new PowerCutStorage(media, ownsMedia: false);

        using var database = Build(storage);

        var beforeCommit = storage.FlushCount;

        using (var transaction = database.BeginTransaction())
        {
            for (int i = 0; i < ROWS; i++)
                transaction.Put(Key(i), Value(i));

            transaction.Commit();
        }

        var afterCommit = storage.FlushCount;

        TestContext.Out.WriteLine(
            $"flushes: {beforeCommit} before the commit, {afterCommit} after; "
            + $"{storage.PagesAtRisk} pages still at risk");

        Assert.That(afterCommit, Is.GreaterThan(beforeCommit),
            "a commit that does not reach the media has not happened as far as a power failure is "
            + "concerned. The count is checked rather than the data because a count of zero here is "
            + "unambiguous - and PowerCutStorageControlTests proves the counter can see a flush");
    }

    [Test]
    public void CommittedDataSurvivesAPowerCutTest()
    {
        using var media = new StorageMemory(PAGE_SIZE);
        var storage = new PowerCutStorage(media, ownsMedia: false);

        var database = Build(storage);

        using (var transaction = database.BeginTransaction())
        {
            for (int i = 0; i < ROWS; i++)
                transaction.Put(Key(i), Value(i));

            transaction.Commit();
        }

        // The power goes here: no dispose, no shutdown, and whatever was never flushed is gone.
        var lost = storage.PowerCut();

        TestContext.Out.WriteLine($"the cut discarded {lost} unflushed pages");

        // Reopened straight onto the media, which is all a machine would have after the power came
        // back. Not through the decorator - its cache is exactly what does not exist any more.
        using var recovered = new WitDatabaseBuilder()
            .WithStorage(media)
            .WithBTree()
            .WithTransactions()
            .Build();

        var survivors = Enumerable.Range(0, ROWS).Count(i => recovered.Get(Key(i)) != null);

        TestContext.Out.WriteLine($"after the power cut: {survivors} of {ROWS} committed values readable");

        Assert.That(survivors, Is.EqualTo(ROWS),
            "every value was written inside a transaction that committed successfully. A commit that "
            + "returns and then loses the data to a power failure is the D in ACID");
    }

    #endregion

    #region Tools

    private static WitDatabase Build(PowerCutStorage storage) =>
        new WitDatabaseBuilder()
            .WithStorage(storage)
            .WithBTree()
            .WithTransactions()
            .Build();

    private static byte[] Key(int i) => System.Text.Encoding.UTF8.GetBytes($"k{i:D3}");

    private static byte[] Value(int i) => System.Text.Encoding.UTF8.GetBytes($"v{i:D3}");

    #endregion
}
