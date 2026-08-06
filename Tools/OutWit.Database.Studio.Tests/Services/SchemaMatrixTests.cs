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
            ("Empty a table", DdlWriter.Truncate("Logs"))
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
    /// PINS AN OBSERVATION, NOT CORRECT BEHAVIOUR.
    ///
    /// The plan says <c>ALTER COLUMN ... TYPE</c> leaves the rows alone. It does not: the rows are
    /// rewritten. What is wrong with it is worse and quieter - a value that will not convert is
    /// replaced with a default, no error is raised, and changing the type back does not bring the value
    /// back. That is why WS-40 still holds, and why the rebuild counts the casualties first.
    ///
    /// If this ever goes red because the engine refuses the conversion, WS-40 gets simpler and this
    /// test should say so.
    /// </summary>
    [Test]
    public async Task ChangingAColumnTypeRewritesTheRowsAndLosesWhatItCannotConvertAsync()
    {
        await Session.ExecuteNonQueryAsync("CREATE TABLE T (Id INTEGER PRIMARY KEY AUTOINCREMENT, V VARCHAR(30))");
        await Session.ExecuteNonQueryAsync("INSERT INTO T (V) VALUES ('42')");
        await Session.ExecuteNonQueryAsync("INSERT INTO T (V) VALUES ('not a number')");

        Assert.That(await TryAsync("ALTER TABLE T ALTER COLUMN V TYPE INTEGER"), Is.Null,
            "The engine accepts the type change - it is Studio that refuses to offer it in place.");

        var afterChange = await ReadAsync("SELECT Id, V FROM T ORDER BY Id");

        Assert.That(afterChange, Is.EqualTo(new[] { "1|42", "2|0" }),
            "The convertible value survived and the other became 0, with no error.");

        await Session.ExecuteNonQueryAsync("ALTER TABLE T ALTER COLUMN V TYPE VARCHAR(30)");

        var afterUndo = await ReadAsync("SELECT Id, V FROM T ORDER BY Id");

        Assert.That(afterUndo, Is.EqualTo(new[] { "1|42", "2|0" }),
            "Changing the type back does not bring the value back: the rows were rewritten, not reinterpreted.");
    }

    /// <summary>
    /// PINS AN OBSERVATION, NOT CORRECT BEHAVIOUR - and this one is why the rebuild does not rename.
    ///
    /// After <c>ALTER TABLE ... RENAME TO</c> the key generator restarts, and the next generated INSERT
    /// lands on an occupied key and OVERWRITES that row, silently, reporting one row affected.
    /// </summary>
    [Test]
    public async Task RenamingATableLosesItsKeyGeneratorAsync()
    {
        await Session.ExecuteNonQueryAsync("CREATE TABLE R (Id INTEGER PRIMARY KEY AUTOINCREMENT, V VARCHAR(10))");
        await Session.ExecuteNonQueryAsync("INSERT INTO R (V) VALUES ('one')");
        await Session.ExecuteNonQueryAsync("INSERT INTO R (V) VALUES ('two')");

        await Session.ExecuteNonQueryAsync(DdlWriter.RenameTable("R", "R2"));
        await Session.ExecuteNonQueryAsync("INSERT INTO R2 (V) VALUES ('three')");

        var rows = await ReadAsync("SELECT Id, V FROM R2 ORDER BY Id");

        Assert.That(rows, Is.EqualTo(new[] { "1|three", "2|two" }),
            "Row 1 was overwritten by the insert. If this goes red the engine has been fixed, and " +
            "TableRebuild may go back to renaming.");

        // The control that attributes it: the same collision, named explicitly, IS refused. So it is
        // the generated-key path that skips the check, not the check that is missing.
        var explicitClash = await TryAsync("INSERT INTO R2 (Id, V) VALUES (2, 'clash')");

        Assert.That(explicitClash, Does.Contain("UNIQUE"),
            "An explicit duplicate key is refused correctly - only the generated one is not.");
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
    public async Task NotNullWithNoDefaultOnATableWithRowsWedgesItAsync()
    {
        Assert.That(await TryAsync("ALTER TABLE Customers ADD COLUMN Req INTEGER NOT NULL"), Is.Null,
            "The engine accepts it.");

        Assert.That(await ReadAsync("SELECT Id, Req FROM Customers ORDER BY Id"),
            Has.All.EndsWith("|"), "and leaves NULL in every existing row.");

        Assert.That(await TryAsync("UPDATE Customers SET Name = 'renamed' WHERE Id = 1"),
            Does.Contain("NOT NULL"),
            "after which an UPDATE of an UNRELATED column is refused - the table is closed for writing.");

        Assert.That(await TryAsync("INSERT INTO Customers (Name, Email) VALUES ('New', 'n@x')"),
            Does.Contain("NOT NULL"));
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
    /// DROP COLUMN takes the foreign key on it and LEAVES the index, which survives a reopen still
    /// naming a column that does not exist. This is why the change set drops the index first.
    /// </summary>
    [Test]
    public async Task DroppingAColumnLeavesTheIndexOnItBehindAsync()
    {
        await Session.ExecuteNonQueryAsync(DdlWriter.DropColumn("Orders", "CustomerId"));

        var indexes = await Session.GetTableIndexesAsync("Orders");

        Assert.That(indexes.Select(i => i.Name), Does.Contain("IX_Orders_CustomerId"),
            "The index is still in the catalogue, over a column that is gone.");

        var constraints = await ReadAsync(
            "SELECT CONSTRAINT_TYPE FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE TABLE_NAME = 'Orders'");

        Assert.That(constraints, Does.Not.Contain("FOREIGN KEY"),
            "The foreign key on the column DID go with it.");
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
