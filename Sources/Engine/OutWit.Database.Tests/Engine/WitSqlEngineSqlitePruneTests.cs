namespace OutWit.Database.Tests;

/// <summary>
/// SQLite-style prune: DELETE WHERE id NOT IN (SELECT id ... ORDER BY id DESC LIMIT n).
/// </summary>
[TestFixture]
public sealed class WitSqlEngineSqlitePruneTests : WitSqlEngineTestsBase
{
    [Test]
    public void DeleteNotInOrderedLimitedSubqueryTest()
    {
        m_engine.Execute("CREATE TABLE t (id INTEGER PRIMARY KEY AUTOINCREMENT, v TEXT)");
        for (var i = 0; i < 5; i++)
        {
            m_engine.Execute("INSERT INTO t (v) VALUES (@v)", new Dictionary<string, object?> { ["v"] = $"r{i}" });
        }

        m_engine.Execute(
            """
            DELETE FROM t
            WHERE id NOT IN (
                SELECT id FROM t ORDER BY id DESC LIMIT @keep
            )
            """,
            new Dictionary<string, object?> { ["keep"] = 3L });

        var count = m_engine.ExecuteScalar("SELECT COUNT(*) FROM t").AsInt64();
        Assert.That(count, Is.EqualTo(3));

        var minId = m_engine.ExecuteScalar("SELECT MIN(id) FROM t").AsInt64();
        Assert.That(minId, Is.EqualTo(3));
    }
}
