using System.Text;

namespace OutWit.Database.AdoNet.Tests.Modularity;

/// <summary>
/// Phase 11 instrument B - one database per legal combination of options, one workload through each, and
/// a verdict: does it work, does it refuse, or does it quietly answer something else?
/// </summary>
/// <remarks>
/// <para>
/// WitDatabase is built as a construction kit - a workload picks a store, a transaction model, a
/// parallel mode, encryption, a journal and a cache - and the combinations have never been enumerated,
/// let alone run. The census (instrument A) asks whether a setting reaches the engine; this asks the
/// question that matters to a consumer, which is whether the engine it reached still answers correctly.
/// </para>
/// <para>
/// <b>Three outcomes, and the third is the dangerous one.</b> A combination that works is fine. A
/// combination refused at <c>Open</c> with a legible message is fine too - a construction kit is allowed
/// to have illegal shapes, as long as it says so. A combination that opens, accepts every statement, and
/// answers differently from every other configuration is the failure this instrument exists to find.
/// </para>
/// <para>
/// <b>The workload is written so that its answers do not depend on the configuration.</b> Where a
/// configuration cannot run a step - no transactions, so no explicit commit - the same rows are written
/// without it, so every combination must produce byte-identical answers. That is what makes a diff
/// meaningful instead of a judgement call.
/// </para>
/// <para>
/// <b>The control is the reference itself.</b> <c>TheReferenceAnswersAreWhatTheyClaimTest</c> asserts the
/// default configuration against hard-coded literals rather than against another run of the engine. Every
/// other case compares to that reference, so an engine-wide regression cannot make the whole matrix agree
/// with itself and pass - which is exactly how a comparison harness lies.
/// </para>
/// </remarks>
[TestFixture]
[Category("Modularity")]
public class CombinationMatrixTests
{
    #region Types

    /// <summary>What the matrix expects of a combination before it is run.</summary>
    public enum Expectation
    {
        /// <summary>Opens, runs, answers like every other configuration, and survives a reopen.</summary>
        Works,

        /// <summary>Opens and runs, but the data is gone after a reopen - an in-memory store.</summary>
        NotPersistent,

        /// <summary>Refused at <c>Open</c>. The message is asserted, not just the throw.</summary>
        Refused
    }

    public sealed record Combination(string Label, string Settings, Expectation Expectation, string? RefusedBecause = null)
    {
        public override string ToString() => Label;
    }

    public sealed record Answers(string Scan, string IndexLookup, string Aggregate)
    {
        public override string ToString() => $"scan=[{Scan}] index=[{IndexLookup}] sum=[{Aggregate}]";
    }

    #endregion

    #region Fields

