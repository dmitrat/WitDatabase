using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// Verification by reading (WS-61), and mostly the question of what a green line is worth.
/// </summary>
[TestFixture]
public class ReadCheckTests
{
    #region Fields

    private StudioFixture m_fixture = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUp()
    {
        m_fixture = await StudioFixture.CreateAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_fixture.DisposeAsync();
    }

    #endregion

    #region Tests

    /// <summary>
    /// Every table is read, and the rows the check counted are the rows the fixture put there.
    /// </summary>
    /// <remarks>
    /// Asserted against the KNOWN contents rather than against "more than zero": a scan that returned
    /// the first row of each table and stopped would pass the second and fail this.
    /// </remarks>
    [Test]
    public async Task EveryTableIsReadAndTheRowsAreTheRowsAsync()
    {
        var report = await ReadChecker.RunAsync(m_fixture.Database);

        var customers = report.Items.Single(item =>
            item is { Subject: ReadCheckSubject.Table, Name: "Customers" });

        Assert.Multiple(() =>
        {
            Assert.That(report.Tables, Is.EqualTo(4), "Customers, Orders, OrdersAudit and Logs");
            Assert.That(report.Failed, Is.Zero);
            Assert.That(report.WasCancelled, Is.False);

            Assert.That(customers.Outcome, Is.EqualTo(ReadCheckOutcome.Ok));
            Assert.That(customers.RowsRead, Is.EqualTo(StudioFixture.CUSTOMER_COUNT));

            // Both numbers are reported and they agree here, which is what makes a disagreement
            // meaningful when it happens: on this engine COUNT(*) is a counter kept BESIDE the rows,
            // and after a crash the two are known to part company.
            Assert.That(customers.CounterSays, Is.EqualTo(StudioFixture.CUSTOMER_COUNT));
            Assert.That(report.Disagreements, Is.Empty);
        });
    }

    /// <summary>
    /// The control, and it is built into the subject: an index the planner did not use is NOT reported
    /// as read.
    ///
    /// <para>
    /// The fixture's <c>Orders</c> has three rows and this planner refuses to consider an index below
    /// ten - so the query written to walk the index is answered by a table scan, and calling that a
    /// checked index would be a green tick for a structure nobody touched. Forty rows later the same
    /// index, the same query and the same check say ok.
    /// </para>
    /// <para>
    /// Which also means the pair measures the check in both directions: without the plan being read,
    /// the first arm would come back ok and nothing here would ever fail.
    /// </para>
    /// </summary>
    [Test]
    public async Task AnIndexThePlannerDidNotUseIsNotReportedAsReadAsync()
    {
        var before = await ReadChecker.RunAsync(m_fixture.Database);

        var small = before.Items.Single(item =>
            item is { Subject: ReadCheckSubject.Index, Name: "IX_Orders_CustomerId" });

        for (var i = 0; i < 40; i++)
        {
            await m_fixture.Database.ExecuteQueryAsync(
                "INSERT INTO Orders (CustomerId, Total, Status) VALUES (1, 10.00, 'new')");
        }

        var after = await ReadChecker.RunAsync(m_fixture.Database);

        var grown = after.Items.Single(item =>
            item is { Subject: ReadCheckSubject.Index, Name: "IX_Orders_CustomerId" });

        Assert.Multiple(() =>
        {
            Assert.That(small.Outcome, Is.EqualTo(ReadCheckOutcome.Inconclusive),
                "three rows is below the planner's threshold, so the index was not touched");
            Assert.That(small.NoteKey, Is.EqualTo("ReadCheck.Note.PlannerDidNotUseIt"));

            Assert.That(grown.Outcome, Is.EqualTo(ReadCheckOutcome.Ok),
                "and above it the same query is answered with the index");
            Assert.That(grown.NoteKey, Is.EqualTo("ReadCheck.Note.SeekNotTraversal"),
                "and the line says what was done, because a seek is not a traversal");
        });
    }

    /// <summary>
    /// The catalogue is its own line, because "no tables" and "the catalogue will not answer" are
    /// different answers that would otherwise look identical - an empty report.
    /// </summary>
    [Test]
    public async Task TheCatalogueIsALineOfItsOwnAsync()
    {
        var report = await ReadChecker.RunAsync(m_fixture.Database);

        var catalog = report.Items.Single(item => item.Subject == ReadCheckSubject.Catalog);

        Assert.Multiple(() =>
        {
            Assert.That(catalog.Outcome, Is.EqualTo(ReadCheckOutcome.Ok));
            Assert.That(catalog.RowsRead, Is.EqualTo(4), "the four tables it managed to name");
        });
    }

    /// <summary>
    /// A check that was stopped says so, and does not come back looking like one that finished.
    /// </summary>
    [Test]
    public async Task ACheckThatWasStoppedSaysSoAsync()
    {
        using var cancellation = new CancellationTokenSource();

        await cancellation.CancelAsync();

        var report = await ReadChecker.RunAsync(m_fixture.Database, ct: cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(report.WasCancelled, Is.True);
            Assert.That(report.Failed, Is.Zero, "stopped is not failed");
        });
    }

    /// <summary>
    /// And every note the check can attach has words in every language - the same guard the capability
    /// rows and the formatter's skip reasons carry, for the same reason.
    /// </summary>
    [Test]
    public void EveryNoteHasWordsInEveryLanguageTest()
    {
        string[] notes =
        [
            "ReadCheck.Note.NoColumns",
            "ReadCheck.Note.PlannerDidNotUseIt",
            "ReadCheck.Note.NothingToSeekWith",
            "ReadCheck.Note.SeekNotTraversal"
        ];

        var localization = new OutWit.Database.Studio.Services.Localization.LocalizationService();

        Assert.Multiple(() =>
        {
            foreach (var language in localization.Available)
            {
                var texts = localization.Texts(language.Code);

                foreach (var note in notes)
                    Assert.That(texts.ContainsKey(note), Is.True, $"{language.Code}: {note}");
            }
        });
    }

    #endregion
}
