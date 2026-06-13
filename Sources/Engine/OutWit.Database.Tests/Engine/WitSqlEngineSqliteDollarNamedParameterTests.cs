namespace OutWit.Database.Tests;

/// <summary>
/// SQLite-style $name parameters (Microsoft.Data.Sqlite / EF ADO compat).
/// </summary>
[TestFixture]
public sealed class WitSqlEngineSqliteDollarNamedParameterTests : WitSqlEngineTestsBase
{
    [Test]
    public void SelectWhereDollarNamedParameterTest()
    {
        m_engine.Execute("CREATE TABLE history (MigrationId TEXT PRIMARY KEY)");
        m_engine.Execute(
            "INSERT INTO history (MigrationId) VALUES (@seed)",
            new Dictionary<string, object?> { ["seed"] = "20260612034124_Initial" });

        var count = m_engine.ExecuteScalar(
            """
            SELECT COUNT(*) FROM history
            WHERE MigrationId = $id
            """,
            new Dictionary<string, object?> { ["$id"] = "20260612034124_Initial" }).AsInt64();

        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public void NumberedParameterStillDistinctFromDollarNamedTest()
    {
        m_engine.Execute("CREATE TABLE t (slot INTEGER PRIMARY KEY, label TEXT)");
        m_engine.Execute("INSERT INTO t (slot, label) VALUES (1, 'first')");

        var label = m_engine.ExecuteScalar(
            "SELECT label FROM t WHERE slot = $1",
            new Dictionary<string, object?> { ["$1"] = 1L }).AsString();

        Assert.That(label, Is.EqualTo("first"));
    }

    [Test]
    public void DollarNamedResolvesBareCallerKeyTest()
    {
        m_engine.Execute("CREATE TABLE t (id INTEGER PRIMARY KEY, label TEXT)");
        m_engine.Execute("INSERT INTO t (id, label) VALUES (1, 'one')");

        // SQL uses $id, caller registers the value under the bare name "id".
        var label = m_engine.ExecuteScalar(
            "SELECT label FROM t WHERE id = $id",
            new Dictionary<string, object?> { ["id"] = 1L }).AsString();

        Assert.That(label, Is.EqualTo("one"));
    }

    [Test]
    public void DollarNamedResolvesAtPrefixedCallerKeyTest()
    {
        m_engine.Execute("CREATE TABLE t (id INTEGER PRIMARY KEY, label TEXT)");
        m_engine.Execute("INSERT INTO t (id, label) VALUES (1, 'one')");

        // SQL uses $id, caller registers the value under "@id".
        var label = m_engine.ExecuteScalar(
            "SELECT label FROM t WHERE id = $id",
            new Dictionary<string, object?> { ["@id"] = 1L }).AsString();

        Assert.That(label, Is.EqualTo("one"));
    }

    [Test]
    public void ColonNamedResolvesBareCallerKeyTest()
    {
        m_engine.Execute("CREATE TABLE t (id INTEGER PRIMARY KEY, label TEXT)");
        m_engine.Execute("INSERT INTO t (id, label) VALUES (1, 'one')");

        // SQL uses :id, caller registers the value under the bare name "id".
        var label = m_engine.ExecuteScalar(
            "SELECT label FROM t WHERE id = :id",
            new Dictionary<string, object?> { ["id"] = 1L }).AsString();

        Assert.That(label, Is.EqualTo("one"));
    }

    [Test]
    public void ExactDollarKeyWinsOverNormalizedFallbackTest()
    {
        m_engine.Execute("CREATE TABLE t (id INTEGER PRIMARY KEY, label TEXT)");
        m_engine.Execute("INSERT INTO t (id, label) VALUES (1, 'one')");
        m_engine.Execute("INSERT INTO t (id, label) VALUES (2, 'two')");

        // Both an exact "$id" and a bare "id" (normalized to "@id") are supplied.
        // The exact placeholder key must win - the fallback must never override it.
        var label = m_engine.ExecuteScalar(
            "SELECT label FROM t WHERE id = $id",
            new Dictionary<string, object?> { ["$id"] = 1L, ["id"] = 2L }).AsString();

        Assert.That(label, Is.EqualTo("one"));
    }

    [Test]
    public void MixedPrefixPlaceholdersResolveBareCallerKeysTest()
    {
        m_engine.Execute("CREATE TABLE t (a INTEGER, b INTEGER, c INTEGER, label TEXT)");
        m_engine.Execute("INSERT INTO t (a, b, c, label) VALUES (1, 2, 3, 'row')");

        // One statement mixing @-, $- and :-prefixed placeholders, all supplied as bare names.
        var label = m_engine.ExecuteScalar(
            "SELECT label FROM t WHERE a = @x AND b = $y AND c = :z",
            new Dictionary<string, object?> { ["x"] = 1L, ["y"] = 2L, ["z"] = 3L }).AsString();

        Assert.That(label, Is.EqualTo("row"));
    }
}
