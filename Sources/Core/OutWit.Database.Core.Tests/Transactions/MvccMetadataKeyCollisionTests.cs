using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Transactions;

namespace OutWit.Database.Core.Tests.Transactions;

/// <summary>
/// A key beginning with <c>$</c> written inside an MVCC transaction used to be committed and then
/// never become visible.
/// </summary>
/// <remarks>
/// <c>MvccKeyValueStore</c> keeps one metadata key of its own, <c>$mvcc:max_timestamp</c>, and
/// <c>CommitTransaction</c> skipped <b>every</b> key beginning with <c>$</c> when marking a
/// transaction's records committed. Anything else in that namespace - and the SQL engine's whole
/// schema catalog lives under <c>$schema:</c> - was written, left uncommitted, and hidden from every
/// reader forever. The transaction reported success.
///
/// Found while making the row counts commit atomically with the rows they describe: routing them
/// through the transaction made <c>SELECT COUNT(*)</c> return zero even after a clean shutdown. The
/// rest of the class already filters its own metadata by exact key (two other places do), so the
/// prefix test was the odd one out - and the <c>MvccRecord.TryDeserialize</c> check immediately after
/// it already rejects anything that is not a versioned record.
/// </remarks>
[TestFixture]
public sealed class MvccMetadataKeyCollisionTests
{
    #region Tests

    [Test]
    public void DollarPrefixedKeyWrittenInATransactionIsVisibleAfterCommitTest()
    {
        using var inner = new StoreInMemory();
        using var store = new MvccTransactionalStore(inner, ownsStore: false);

        using (var transaction = store.BeginTransaction())
        {
            transaction.Put(Key("$schema:_rowcount:T"), Value("20"));
            transaction.Commit();
        }

        Assert.That(store.Get(Key("$schema:_rowcount:T")), Is.EqualTo(Value("20")),
            "the transaction committed successfully, so the value must be readable - a key is not "
            + "the store's to swallow because of the character it starts with");
    }

    /// <summary>
    /// The control: an ordinary key written the same way. If this fails the fixture is broken rather
    /// than the prefix handling.
    /// </summary>
    [Test]
    public void ControlOrdinaryKeyWrittenInATransactionIsVisibleAfterCommitTest()
    {
        using var inner = new StoreInMemory();
        using var store = new MvccTransactionalStore(inner, ownsStore: false);

        using (var transaction = store.BeginTransaction())
        {
            transaction.Put(Key("schema:_rowcount:T"), Value("20"));
            transaction.Commit();
        }

        Assert.That(store.Get(Key("schema:_rowcount:T")), Is.EqualTo(Value("20")),
            "the same write without the leading '$' - if this fails, the test says nothing about "
            + "the prefix");
    }

    /// <summary>
    /// And the store's own metadata key must still be left alone, which is what the skip was for.
    /// </summary>
    [Test]
    public void ControlTheStoresOwnTimestampKeyStillSurvivesACommitTest()
    {
        using var inner = new StoreInMemory();

        using (var store = new MvccTransactionalStore(inner, ownsStore: false))
        {
            using (var transaction = store.BeginTransaction())
            {
                transaction.Put(Key("k"), Value("v"));
                transaction.Commit();
            }

            store.Flush();
        }

        // Reopening reads the persisted watermark; if the commit had mangled it, the committed row
        // would be invisible to the new store.
        using var reopened = new MvccTransactionalStore(inner, ownsStore: false);

        Assert.That(reopened.Get(Key("k")), Is.EqualTo(Value("v")),
            "the store's own max-timestamp metadata must survive a commit untouched - narrowing the "
            + "skip must not start treating it as a versioned record");
    }

    #endregion

    #region Tools

    private static byte[] Key(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    private static byte[] Value(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    #endregion
}
