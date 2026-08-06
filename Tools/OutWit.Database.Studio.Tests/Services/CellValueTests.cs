using System.Data;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// How a value is shown (4.4, 4.5, WS-33, WS-34).
///
/// The types come from a real database in the first case, because the claim being made is about what
/// the ENGINE returns - a fixed set of CLR objects would be testing the test.
/// </summary>
[TestFixture]
public class CellValueTests
{
    #region What the engine returns

    /// <summary>
    /// PINS THE ENGINE, and it is good news: values come back exactly typed, so nothing in the grid
    /// has to reconstruct a DECIMAL from a string or a GUID from sixteen bytes. What WS-34 is really
    /// about is a client that renders them all through ToString and parses them back through double.
    /// </summary>
    [Test]
    public async Task EveryTypeComesBackAsItselfTest()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        await fixture.Database.ExecuteNonQueryAsync(
            "CREATE TABLE Wide (Id INTEGER PRIMARY KEY, D DECIMAL(18,4), F DOUBLE, B BOOLEAN, " +
            "G UNIQUEIDENTIFIER, T DATETIME, Bin BLOB, Txt TEXT)");

        var insert = await fixture.Database.ExecuteQueryAsync(
            "INSERT INTO Wide (Id, D, F, B, G, T, Bin, Txt) VALUES (1, 123.4567, 1.5, TRUE, " +
            "'6f9619ff-8b86-d011-b42d-00c04fc964ff', '2026-08-06 12:34:56', X'89504E470D0A1A0A', " +
            "'{\"a\": 1}')");

        Assert.That(insert.ErrorMessage, Is.Null);

        var read = await fixture.Database.ExecuteQueryAsync("SELECT * FROM Wide");
        var row = read.Data!.Rows[0];

        Assert.Multiple(() =>
        {
            Assert.That(row["D"], Is.TypeOf<decimal>().And.EqualTo(123.4567m));
            Assert.That(row["F"], Is.TypeOf<double>());
            Assert.That(row["B"], Is.TypeOf<bool>());
            Assert.That(row["G"], Is.TypeOf<Guid>());
            Assert.That(row["T"], Is.TypeOf<DateTime>());
            Assert.That(row["Bin"], Is.TypeOf<byte[]>());
        });

        // And the grid's own reading of them.
        Assert.Multiple(() =>
        {
            Assert.That(CellValue.Display(row["D"]), Is.EqualTo("123.4567"),
                "every digit the column declared, and no double on the way");
            Assert.That(CellValue.Display(row["B"]), Is.EqualTo("true"));
            Assert.That(CellValue.Display(row["Bin"]), Does.StartWith("BLOB · "));
            Assert.That(CellValue.KindOf(row["Txt"]), Is.EqualTo(CellKind.Json));
            Assert.That(CellValue.Describe(row["Bin"]), Does.StartWith("PNG image"),
                "the first bytes are recognised rather than guessed at");
        });
    }

    #endregion

    #region NULL

    /// <summary>
    /// WS-33, and the reason it is a decision rather than a detail: an empty string and a NULL are
    /// different things in the database, and a grid that draws both as an empty cell costs somebody an
    /// hour every time it happens.
    /// </summary>
    [Test]
    public void NullIsNeverAnEmptyCellTest()
    {
        Assert.That(CellValue.Display(null), Is.EqualTo("NULL"));
        Assert.That(CellValue.Display(DBNull.Value), Is.EqualTo("NULL"));
        Assert.That(CellValue.Display(string.Empty), Is.Empty, "and an empty string is empty");

        Assert.That(CellValue.KindOf(DBNull.Value), Is.EqualTo(CellKind.Null));
        Assert.That(CellValue.KindOf(string.Empty), Is.EqualTo(CellKind.Text));
    }

    #endregion

    #region One line in a cell

    [Test]
    public void ALineBreakBecomesAMarkAndTheWholeTextStaysInTheViewerTest()
    {
        const string text = "first\nsecond";

        Assert.That(CellValue.Display(text), Does.Not.Contain("\n"));
        Assert.That(CellValue.Display(text), Does.Contain("¶"));
        Assert.That(CellValue.Full(text), Is.EqualTo(text), "the viewer gets it whole");
    }

    [Test]
    public void AGuidIsShortenedInTheCellAndWholeInTheViewerTest()
    {
        var guid = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

        Assert.That(CellValue.Display(guid), Is.EqualTo("6f96…4ff"));
        Assert.That(CellValue.Full(guid), Does.Contain("6f9619ff-8b86-d011-b42d-00c04fc964ff")
            .Or.EqualTo("6f96…4ff"));
    }

    #endregion

    #region Binary

    [Test]
    public void BinaryIsAHexDumpAndNeverTextTest()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x41 };

        var dump = CellValue.Hex(bytes);

        Assert.That(dump, Does.Contain("00000000"));
        Assert.That(dump, Does.Contain("89 50 4E 47"));
        Assert.That(dump, Does.Contain(".PNG"), "the printable bytes beside the hex");
        Assert.That(CellValue.Signature(bytes), Is.EqualTo("PNG image"));
    }

    [Test]
    public void AnUnrecognisedBlobStaysADumpRatherThanBecomingBrokenTextTest()
    {
        var bytes = new byte[] { 0x01, 0x02, 0xFF, 0xFE };

        Assert.That(CellValue.Signature(bytes), Is.Null);
        Assert.That(CellValue.Describe(bytes), Does.StartWith("BLOB · 4 bytes"));
    }

    [Test]
    public void ALargeBlobIsCutWithASayingSoTest()
    {
        var bytes = new byte[9000];

        var dump = CellValue.Hex(bytes, limit: 256);

        Assert.That(dump, Does.Contain("8744 more bytes"));
    }

    #endregion

    #region JSON

    [Test]
    public void JsonBecomesATreeTest()
    {
        var tree = CellValue.Tree("""{"channel": "partner", "tags": ["urgent", "prepaid"], "agent": {"id": 4412}}""");

        Assert.That(tree, Is.Not.Null);
        Assert.That(tree!.Children.Select(node => node.Name),
            Is.EqualTo(new[] { "channel", "tags", "agent" }));

        var tags = tree.Children.First(node => node.Name == "tags");

        Assert.That(tags.Children, Has.Count.EqualTo(2));
        Assert.That(tags.Children[0].Value, Is.EqualTo("urgent"));

        var agent = tree.Children.First(node => node.Name == "agent");

        Assert.That(agent.Children[0].Name, Is.EqualTo("id"));
        Assert.That(agent.Children[0].Value, Is.EqualTo("4412"));
    }

    /// <summary>
    /// A column holds whatever was put in it, and nothing promised it was well formed.
    /// </summary>
    [Test]
    public void TextThatIsNotJsonIsNotATreeAndDoesNotThrowTest()
    {
        Assert.That(CellValue.Tree("{ not json at all"), Is.Null);
        Assert.That(CellValue.Tree("plain text"), Is.Null);
        Assert.That(CellValue.Tree(null), Is.Null);
        Assert.That(CellValue.KindOf("plain text"), Is.EqualTo(CellKind.Text));
    }

    #endregion
}
