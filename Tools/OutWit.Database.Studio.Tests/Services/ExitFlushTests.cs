using System.Buffers.Binary;
using System.Text.RegularExpressions;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// Leaving Studio writes the databases down (issue 10).
///
/// <para>
/// <b>What went wrong, because it decides the shape of these cases.</b> Only <c>Flush</c> writes a
/// database file's header, and a page reaches the disk on its own whenever the page cache evicts it.
/// So a process that ends without disposing its connections leaves a file whose header is OLDER than
/// its own pages: everything since the last flush is missing, and once the cache has evicted anything
/// the file cannot be opened at all. Two databases were lost that way and it was blamed on the table
/// rebuild for a fortnight - the rebuild is only the workload that churns the catalogue hardest.
/// </para>
/// <para>
/// <b>The cause was an ORDERING, which is why there are two cases here.</b>
/// <c>MainWindow.OnClosing</c> is <c>async void</c>: its first <c>await</c> hands control back to
/// Avalonia, which closes the window and ends the process, so nothing after that await ever runs. A
/// close written after it looks completely correct and does nothing at all - measured, twice. The
/// second case is therefore about the source, not the behaviour: it is the only way to catch the
/// defect that actually shipped.
/// </para>
/// </summary>
[TestFixture]
public class ExitFlushTests
{
    #region Tests

    /// <summary>
    /// A closed database counts its own pages.
    ///
    /// <para>
    /// The header's <c>TotalPageCount</c> against the size of the file is the cheapest statement of
    /// "this was flushed": the file is extended the moment a page is allocated, and the count that
    /// says how many are live is only written by a flush. Before the fix this read 2 against 8.
    /// </para>
    /// </summary>
    [Test]
    public async Task ClosingTheDatabasesWritesTheHeaderTest()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        var session = fixture.App.Connections.Active!;

        // Enough churn that the file grows past whatever the last flush recorded. The catalogue is one
        // big value in an overflow chain, so every statement here rewrites it and frees the old one.
        for (var i = 1; i <= 8; i++)
        {
            await session.ExecuteNonQueryAsync(
                $"CREATE TABLE Churn{i} (Id INTEGER PRIMARY KEY, Payload VARCHAR(200))");
            await session.ExecuteNonQueryAsync($"DROP TABLE Churn{i}");
        }

        // The file cannot be read while the engine holds it - since 12.2.0 the lock is exclusive - so
        // the growth is measured on the closed file rather than before. That is not a weaker control:
        // a file this size cannot be the two pages an untouched database has.
        fixture.App.CloseDatabases();

        var (counted, pages) = HeaderAndFile(fixture.DatabasePath);

        Assert.Multiple(() =>
        {
            // CONTROL: with too little work the file never outgrows the last flush, and the case would
            // pass without the closing having written anything. It has to have something to write.
            Assert.That(pages, Is.GreaterThan(3),
                "CONTROL: the file never grew, so this case cannot tell a flush from a no-op");

            Assert.That(counted, Is.EqualTo(pages),
                $"the header counts {counted} pages and the file holds {pages}: it was not flushed, so "
                + "everything since the last flush is lost and the file is one eviction away from "
                + "being unreadable");
        });
    }

    /// <summary>
    /// The close runs BEFORE the first await of <c>OnClosing</c>.
    ///
    /// <para>
    /// A source rule, and it exists because the behaviour cannot be tested from here: the defect is
    /// that Avalonia ends the process at the await, and no test host reproduces that. A call moved
    /// three lines down is invisible to every other case in this suite and silently stops closing
    /// anything - which is exactly what shipped.
    /// </para>
    /// </summary>
    [Test]
    public void TheDatabasesAreClosedBeforeTheFirstAwaitTest()
    {
        // Normalised, because the file is CRLF and every pattern below is written with \n. The first
        // run of this rule found no confirmation block for exactly that reason and then measured the
        // wrong await - the instrument was wrong before the subject was.
        var source = File.ReadAllText(Path.Combine(StudioFolder(), "Views", "MainWindow.axaml.cs"))
            .ReplaceLineEndings("\n");

        var closing = Regex.Match(source,
            @"private async void OnClosing\(.*?\n    \}", RegexOptions.Singleline);

        Assert.That(closing.Success, Is.True, "OnClosing was not found in MainWindow.axaml.cs");

        var body = closing.Value;

        var close = body.IndexOf("CloseDatabases()", StringComparison.Ordinal);

        // The LAST await, not the first. The first ones belong to the confirmation pass, which returns
        // long before the close is reached; the one that matters is the last thing the handler does,
        // because that is where the process goes away. Written the other way round first, and it
        // measured the confirmation's await and went red against correct code.
        var awaited = body.LastIndexOf("await ", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(close, Is.GreaterThan(0),
                "OnClosing does not close the databases at all - nothing will be flushed on the way out");

            // CONTROL: an OnClosing with no await left in it would pass this vacuously, and the rule
            // exists precisely because there IS one.
            Assert.That(awaited, Is.GreaterThan(0),
                "CONTROL: no await was found after the confirmation, so this rule is guarding nothing");

            Assert.That(close, Is.LessThan(awaited),
                "the databases are closed AFTER an await in an 'async void' handler: Avalonia ends the "
                + "process at that await and the close never runs. It has to be first, and synchronous");
        });
    }

    #endregion

    #region Tools

    /// <summary>
    /// <c>TotalPageCount</c> out of the file header (<c>DatabaseHeader</c>: page size at 18, the count
    /// at 20), read raw. Nothing here goes through the engine - the point is to read what was written.
    /// </summary>
    private static (uint Counted, long Pages) HeaderAndFile(string path)
    {
        var raw = ReadWhileOpen(path);

        var pageSize = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(18));
        var counted = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(20));

        return (counted, raw.Length / pageSize);
    }

    /// <summary>
    /// The bytes, even while the engine holds the file: this reads it from underneath a live
    /// connection, which is the whole point - what is ON DISK is the question.
    /// </summary>
    private static byte[] ReadWhileOpen(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var memory = new MemoryStream();

        stream.CopyTo(memory);

        return memory.ToArray();
    }

    private static string StudioFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Tools", "OutWit.Database.Studio");

            if (Directory.Exists(Path.Combine(candidate, "Views")))
                return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("the Studio project was not found from " + AppContext.BaseDirectory);
    }

    #endregion
}
