using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The Connections window keeps naming the store after the database has been opened.
/// </summary>
/// <remarks>
/// <para>
/// Finding 41: a row read <i>B-Tree · 6.3 MB</i>, and after connecting to it the same row read
/// <i>6.3 MB</i>. The engine name was dropped at the moment it is most certainly known.
/// </para>
/// <para>
/// <b>Why it happened, and why the fix is not the profile.</b> The column is taken from a probe of
/// the path rather than from the saved connection, on purpose - a list showing what a database used
/// to be is worse than one showing nothing. But an open database holds an exclusive lock, so the
/// probe cannot read its header and answers <c>Locked</c>: a size, and no store type. The current
/// answer is not in the profile, it is in the session, and Studio has learned this once before - the
/// «База» tab asks the connection rather than the file for the same reason.
/// </para>
/// </remarks>
[TestFixture]
public class AnOpenDatabaseStillSaysWhatItIsTests
{
    #region Fields

    private StudioFixture m_studio = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUp()
    {
        m_studio = await StudioFixture.CreateAsync(connect: false);
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_studio.DisposeAsync();
    }

    #endregion

    #region Tests

    [Test]
    public async Task AConnectedRowStillNamesItsStoreTest()
    {
        var path = Path.Combine(m_studio.Root, "connected.witdb");

        StudioFixture.CreateDatabaseOnDisk(path);

        using var closed = new ConnectionsViewModel(m_studio.App);

        await m_studio.Profiles.SaveAsync(new ConnectionProfile { Name = "connected", Path = path });
        await closed.RefreshAsync();

        Assume.That(closed.Rows[0].Storage, Does.Contain("B-Tree"),
            "the control: a database nobody has open says what it is");

        await m_studio.App.OpenDatabaseAsync(new ConnectionInfo { FilePath = path });

        using var open = new ConnectionsViewModel(m_studio.App);

        await open.RefreshAsync();

        Assert.That(open.Rows[0].Storage, Does.Contain("B-Tree"),
            "and opening it is not a reason to stop saying so");
    }

    /// <summary>
    /// The same for the other store, and it is not the same code path: an LSM database is a folder.
    /// </summary>
    [Test]
    public async Task AConnectedLsmRowStillNamesItsStoreTest()
    {
        await using var lsm = await StudioFixture.CreateAsync(StudioStorage.Lsm, withSchema: false,
            connect: false);

        await lsm.Profiles.SaveAsync(new ConnectionProfile
        {
            Name = "events",
            Path = lsm.DatabasePath,
            StorageEngine = "lsm"
        });

        await lsm.App.OpenDatabaseAsync(new ConnectionInfo
        {
            FilePath = lsm.DatabasePath,
            StorageEngine = "lsm"
        });

        using var window = new ConnectionsViewModel(lsm.App);

        await window.RefreshAsync();

        Assert.That(window.Rows[0].Storage, Does.Contain("LSM"),
            "an open LSM database says LSM, the way the paged one says B-Tree");
    }

    /// <summary>
    /// The control, and the honest boundary: a database this Studio does NOT hold open cannot be
    /// read while something else holds it, and the column says the size and nothing more. Claiming a
    /// store type there would be Studio reporting what it did not read.
    /// </summary>
    [Test]
    public async Task ADatabaseHeldBySomebodyElseSaysOnlyItsSizeTest()
    {
        await using var other = await StudioFixture.CreateAsync(withSchema: false);

        await m_studio.Profiles.SaveAsync(new ConnectionProfile
        {
            Name = "somebody else's",
            Path = other.DatabasePath
        });

        using var window = new ConnectionsViewModel(m_studio.App);

        await window.RefreshAsync();

        Assert.That(window.Rows[0].Storage, Does.Not.Contain("B-Tree"),
            "nothing here has read that file, and the row does not pretend otherwise");
    }

    #endregion
}
