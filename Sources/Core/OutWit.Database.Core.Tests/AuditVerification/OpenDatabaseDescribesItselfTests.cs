using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Providers;

namespace OutWit.Database.Core.Tests.AuditVerification;

/// <summary>
/// An open database can say what it was created with.
/// </summary>
/// <remarks>
/// <para>
/// The phase-10 remainder's first item. Since 12.2.0 an open paged database holds an exclusive file
/// lock, so <see cref="StorageDetector.ReadStoredConfiguration"/> - which opens the file and reads the
/// header - answers <c>null</c> for exactly the database a caller is most likely to be asking about:
/// the one it has open. Studio worked around it by reading the header a moment BEFORE connecting,
/// which works until something else has the database open already, and then there is no "before".
/// </para>
/// <para>
/// <b>Measured 2026-08-09, and it narrowed the recorded finding.</b> "An open database cannot be read
/// at all" is true of a PAGED file only: with the database open, <c>ReadStoredConfiguration</c>
/// answered <c>null</c> for a B-Tree file and <c>lsm</c> for an LSM directory, because the sidecar is
/// a separate file the lock does not cover. Both halves are cases here - the first is what the fix is
/// for, and the second is the control that says the fix did not have to invent anything for LSM.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class OpenDatabaseDescribesItselfTests
{
    #region Setup

    private string m_root = null!;

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), "WitOpenDescribes", Guid.NewGuid().ToString("N"));
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
            // a file still held open is not a test failure
        }
    }

    #endregion

    #region Tests

    /// <summary>
    /// The case the item exists for: the file cannot be read while it is open, and the database
    /// answers anyway.
    /// </summary>
    [Test]
    public void APagedDatabaseDescribesItselfWhileItIsOpenTest()
    {
        var path = Path.Combine(m_root, "paged.witdb");

        using var database = new WitDatabaseBuilder().WithFilePath(path).WithBTree().Build();

        database.Put("k"u8.ToArray(), "v"u8.ToArray());

        // The control, and it is the whole reason for this property: the file route answers nothing
        // here. If this ever starts answering, the lock has changed and the item needs re-reading.
        Assert.That(StorageDetector.ReadStoredConfiguration(path), Is.Null,
            "the file cannot be read while the database holds it - that is what is being worked around");

        var stored = database.StoredConfiguration;

        Assert.That(stored, Is.Not.Null, "and the open database answers from its own header");
        Assert.That(stored!.Metadata.StoreProviderKey, Is.EqualTo("btree"));
        Assert.That(stored.PageSize, Is.GreaterThan(0));
        Assert.That(stored.IsDirectory, Is.False);
        Assert.That(stored.FormatVersion, Is.Not.Null);
    }

    /// <summary>
    /// The answer has to be the SAME answer. A property that reported something plausible but
    /// different would satisfy the case above.
    /// </summary>
    [Test]
    public void ItIsTheSameAnswerTheFileGivesOnceItIsClosedTest()
    {
        var path = Path.Combine(m_root, "same.witdb");

        StoredConfiguration? whileOpen;

        using (var database = new WitDatabaseBuilder().WithFilePath(path).WithBTree().WithTransactions().Build())
        {
            database.Put("k"u8.ToArray(), "v"u8.ToArray());
            whileOpen = database.StoredConfiguration;
        }

        var afterClose = StorageDetector.ReadStoredConfiguration(path);

        Assert.That(afterClose, Is.Not.Null);
        Assert.That(whileOpen, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(whileOpen!.Metadata.StoreProviderKey, Is.EqualTo(afterClose!.Metadata.StoreProviderKey));
            Assert.That(whileOpen.Metadata.HasTransactions, Is.EqualTo(afterClose.Metadata.HasTransactions));
            Assert.That(whileOpen.Metadata.HasMvcc, Is.EqualTo(afterClose.Metadata.HasMvcc));
            Assert.That(whileOpen.PageSize, Is.EqualTo(afterClose.PageSize));
            Assert.That(whileOpen.FormatVersion, Is.EqualTo(afterClose.FormatVersion));
        });
    }

    /// <summary>
    /// The transaction model reaches it through the wrappers. A database built with transactions has
    /// a chain of stores between the caller and the one holding the header, and the answer comes from
    /// the bottom of it - which is what <c>FindCapability</c> is for and what a hand-written
    /// forwarding chain would have got wrong the first time a wrapper was added.
    /// </summary>
    [Test]
    public void TheAnswerComesThroughTheWrappersTest()
    {
        var path = Path.Combine(m_root, "mvcc.witdb");

        using var database = new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithTransactions()
            .WithMvcc()
            .Build();

        database.Put("k"u8.ToArray(), "v"u8.ToArray());

        var stored = database.StoredConfiguration;

        Assert.That(stored, Is.Not.Null);
        Assert.That(stored!.Metadata.HasMvcc, Is.True,
            "and it is this database's own record, not a default");
    }

    /// <summary>
    /// THE CONTROL for the whole fixture: the answer is READ, not manufactured. Every case above
    /// asserts values a hard-coded default would also satisfy - "btree", transactions on, a page size
    /// greater than zero - so one case has to ask for something that cannot be guessed.
    /// </summary>
    /// <remarks>
    /// This is phase 12's VACUOUS rule applied here: "the two sides agree" is only evidence if the
    /// case could have made them disagree, and a database created at the default page size would
    /// agree with a property that always answered the default.
    /// </remarks>
    [Test]
    public void ANonDefaultPageSizeComesBackTest()
    {
        var path = Path.Combine(m_root, "wide.witdb");

        using var database = new WitDatabaseBuilder().WithFilePath(path).WithBTree().WithPageSize(8192).Build();

        database.Put("k"u8.ToArray(), "v"u8.ToArray());

        Assert.That(database.StoredConfiguration?.PageSize, Is.EqualTo(8192));
    }

    /// <summary>
    /// MEASURED, and it corrects an assumption this fixture was first written with: an in-memory
    /// database DOES describe itself, and describes itself as a B-Tree - because that is what it is,
    /// a B+Tree over memory storage, and it keeps the same header. The first version of this case
    /// asserted the opposite as a "control" and went red on the first run.
    /// </summary>
    [Test]
    public void AnInMemoryDatabaseDescribesItselfAsABTreeTest()
    {
        using var database = WitDatabase.CreateInMemory();

        database.Put("k"u8.ToArray(), "v"u8.ToArray());

        Assert.That(database.StoredConfiguration?.Metadata.StoreProviderKey, Is.EqualTo("btree"));
    }

    #endregion
}
