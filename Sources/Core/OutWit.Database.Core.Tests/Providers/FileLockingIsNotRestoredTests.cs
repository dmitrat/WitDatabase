using OutWit.Database.Core.Builder;

namespace OutWit.Database.Core.Tests.Providers;

/// <summary>
/// <see cref="WitDatabase.Open(string)"/> takes the exclusive lock even when the database was created
/// without it.
/// </summary>
/// <remarks>
/// <para>
/// This is the one place phase 12 makes something that worked stop working, so it is measured rather
/// than reasoned about. Until 12.2.0 <c>Open</c> read <c>FileLocking</c> out of the header and called
/// <c>WithoutFileLocking()</c> when the flag was clear; a database created with <c>FileLocking=false</c>
/// therefore reopened without the guard, and on Linux a second engine could open it alongside the first.
/// </para>
/// <para>
/// The decision that governs it is that <b>safety settings are not restored from the file</b>: a file
/// may not make a database less exclusive for a caller who said nothing about it. So the flag is still
/// recorded in the header - it says what the database was created with - and it no longer decides what
/// an open does. A caller who wants the guard off names it again.
/// </para>
/// </remarks>
[TestFixture]
public class FileLockingIsNotRestoredTests
{
    #region Fields

    private string m_directory = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witdb_locking_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_directory))
                Directory.Delete(m_directory, recursive: true);
        }
        catch
        {
            // Cleanup must not fail the run.
        }
    }

    #endregion

    #region Probes

    [Test]
    public void OpenTakesTheLockOnADatabaseCreatedWithoutItTest()
    {
        var path = Path.Combine(m_directory, "unlocked.witdb");

        using (var created = new WitDatabaseBuilder()
                   .WithFilePath(path)
                   .WithBTree()
                   .WithMvcc()
                   .WithoutFileLocking()
                   .Build())
        {
            created.Put("k"u8.ToArray(), "v"u8.ToArray());
        }

        using var reopened = WitDatabase.Open(path);

        Assert.Multiple(() =>
        {
            Assert.That(reopened.Get("k"u8.ToArray()), Is.EqualTo("v"u8.ToArray()),
                "the data must come back - this is a change of guard, not of format");

            // The observable consequence, and the reason this is a breaking change rather than a
            // tightening nobody can see: a second engine over the same database is refused.
            Assert.That(() => WitDatabase.Open(path), Throws.Exception,
                "Open no longer restores FileLocking=false, so the exclusive guard is taken and a " +
                "second engine is refused. Before 12.2.0 this succeeded.");
        });
    }

    #endregion
}
