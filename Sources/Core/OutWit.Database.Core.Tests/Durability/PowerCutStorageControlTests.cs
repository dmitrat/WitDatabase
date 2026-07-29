using OutWit.Database.Core.Storage;

namespace OutWit.Database.Core.Tests.Durability;

/// <summary>
/// The controls for <see cref="PowerCutStorage"/>. They say nothing about the database - they say
/// the model models something, and without them every result taken through it is unreadable.
/// </summary>
/// <remarks>
/// The pair C4/C5 is the load-bearing part: a write that was flushed must survive the cut, and the
/// same write without a flush must not. If C4 fails the model is too aggressive and will report
/// durable data as lost; if C5 passes it is not modelling a cut at all and will report a real defect
/// as safe. One without the other proves nothing.
///
/// C6 pins that <see cref="PowerCutStorage.FlushCount"/> can see a flush at all, which is what makes
/// a <b>zero</b> count on some other path evidence rather than a broken counter.
/// </remarks>
[TestFixture]
[Category("Durability")]
public sealed class PowerCutStorageControlTests
{
    #region Constants

    private const int PAGE_SIZE = 4096;
    private const long PAGE = 1;

    #endregion

    #region C4 - a flushed write survives the cut

    [Test]
    public void ControlFlushedWriteSurvivesThePowerCutTest()
    {
        using var media = new StorageMemory(PAGE_SIZE);
        var storage = new PowerCutStorage(media, ownsMedia: false);

        storage.SetSize(PAGE + 1);
        storage.WritePage(PAGE, Filled(0xAB));
        storage.Flush();

        storage.PowerCut();

        var read = new byte[PAGE_SIZE];
        storage.ReadPage(PAGE, read);

        Assert.That(read[0], Is.EqualTo(0xAB),
            "the write was flushed before the cut, so it must survive - a model that loses it would "
            + "report durable data as lost and turn every correct path into a finding");
    }

    #endregion

    #region C5 - an unflushed write does not

    /// <summary>
    /// Both directions in one shape: page 0 is flushed, page 1 is not, then the power goes. The
    /// survivor and the casualty are asserted by <i>value</i> rather than by an exception type -
    /// this suite has already been caught out by a bare <c>Throws</c> that was satisfied for an
    /// unrelated reason.
    /// </summary>
    [Test]
    public void ControlUnflushedWriteIsLostInThePowerCutTest()
    {
        using var media = new StorageMemory(PAGE_SIZE);
        var storage = new PowerCutStorage(media, ownsMedia: false);

        storage.SetSize(2);
        storage.WritePage(0, Filled(0xAB));
        storage.Flush();

        storage.WritePage(PAGE, Filled(0xCD));

        Assert.That(storage.PagesAtRisk, Is.EqualTo(1), "the second page is written and not durable");

        var lost = storage.PowerCut();

        Assert.Multiple(() =>
        {
            Assert.That(lost, Is.EqualTo(1), "exactly the unflushed page is lost");

            Assert.That(storage.PageCount, Is.EqualTo(1),
                "the media was never extended for the unflushed page, so after the cut the storage "
                + "is one page long - if it still claims two, the cut left the write's footprint "
                + "behind and the model is not modelling a power failure");

            var read = new byte[PAGE_SIZE];
            storage.ReadPage(0, read);

            Assert.That(read[0], Is.EqualTo(0xAB),
                "and the flushed page is untouched, which is what makes the loss above attributable "
                + "to the missing flush rather than to the cut destroying everything");
        });
    }

    #endregion

    #region C5b - and the process cannot tell before the cut

    [Test]
    public void ControlAnUnflushedWriteIsReadableUntilThePowerCutTest()
    {
        using var media = new StorageMemory(PAGE_SIZE);
        var storage = new PowerCutStorage(media, ownsMedia: false);

        storage.SetSize(PAGE + 1);
        storage.WritePage(PAGE, Filled(0xEF));

        var read = new byte[PAGE_SIZE];
        storage.ReadPage(PAGE, read);

        Assert.That(read[0], Is.EqualTo(0xEF),
            "before the power goes, a cached page is indistinguishable from a durable one - which is "
            + "exactly why this class of defect never shows up in an ordinary test");
    }

    #endregion

    #region C6 - the flush counter can see a flush

    [Test]
    public void ControlFlushCountCountsRealFlushesTest()
    {
        using var media = new StorageMemory(PAGE_SIZE);
        var storage = new PowerCutStorage(media, ownsMedia: false);

        Assert.That(storage.FlushCount, Is.Zero, "nothing has been flushed yet");

        storage.SetSize(PAGE + 1);
        storage.WritePage(PAGE, Filled(0x01));
        storage.Flush();
        storage.Flush();

        Assert.That(storage.FlushCount, Is.EqualTo(2),
            "the counter has to see a flush that happened, or a zero count on some other path proves "
            + "nothing about that path");
    }

    #endregion

    #region Tools

    private static byte[] Filled(byte value)
    {
        var page = new byte[PAGE_SIZE];
        Array.Fill(page, value);

        return page;
    }

    #endregion
}
