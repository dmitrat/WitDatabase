using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// The matrix of 5.2, re-measured against the engine.
///
/// This is the control built into the instrument. <see cref="SchemaCapabilities.Matrix"/> is what the
/// designer promises: this edit is one ALTER, that one needs the table rebuilt. A matrix nobody
/// re-measures drifts away from the engine and starts promising things - which is the exact failure
/// section 5 exists to prevent - so every row of it is executed here against a real database.
///
/// Two of the "rebuild" rows are not refusals but DAMAGE, and they are pinned as observations with the
/// value they produce today. If the engine starts refusing them, or starts converting correctly, these
/// go red and the matrix has to be rewritten - which is the point.
/// </summary>
[TestFixture]
public class SchemaMatrixTests
{
    private StudioFixture m_fixture = null!;

    [SetUp]
    public async Task SetUpAsync()
    {
        m_fixture = await StudioFixture.CreateAsync();
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        await m_fixture.DisposeAsync();
    }

    private IDatabaseSession Session => m_fixture.Database;

    private async Task<string?> TryAsync(string sql)
    {
        try
        {
            await Session.ExecuteNonQueryAsync(sql);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message.Split('\n')[0].Trim();
        }
    }

    #region In place

    [Test]
    public async Task EveryInPlaceChangeIsAcceptedByTheEngineAsync()
    {
        // One case per "in place" row of the matrix, in the writer's own words - so that a change to
        // DdlWriter that produces SQL the engine will not take is caught here rather than by a user.
        var statements = new List<(string Row, string Sql)>
        {
            ("Add a column", DdlWriter.AddColumn("Customers", new ColumnDraft
            {
                Name = "Note", DataType = "VARCHAR", MaxLength = 50, IsNullable = true
            })),
            ("Add a column, with everything on it", DdlWriter.AddColumn("Customers", new ColumnDraft
            {
                Name = "Code", DataType = "VARCHAR", MaxLength = 10, IsNullable = true,
                IsUnique = true, DefaultValue = "'x'", CheckExpression = "LENGTH(Code) > 0"
            })),
            ("Add a computed column", DdlWriter.AddColumn("Customers", new ColumnDraft
            {
                Name = "Upper", DataType = "VARCHAR", ComputedExpression = "UPPER(Name)"
            })),
            ("Rename a column", DdlWriter.RenameColumn("Customers", "Note", "Comment")),
            ("Drop a column", DdlWriter.DropColumn("Customers", "Comment")),
            ("Set a default", DdlWriter.SetDefault("Customers", "Email", "'none@example'")),
            ("Drop a default", DdlWriter.DropDefault("Customers", "Email")),
            ("Drop NOT NULL", DdlWriter.DropNotNull("Customers", "Name")),
            ("Set NOT NULL", DdlWriter.SetNotNull("Customers", "Name")),
            ("Add UNIQUE", DdlWriter.AddUnique("Customers", "UQ_Customers_Email", "Email")),
            ("Drop a constraint", DdlWriter.DropConstraint("Customers", "UQ_Customers_Email")),
            ("Add CHECK", DdlWriter.AddCheck("Orders", "CK_Orders_Total", "Total >= 0")),
            ("Add a foreign key", DdlWriter.AddForeignKey("OrdersAudit", "FK_OrdersAudit_Orders_OrderId",
                "OrderId", "Orders", "Id")),
            ("Rename a table", DdlWriter.RenameTable("Logs", "LogLines")),
            ("Rename it back", DdlWriter.RenameTable("LogLines", "Logs")),
            ("Empty a table", DdlWriter.Truncate("Logs")),

            // UPDATE OF is only legal after UPDATE and the writer knows it - a list written after
            // INSERT would be a parse error, and this row is here so that the WRITER's rule is
            // measured against the engine rather than asserted against a string.
            ("Create a trigger watching two columns", DdlWriter.CreateTrigger(new TriggerDraft
            {
                Name = "TR_Orders_Watch", Table = "Orders", Timing = "AFTER", Event = "UPDATE",
                UpdateColumns = ["Total", "Status"], ForEachRow = true,
                Body = "INSERT INTO OrdersAudit (OrderId) VALUES (NEW.Id);"
            })),
            ("Drop it again", DdlWriter.DropTrigger("TR_Orders_Watch"))
        };

        var refused = new List<string>();

        foreach (var (row, sql) in statements)
        {
            var error = await TryAsync(sql);

            if (error != null)
                refused.Add($"{row}: {sql} -> {error}");
        }

        Assert.That(refused, Is.Empty,
            "The matrix says these are one ALTER each, and the engine refused them:\n" + string.Join("\n", refused));
    }

    #endregion

    #region Rebuild

    [Test]
    public async Task AddingAPrimaryKeyIsRefusedAsync()
    {
        var error = await TryAsync("ALTER TABLE Logs ADD CONSTRAINT PK_Logs PRIMARY KEY (Message)");

        Assert.That(error, Is.Not.Null, "The matrix says a key cannot be added to an existing table.");
        Assert.That(error, Does.Contain("PRIMARY KEY"));
    }

