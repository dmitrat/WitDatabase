
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Transactions;

namespace OutWit.Database.Core.Tests.AuditVerification;

/// <summary>
/// A key whose bytes begin with another key's bytes is a DIFFERENT key, and the MVCC layer has to
/// treat it as one.
/// </summary>
/// <remarks>
/// <para>
/// <b>The encoding is not prefix-free.</b> A version is stored at <c>[key][8-byte inverted
/// timestamp]</c> and every version of one key is found by scanning
/// <c>[key]00·8 … [key]FF·8</c>. That range does not contain only <c>key</c>'s versions: for
/// <c>Orders</c> it also contains every version of <c>OrdersAudit</c>, because <c>'A'</c> is 0x41 and
/// the range runs to 0xFF. Worse, <c>0x41</c> sorts BEFORE a typical inverted timestamp, so a foreign
/// record is usually the FIRST thing such a scan sees.
/// </para>
/// <para>
/// <b>What that cost, measured from the far end.</b> Studio's dump could not be executed back into a
/// database: the restored copy refused the next generated key in `OrdersAudit`, because the fixture
/// also holds a table called `Orders` and writing `Orders`' row-id counter had marked
/// `OrdersAudit`'s counter deleted. `KnownIssues.md` issue 11, and it took bisecting down from the
/// application to see it - fifteen cases built up from nothing all used names like `A`/`B` and
/// `Alpha`/`Beta`, which cannot reproduce it.
/// </para>
/// <para>
/// The fix is a length test at each single-key version scan: a versioned key that belongs to
/// <c>key</c> is exactly <c>key.Length + 8</c> bytes long, and inside the scanned range the length
/// alone settles it.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class MvccPrefixKeyTests
{
    #region Setup

    private StoreInMemory m_innerStore = null!;
    private TransactionTimestampManager m_timestamps = null!;
    private MvccKeyValueStore m_store = null!;

    [SetUp]
    public void SetUp()
    {
        m_innerStore = new StoreInMemory();
        m_timestamps = new TransactionTimestampManager();
        m_store = new MvccKeyValueStore(m_innerStore, m_timestamps, ownsStore: false);
    }

    [TearDown]
    public void TearDown()
    {
        m_store.Dispose();
        m_innerStore.Dispose();
    }

    #endregion

    #region Tests

    /// <summary>
    /// Writing the shorter key must not touch the longer one. This is the defect itself.
    /// </summary>
    [Test]
    public void WritingAShorterKeyLeavesTheLongerOneAloneTest()
    {
        m_store.Put(Key("$schema:_rowid:OrdersAudit"), Value("audit"));
        m_store.Put(Key("$schema:_rowid:Orders"), Value("orders"));

        // The second Put marks the previous version of its own key deleted - and used to find
        // OrdersAudit's record first, because 'A' sorts before the inverted timestamp.
        m_store.Put(Key("$schema:_rowid:Orders"), Value("orders again"));

        Assert.Multiple(() =>
        {
            Assert.That(Text(m_store.Get(Key("$schema:_rowid:OrdersAudit"))), Is.EqualTo("audit"),
                "the longer key's value was marked deleted by a write to the shorter one");
            Assert.That(Text(m_store.Get(Key("$schema:_rowid:Orders"))), Is.EqualTo("orders again"));
        });
    }

    /// <summary>
    /// And reading the shorter key must not answer with the longer key's value.
    /// </summary>
    /// <remarks>
    /// The same range, the other direction: with no version of its own to find, a read of
    /// <c>Orders</c> would deserialize the first record in the range - which is
    /// <c>OrdersAudit</c>'s - and return it. That is a wrong ANSWER rather than a lost one, so it is
    /// asserted separately.
    /// </remarks>
    [Test]
    public void ReadingAShorterKeyDoesNotAnswerWithTheLongerOnesValueTest()
    {
        m_store.Put(Key("$schema:_rowid:OrdersAudit"), Value("audit"));

        Assert.That(m_store.Get(Key("$schema:_rowid:Orders")), Is.Null,
            "there is no such key, and the value of a key that merely starts with it is not an answer");
    }

    /// <summary>
    /// The version count belongs to one key too.
    /// </summary>
    [Test]
    public void TheVersionCountIsOfOneKeyOnlyTest()
    {
        m_store.Put(Key("$schema:_rowid:Orders"), Value("orders"));

        m_store.Put(Key("$schema:_rowid:OrdersAudit"), Value("a"));
        m_store.Put(Key("$schema:_rowid:OrdersAudit"), Value("b"));
        m_store.Put(Key("$schema:_rowid:OrdersAudit"), Value("c"));

        Assert.That(m_store.GetVersionCount(Key("$schema:_rowid:Orders")), Is.EqualTo(1),
            "three of OrdersAudit's versions are inside the range this scan walks");
    }

    /// <summary>
    /// CONTROL. Two unrelated keys, which is what every case that failed to reproduce this used.
    /// </summary>
    [Test]
    public void TwoUnrelatedKeysAreUnaffectedTest()
    {
        m_store.Put(Key("$schema:_rowid:Alpha"), Value("alpha"));
        m_store.Put(Key("$schema:_rowid:Beta"), Value("beta"));
        m_store.Put(Key("$schema:_rowid:Beta"), Value("beta again"));

        Assert.Multiple(() =>
        {
            Assert.That(Text(m_store.Get(Key("$schema:_rowid:Alpha"))), Is.EqualTo("alpha"));
            Assert.That(Text(m_store.Get(Key("$schema:_rowid:Beta"))), Is.EqualTo("beta again"));
        });
    }

    /// <summary>
    /// CONTROL, the other half: a key that IS scanned by prefix on purpose must still work. Deleting
    /// the shorter key must not take the longer one with it either.
    /// </summary>
    [Test]
    public void DeletingTheShorterKeyLeavesTheLongerOneTest()
    {
        m_store.Put(Key("$schema:_rowid:OrdersAudit"), Value("audit"));
        m_store.Put(Key("$schema:_rowid:Orders"), Value("orders"));

        m_store.Delete(Key("$schema:_rowid:Orders"));

        Assert.Multiple(() =>
        {
            Assert.That(m_store.Get(Key("$schema:_rowid:Orders")), Is.Null);
            Assert.That(Text(m_store.Get(Key("$schema:_rowid:OrdersAudit"))), Is.EqualTo("audit"),
                "a delete of the shorter key must not reach the longer one");
        });
    }

    #endregion

    #region Tools

    private static byte[] Key(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    private static byte[] Value(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    private static string? Text(byte[]? value) => value == null ? null : System.Text.Encoding.UTF8.GetString(value);

    #endregion
}
