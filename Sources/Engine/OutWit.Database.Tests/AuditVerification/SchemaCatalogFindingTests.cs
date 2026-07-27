namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// Verification of the engine-side <c>blocker-migrations</c> finding of the 2026-07 audit.
/// </summary>
/// <remarks>See Docs/NEXT-SESSION-PLAN.md workstream B.</remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class SchemaCatalogFindingTests : WitSqlEngineTestsBase
{
    #region SchemaCatalog.AddColumn accepts a duplicate column name

    [Test]
    [Ignore("CONFIRMED 2026-07-27: the second ALTER TABLE ADD COLUMN is accepted without error. "
            + "blocker-migrations, Engine/Schema/SchemaCatalog.Columns.cs:17")]
    public void AddingTheSameColumnTwiceIsRejectedTest()
    {
        // Finding: SchemaCatalog.Columns.cs:17 - AddColumn does not reject a duplicate name, so a
        // replayed ALTER TABLE ADD COLUMN appends a second column of the same name. Migrations are
        // replayed routinely - a partially applied migration, a script run twice - so this is not a
        // hypothetical path.
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY)");
        m_engine.Execute("ALTER TABLE T ADD COLUMN A INT");

        Assert.That(() => m_engine.Execute("ALTER TABLE T ADD COLUMN A INT"), Throws.Exception,
            "a column named A already exists, so adding it again must be rejected");
    }

    [Test]
    [Ignore("CONFIRMED 2026-07-27, with the damage visible in the catalog: the table ends up with "
            + "columns [Id, A, A]. A replayed migration - a partially applied one, or a script run "
            + "twice - widens every row again.")]
    public void ReplayedAddColumnDoesNotDuplicateTheColumnTest()
    {
        // The consequence: whether or not the second ALTER is rejected, the table must not end up
        // holding the same column twice.
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY)");
        m_engine.Execute("ALTER TABLE T ADD COLUMN A INT");
        try { m_engine.Execute("ALTER TABLE T ADD COLUMN A INT"); }
        catch { /* rejection is the correct outcome and is asserted above */ }

        var table = m_engine.GetTable("T");
        var duplicates = table!.Columns
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} x{g.Count()}")
            .ToList();

        Assert.That(duplicates, Is.Empty,
            $"the table must not hold a column twice. Columns: " +
            $"{string.Join(", ", table.Columns.Select(c => c.Name))}");
    }

    #endregion
}