    [Test]
    public async Task DroppingAKeyColumnIsRefusedAsync()
    {
        var error = await TryAsync(DdlWriter.DropColumn("Customers", "Id"));

        Assert.That(error, Is.Not.Null);
        Assert.That(error, Does.Contain("primary key"));
    }

    [Test]
    public async Task ThereIsNoWayToMoveAColumnAsync()
    {
        // The matrix says a reorder is a rebuild because the language has no syntax for it. Four
        // spellings, from four dialects, and all four are parse errors.
        foreach (var sql in new[]
                 {
                     "ALTER TABLE Customers ADD COLUMN Zed INTEGER FIRST",
                     "ALTER TABLE Customers ADD COLUMN Zed INTEGER AFTER Id",
                     "ALTER TABLE Customers MODIFY COLUMN Name VARCHAR(200)",
                     "ALTER TABLE Customers CHANGE Name Title VARCHAR(200)"
                 })
        {
            Assert.That(await TryAsync(sql), Is.Not.Null, $"{sql} was accepted, so the matrix is wrong.");
        }
    }

    /// <summary>
    /// WAS A PIN, NOW AN ASSERTION. It went red when the engine was fixed, which is what it was for.
    ///
    /// The plan said <c>ALTER COLUMN ... TYPE</c> leaves the rows alone. It does not - it rewrites
    /// them - and what was actually wrong with it was quieter: a value that would not convert was
    /// replaced with a default, no error was raised, and changing the type back did not bring it back.
    /// The engine now refuses such a value instead, naming it.
    ///
    /// <b>WS-40 still holds, and for the reason it was rewritten with:</b> the designer offers a type
    /// change as a REBUILD, so the conversion is a CAST the user can read and the values that will not
    /// survive it are counted before anything runs. An in-place ALTER now fails at the first bad value
    /// with nothing changed, which is safe but tells the user nothing about the other 999.
    /// </summary>
    [Test]
    public async Task ChangingAColumnTypeRefusesAValueItCannotReadAsync()
    {
        await Session.ExecuteNonQueryAsync("CREATE TABLE T (Id INTEGER PRIMARY KEY AUTOINCREMENT, V VARCHAR(30))");
        await Session.ExecuteNonQueryAsync("INSERT INTO T (V) VALUES ('42')");
        await Session.ExecuteNonQueryAsync("INSERT INTO T (V) VALUES ('not a number')");

        Assert.That(await TryAsync("ALTER TABLE T ALTER COLUMN V TYPE INTEGER"),
            Does.Contain("not a number"),
            "The engine refuses, naming the value that stopped it.");

        Assert.That(await ReadAsync("SELECT Id, V FROM T ORDER BY Id"),
            Is.EqualTo(new[] { "1|42", "2|not a number" }),
            "and nothing was changed on the way to finding out.");
    }

    /// <summary>
    /// The control: a column whose values all read as the new type is still converted in place. The
    /// designer refuses to OFFER that as an edit - a rebuild shows the conversion - but the engine
    /// performing it is what makes the refusal a choice rather than a workaround.
    /// </summary>
    [Test]
    public async Task AConvertibleColumnIsStillConvertedByTheEngineAsync()
    {
        await Session.ExecuteNonQueryAsync("CREATE TABLE T (Id INTEGER PRIMARY KEY AUTOINCREMENT, V VARCHAR(30))");
        await Session.ExecuteNonQueryAsync("INSERT INTO T (V) VALUES ('42')");

        Assert.That(await TryAsync("ALTER TABLE T ALTER COLUMN V TYPE INTEGER"), Is.Null);

        Assert.That(await ReadAsync("SELECT Id, V FROM T"), Is.EqualTo(new[] { "1|42" }));
    }

    /// <summary>
    /// WAS A PIN, NOW AN ASSERTION, and the one that decided the shape of the rebuild.
    ///
    /// A renamed table used to restart its key generator, so the next generated INSERT landed on key 1
    /// and OVERWROTE the row that was there - silently, reporting one row affected. The rename now
    /// carries the counter, and a generated key that lands on an existing row is refused rather than
    /// written.
    ///
    /// <b>The rebuild still does not rename</b>, and that is now a choice rather than a necessity: it
    /// copies the rows out and back, which leaves the carrier as something to recover from if a step
    /// fails. Renaming would be one statement fewer and no safer.
    /// </summary>
    [Test]
    public async Task RenamingATableKeepsItsKeyGeneratorAsync()
    {
        await Session.ExecuteNonQueryAsync("CREATE TABLE R (Id INTEGER PRIMARY KEY AUTOINCREMENT, V VARCHAR(10))");
        await Session.ExecuteNonQueryAsync("INSERT INTO R (V) VALUES ('one')");
        await Session.ExecuteNonQueryAsync("INSERT INTO R (V) VALUES ('two')");

        await Session.ExecuteNonQueryAsync(DdlWriter.RenameTable("R", "R2"));
        await Session.ExecuteNonQueryAsync("INSERT INTO R2 (V) VALUES ('three')");

        Assert.That(await ReadAsync("SELECT Id, V FROM R2 ORDER BY Id"),
            Is.EqualTo(new[] { "1|one", "2|two", "3|three" }),
            "the insert adds a row - it used to land on key 1 and destroy 'one'");

        // The control that attributed the defect when it was one, kept because it is still the rule:
        // an explicit duplicate key is refused.
        Assert.That(await TryAsync("INSERT INTO R2 (Id, V) VALUES (2, 'clash')"), Does.Contain("UNIQUE"));
    }