    private string m_root = null!;
    private int m_sequence;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witdb_matrix_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
        m_sequence = 0;
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_root))
                Directory.Delete(m_root, recursive: true);
        }
        catch
        {
            // Cleanup must not fail the run.
        }
    }

    #endregion

    #region The matrix

    private static readonly string[] STORES = ["btree", "lsm", "inmemory"];

    /// <summary>The transaction model, which is two keywords that only make sense together.</summary>
    private static readonly (string Label, string Settings)[] TRANSACTIONS =
    [
        ("tx=mvcc", "Transactions=true;MVCC=true"),
        ("tx=locks", "Transactions=true;MVCC=false"),
        ("tx=off", "Transactions=false")
    ];

    private static readonly (string Label, string Settings)[] ENCRYPTION =
    [
        ("plain", ""),
        ("aes", "Encryption=aes-gcm;Password=matrix-secret;FastEncryption=true")
    ];

    /// <summary>
    /// The orthogonal add-ons, swept one at a time against the default base rather than crossed with
    /// everything - they are separate subsystems, and crossing them would multiply the matrix by nine for
    /// no extra question asked.
    /// </summary>
    private static readonly (string Label, string Settings, Expectation Expectation, string? Because)[] ADD_ONS =
    [
        // A journal is only reachable through the lock-based transactional store. With MVCC - the
        // default - nothing would use it, so the build refuses rather than accepting and ignoring.
        ("journal=wal", "Journal=wal", Expectation.Refused, "cannot be combined with MVCC"),
        ("journal=rollback", "Journal=rollback", Expectation.Refused, "cannot be combined with MVCC"),
        ("journal=wal+locks", "Journal=wal;MVCC=false", Expectation.Works, null),
        ("journal=rollback+locks", "Journal=rollback;MVCC=false", Expectation.Works, null),
        ("cache=clock", "Cache=clock", Expectation.Works, null),
        ("cache=lru", "Cache=lru", Expectation.Works, null),
        ("pagesize=16384", "PageSize=16384", Expectation.Works, null),
        ("cachesize=64", "CacheSize=64", Expectation.Works, null),
        ("sync=off", "Synchronous Commit=false", Expectation.Works, null),
        ("locking=off", "FileLocking=false", Expectation.Works, null),
        ("isolation=serializable", "Isolation Level=Serializable", Expectation.Works, null),
        ("isolation=snapshot", "Isolation Level=Snapshot", Expectation.Works, null),
        // Removed in 12.0.0 and refused rather than ignored - the phase's own rule applied to
        // the phase's own removal.
        ("parallel-mode-removed", "Parallel Mode=Auto", Expectation.Refused, "was removed in 12.0.0"),
        ("max-writers-removed", "Max Writers=2", Expectation.Refused, "was removed in 12.0.0")
    ];

    private static IEnumerable<Combination> Matrix()
    {
        foreach (var store in STORES)
        foreach (var (transactionLabel, transactionSettings) in TRANSACTIONS)
        foreach (var (encryptionLabel, encryptionSettings) in ENCRYPTION)
        {
            var settings = Join($"Store={store}", transactionSettings, encryptionSettings);
            var label = $"{store} {transactionLabel} {encryptionLabel}";

            yield return new Combination(label, settings, Expect(store));
        }

        foreach (var (label, settings, expectation, because) in ADD_ONS)
            yield return new Combination($"btree {label}", settings, expectation, because);

        // The page-oriented add-ons are B+Tree-only: the LSM store has no pages and no page cache.
        foreach (var (label, settings, expectation, because) in ADD_ONS.Where(
                     a => !a.Settings.Contains("PageSize") && !a.Settings.Contains("Cache")))
        {
            yield return new Combination($"lsm {label}", Join("Store=lsm", settings), expectation, because);
        }
    }

    private static Expectation Expect(string store)
    {
        // The in-memory store keeps nothing after the connection closes, whatever Data Source says. That
        // is what the store is, not a defect - but it has to be stated, because the connection string
        // naming a file reads like a promise that it is written to one.
        return store == "inmemory" ? Expectation.NotPersistent : Expectation.Works;
    }

    #endregion

    #region The control

    /// <summary>
    /// The control: the reference answers, asserted against literals rather than against the engine.
    /// </summary>
    /// <remarks>
    /// Without this, every case in the matrix compares one run of WitDatabase against another run of
    /// WitDatabase, and a change that broke all of them equally would pass the whole fixture. Phase 3 and
    /// phase 10 both had a harness that could only see disagreement, and both were wrong before their
    /// subject was.
    /// </remarks>
    [Test]
    public void TheReferenceAnswersAreWhatTheyClaimTest()
    {
        var (answers, _) = RunCombination(new Combination("reference", "", Expectation.Works));

        Assert.Multiple(() =>
        {
            Assert.That(answers!.Scan, Is.EqualTo(EXPECTED.Scan), "the reference scan");
            Assert.That(answers.IndexLookup, Is.EqualTo(EXPECTED.IndexLookup), "the reference index lookup");
            Assert.That(answers.Aggregate, Is.EqualTo(EXPECTED.Aggregate), "the reference aggregate");
        });
    }

    /// <summary>
    /// What the workload below produces, written out by hand. Eight rows inserted, row 2 renamed, row 5
    /// deleted, row 10 committed, row 11 rolled back.
    /// </summary>
    private static readonly Answers EXPECTED = new(
        Scan: "1:row1:1.25|2:updated:2.25|3:row3:3.25|4:row4:4.25|6:row6:6.25|7:row7:7.25|8:row8:8.25|10:committed:10.00",
        IndexLookup: "3:row3",
        // 1.25 + 2.25 + 3.25 + 4.25 + 6.25 + 7.25 + 8.25 + 10.00. Written out because the first version of
        // this literal was wrong, and the control caught it before a single engine verdict was believed.
        Aggregate: "8:42.75");

    #endregion

    #region The matrix run

    [Test]
    [TestCaseSource(nameof(Matrix))]
    public void CombinationWorksTest(Combination combination)
    {
        Answers? answers;
        string? failure;

        try
        {
            (answers, failure) = RunCombination(combination);
        }
        catch (Exception e)
        {
            if (combination.Expectation == Expectation.Refused)
            {
                Assert.That(e.Message, Does.Contain(combination.RefusedBecause!),
                    $"{combination.Label} was refused, which is expected, but not for the stated reason.");
                return;
            }

            Assert.Fail($"{combination.Label} refused to open: {e.GetType().Name}: {Short(e)}");
            return;
        }

        Assert.That(combination.Expectation, Is.Not.EqualTo(Expectation.Refused),
            $"{combination.Label} was expected to be refused at Open and was not.");

        Assert.That(failure, Is.Null, $"{combination.Label} could not run its workload.");
        Assert.That(answers, Is.EqualTo(EXPECTED), $"{combination.Label} answered differently.");
    }

    /// <summary>
    /// Persistence is asked separately, because it is a different question and it has a different answer
    /// for the in-memory store - which is a legitimate configuration, not a broken one.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(Matrix))]
    public void CombinationSurvivesAReopenTest(Combination combination)
    {
        if (combination.Expectation == Expectation.Refused)
            Assert.Ignore("Refused at Open - nothing to reopen.");

        var directory = NewDirectory();
        var dataSource = Path.Combine(directory, "matrix.witdb");

        using (var connection = new WitDbConnection(Compose(dataSource, combination.Settings)))
        {
            connection.Open();
            var failure = RunWorkload(connection, combination);
            Assert.That(failure, Is.Null, $"{combination.Label} could not run its workload.");
        }

        using var reopened = new WitDbConnection(Compose(dataSource, combination.Settings));
        reopened.Open();

        if (combination.Expectation == Expectation.NotPersistent)
        {
            // A store that keeps nothing comes back with no table at all, so asking for the rows throws
            // rather than answering none. Both are "the data is gone"; the distinction matters only to
            // this assertion.
            var kept = "";

            try
            {
                kept = Scan(reopened);
            }
            catch (WitDbException e) when (e.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                kept = "";
            }

            Assert.That(kept, Is.Empty,
                $"{combination.Label} kept data across a reopen, which the matrix records it as not doing. " +
                "The record is what is wrong if this fails - update it.");
            return;
        }

        Assert.That(Scan(reopened), Is.EqualTo(EXPECTED.Scan),
            $"{combination.Label} lost or changed data across a reopen.");
    }

    private (Answers? Answers, string? Failure) RunCombination(Combination combination)
    {
        var directory = NewDirectory();
        var dataSource = Path.Combine(directory, "matrix.witdb");

        using var connection = new WitDbConnection(Compose(dataSource, combination.Settings));
        connection.Open();

        var failure = RunWorkload(connection, combination);

        if (failure != null)
            return (null, failure);

        return (new Answers(Scan(connection), IndexLookup(connection), Aggregate(connection)), null);
    }

    #endregion

    #region The workload

    /// <summary>
    /// The same rows by whatever route the configuration allows: a configuration without transactions
    /// writes the committed row directly and never writes the rolled-back one, so the final state - and
    /// therefore every answer - is identical everywhere.
    /// </summary>
    private static string? RunWorkload(WitDbConnection connection, Combination combination)
    {
        try
        {
            Execute(connection, "CREATE TABLE Matrix (Id BIGINT PRIMARY KEY, Name VARCHAR(50), Amount DECIMAL(9,2))");
            Execute(connection, "CREATE INDEX IX_Matrix_Name ON Matrix (Name)");

            for (var i = 1; i <= 8; i++)
                Execute(connection, $"INSERT INTO Matrix (Id, Name, Amount) VALUES ({i}, 'row{i}', {i}.25)");

            Execute(connection, "UPDATE Matrix SET Name = 'updated' WHERE Id = 2");
            Execute(connection, "DELETE FROM Matrix WHERE Id = 5");

            if (combination.Settings.Contains("Transactions=false"))
            {
                Execute(connection, "INSERT INTO Matrix (Id, Name, Amount) VALUES (10, 'committed', 10.00)");
            }
            else
            {
                using (var transaction = (WitDbTransaction)connection.BeginTransaction())
                {
                    Execute(connection, "INSERT INTO Matrix (Id, Name, Amount) VALUES (10, 'committed', 10.00)", transaction);
                    transaction.Commit();
                }

                using var rolledBack = (WitDbTransaction)connection.BeginTransaction();
                Execute(connection, "INSERT INTO Matrix (Id, Name, Amount) VALUES (11, 'rolled back', 11.00)", rolledBack);
                rolledBack.Rollback();
            }

            return null;
        }
        catch (Exception e)
        {
            return $"{e.GetType().Name}: {Short(e)}";
        }
    }

    /// <summary>
    /// Every row, read back rather than counted. <c>COUNT(*)</c> is answered from a cached counter on this
    /// engine and has disagreed with the rows before - phase 4 spent a session on a false report built on
    /// exactly that.
    /// </summary>
    private static string Scan(WitDbConnection connection)
    {
        return Query(connection, "SELECT Id, Name, Amount FROM Matrix ORDER BY Id",
            reader => $"{reader.GetInt64(0)}:{reader.GetString(1)}:{reader.GetDecimal(2):0.00}");
    }

    private static string IndexLookup(WitDbConnection connection)
    {
        return Query(connection, "SELECT Id, Name FROM Matrix WHERE Name = 'row3'",
            reader => $"{reader.GetInt64(0)}:{reader.GetString(1)}");
    }

    private static string Aggregate(WitDbConnection connection)
    {
        return Query(connection, "SELECT COUNT(*), SUM(Amount) FROM Matrix",
            reader => $"{reader.GetInt64(0)}:{reader.GetDecimal(1):0.00}");
    }

    private static string Query(WitDbConnection connection, string sql, Func<System.Data.Common.DbDataReader, string> row)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        using var reader = command.ExecuteReader();
        var builder = new StringBuilder();

        while (reader.Read())
        {
            if (builder.Length > 0)
                builder.Append('|');

            builder.Append(row(reader));
        }

        return builder.ToString();
    }

    private static void Execute(WitDbConnection connection, string sql, WitDbTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.ExecuteNonQuery();
    }

    #endregion

    #region Helpers

    private string NewDirectory()
    {
        var directory = Path.Combine(m_root, $"case{Interlocked.Increment(ref m_sequence):D4}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string Compose(string dataSource, string settings)
    {
        return string.IsNullOrEmpty(settings)
            ? $"Data Source={dataSource}"
            : $"Data Source={dataSource};{settings}";
    }

    private static string Join(params string[] parts)
    {
        return string.Join(';', parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    private static string Short(Exception exception)
    {
        var line = exception.Message.Split('\n')[0].Trim();
        return line.Length > 200 ? line[..200] : line;
    }

    #endregion
}