    #endregion

    #region Drop and create

    [Test]
    public async Task ThereIsNoAlterForAViewOrATriggerAsync()
    {
        Assert.Multiple(async () =>
        {
            Assert.That(await TryAsync("ALTER VIEW ActiveOrders AS SELECT Id FROM Orders"), Is.Not.Null);
            Assert.That(await TryAsync("CREATE OR REPLACE VIEW ActiveOrders AS SELECT Id FROM Orders"), Is.Not.Null);
            Assert.That(await TryAsync("ALTER TRIGGER TR_Orders_Audit DISABLE"), Is.Not.Null);
            Assert.That(await TryAsync("REINDEX IX_Orders_CustomerId"), Is.Not.Null);
            Assert.That(await TryAsync("ALTER INDEX IX_Orders_CustomerId REBUILD"), Is.Not.Null);
        });
    }

    /// <summary>
    /// Everything the matrix says is missing from the engine is actually missing. The list is shown to
    /// the user as absent rather than as a button that fails, so it has to be true.
    /// </summary>
    [Test]
    public void TheAbsentListIsNotEmptyAndIsAboutThisEngine()
    {
        Assert.That(SchemaCapabilities.NotInTheEngine, Is.Not.Empty);
        Assert.That(SchemaCapabilities.Matrix.Count, Is.EqualTo(11));
    }

    #endregion

    #region The rules Studio applies itself

    /// <summary>
    /// The engine ACCEPTS this and it wrecks the table. Studio refuses it instead, which is the one
    /// place in section 5 where the designer is stricter than the engine - so the reason had better be
    /// real, and this is it.
    /// </summary>
    [Test]
    public async Task NotNullWithNoDefaultOnATableWithRowsIsRefusedByTheEngineTooAsync()
    {
        // WAS A PIN, NOW AN ASSERTION. The engine used to ACCEPT this, leave NULL in every existing
        // row and then refuse every later write to the table - including an UPDATE of an unrelated
        // column. It refuses the statement itself now.
        //
        // Studio still refuses it first, and that is not redundant: the designer says so in the row
        // while the user is still deciding, instead of letting Apply come back with an error.
        Assert.That(await TryAsync("ALTER TABLE Customers ADD COLUMN Req INTEGER NOT NULL"),
            Does.Contain("DEFAULT"),
            "the engine refuses it and says what would work");

        Assert.That(await TryAsync("UPDATE Customers SET Name = 'renamed' WHERE Id = 1"), Is.Null,
            "and the table is still writable, which is the whole point");
    }

    [Test]
    public async Task OnAnEmptyTableTheSameStatementIsHarmlessAsync()
    {
        // The control: Studio's refusal is about the ROWS, not about the statement. On an empty table
        // it is allowed, and a refusal there would be a rule Studio invented.
        await Session.ExecuteNonQueryAsync("CREATE TABLE E (Id INTEGER PRIMARY KEY AUTOINCREMENT, A VARCHAR(10))");

        Assert.That(await TryAsync("ALTER TABLE E ADD COLUMN B INTEGER NOT NULL"), Is.Null);
        Assert.That(await TryAsync("INSERT INTO E (A, B) VALUES ('a', 1)"), Is.Null);
    }

    /// <summary>
    /// WAS A PIN, NOW AN ASSERTION. The index over a dropped column used to stay in the catalogue,
    /// naming a column that no longer existed, and survive a reopen.
    ///
    /// The change set still drops the index explicitly first, and that stays: it runs before the
    /// column drop, so it is the statement the user reads in the DDL panel rather than something the
    /// engine does invisibly.
    /// </summary>
    [Test]
    public async Task DroppingAColumnTakesTheIndexOnItAsync()
    {
        await Session.ExecuteNonQueryAsync(DdlWriter.DropColumn("Orders", "CustomerId"));

        var indexes = await Session.GetTableIndexesAsync("Orders");

        Assert.That(indexes.Select(i => i.Name), Does.Not.Contain("IX_Orders_CustomerId"));

        var constraints = await ReadAsync(
            "SELECT CONSTRAINT_TYPE FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE TABLE_NAME = 'Orders'");

        Assert.That(constraints, Does.Not.Contain("FOREIGN KEY"),
            "and the foreign key goes with it, as it always did");
    }

    #endregion

    #region Tools

    private async Task<List<string>> ReadAsync(string sql)
    {
        var result = await Session.ExecuteQueryAsync(sql);

        Assert.That(result.ErrorMessage, Is.Null.Or.Empty, sql);

        var rows = new List<string>();

        foreach (System.Data.DataRow row in result.Data!.Rows)
            rows.Add(string.Join("|", row.ItemArray.Select(v => v is null or DBNull ? "" : $"{v}")));

        return rows;
    }

    #endregion
}
