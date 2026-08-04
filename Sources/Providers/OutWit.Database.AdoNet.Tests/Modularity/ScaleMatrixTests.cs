using System.Text;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.AdoNet.Tests.Modularity;

/// <summary>
/// Phase 11 follow-up - the combinations at a size that reaches the structures. Until this fixture,
/// "works" meant "works on eight rows": no combination in the matrix had ever caused a page split, an
/// overflow page or an LSM compaction.
/// </summary>
/// <remarks>
/// <para>
/// Eight rows fit in one leaf. Everything that makes a storage engine interesting - splitting a full
/// node, spilling a value too large to sit inline, merging sorted runs - was therefore untested across
/// the configuration matrix, and phase 4 recorded exactly this trap from the other side: the compaction
/// fixtures passed with the fix reverted because at the default block size a table fits in one block and
/// the merge never touches a file.
/// </para>
/// <para>
/// <b>So the workload is not the assertion.</b> Writing two thousand rows proves nothing on its own;
/// what proves something is <b>evidence that the structure was reached</b>, read off the files
/// afterwards: how many pages the B+Tree database occupies, that its inline limit is smaller than the
/// payload written (so the payload cannot be anywhere but an overflow chain), and how many SSTables an
/// LSM database left behind and whether any were merged away.
/// </para>
/// <para>
/// <b>The control is the small database.</b> The same evidence is taken from an eight-row database of
/// the same configuration, and it must NOT show those signs. Without it, "the file has many pages" is a
/// statement about the engine's appetite rather than about the workload.
/// </para>
/// </remarks>
[TestFixture]
[Category("Modularity")]
public class ScaleMatrixTests
{
    #region Constants

    /// <summary>Enough rows to split a 4 KB leaf many times over.</summary>
    private const int ROWS = 2000;

    /// <summary>Every hundredth row carries this many characters, which cannot sit inline.</summary>
    private const int PAYLOAD = 4000;

    /// <summary>The eight-row control, so both sides run the identical evidence check.</summary>
    private const int SMALL_ROWS = 8;

    #endregion

    #region Types

    public sealed record Combination(string Label, string Settings, bool IsLsm)
    {
        public override string ToString() => Label;
    }

    /// <param name="Pages">Pages in the B+Tree database file, or 0 for an LSM one.</param>
    /// <param name="SsTables">SSTable files an LSM database left behind, or 0 for a B+Tree one.</param>
    /// <param name="HighestSsTableId">
    /// The largest SSTable file number. Higher than the number of files means files were written and
    /// then merged away - which is what a compaction looks like from outside the store.
    /// </param>
    private sealed record Evidence(long Pages, int SsTables, int HighestSsTableId)
    {
        public override string ToString() =>
            $"pages={Pages} sstables={SsTables} highestId={HighestSsTableId}";
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
        m_root = Path.Combine(Path.GetTempPath(), $"witdb_scale_{Guid.NewGuid():N}");
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

    #region The combinations

    private static IEnumerable<Combination> Combinations()
    {
        yield return new Combination("btree", "", IsLsm: false);
        yield return new Combination("btree encrypted", "Encryption=aes-gcm;Password=scale-secret;FastEncryption=true", IsLsm: false);
        yield return new Combination("btree locks", "MVCC=false", IsLsm: false);

        // Small memtable and a low trigger, so the volume below reaches a compaction rather than
        // merely filling one table. Left at the defaults, 2,000 rows would sit in the MemTable.
        yield return new Combination("lsm", "Store=lsm;MemTableSize=65536;CompactionTrigger=2", IsLsm: true);
        yield return new Combination("lsm locks", "Store=lsm;MVCC=false;MemTableSize=65536;CompactionTrigger=2", IsLsm: true);
    }

    #endregion

    #region The probe

    [Test]
    [TestCaseSource(nameof(Combinations))]
    public void ACombinationSurvivesRealVolumeTest(Combination combination)
    {
        var dataSource = NewDataSource();

        Write(dataSource, combination, ROWS);

        var evidence = Read(dataSource, combination);
        TestContext.Out.WriteLine($"SCALE {combination.Label,-16} {ROWS} rows -> {evidence}");

        // Reopened rather than only written: a structure that is built and cannot be read back is the
        // failure this is looking for, and it only shows on the second open.
        using var connection = new WitDbConnection(Compose(dataSource, combination.Settings));
        connection.Open();

        Assert.Multiple(() =>
        {
            Assert.That(CountByScanning(connection), Is.EqualTo(ROWS),
                $"{combination.Label}: not every row came back after a reopen");

            Assert.That(PayloadOf(connection, 1000), Has.Length.EqualTo(PAYLOAD),
                $"{combination.Label}: the large value did not survive - it is longer than anything that " +
                "can sit inline, so what this checks is the overflow chain");

            Assert.That(PayloadOf(connection, 1000), Is.EqualTo(Payload(1000)),
                $"{combination.Label}: the large value came back changed");

            Assert.That(IndexLookup(connection, "name-1500"), Is.EqualTo("1500"),
                $"{combination.Label}: the secondary index did not find a row at this volume");
        });
    }

    /// <summary>
    /// The evidence that the volume above actually reached the structures - and the eight-row control
    /// that says the evidence discriminates.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(Combinations))]
    public void TheVolumeReachesTheStructuresTest(Combination combination)
    {
        var large = NewDataSource();
        Write(large, combination, ROWS);
        var atVolume = Read(large, combination);

        var small = NewDataSource();
        Write(small, combination, SMALL_ROWS);
        var atEight = Read(small, combination);

        TestContext.Out.WriteLine(
            $"SCALE {combination.Label,-16} {ROWS} rows [{atVolume}]   {SMALL_ROWS} rows [{atEight}]");

        if (combination.IsLsm)
        {
            Assert.Multiple(() =>
            {
                Assert.That(atVolume.HighestSsTableId, Is.GreaterThan(atVolume.SsTables),
                    $"{combination.Label}: every SSTable ever written is still on disk, so nothing was " +
                    "ever compacted and this fixture is not measuring what it claims");

                Assert.That(atEight.HighestSsTableId, Is.LessThanOrEqualTo(1),
                    $"{combination.Label}: eight rows produced more than one SSTable, so the evidence " +
                    "does not distinguish volume from the engine's ordinary behaviour");
            });

            return;
        }

        Assert.Multiple(() =>
        {
            // A 4 KB page holds a few dozen of these rows, so two thousand cannot be one leaf.
            Assert.That(atVolume.Pages, Is.GreaterThan(50),
                $"{combination.Label}: {ROWS} rows fitted in {atVolume.Pages} pages, which means no split " +
                "happened and this fixture is testing the same thing the eight-row matrix does");

            Assert.That(atEight.Pages, Is.LessThan(atVolume.Pages / 10),
                $"{combination.Label}: eight rows occupy {atEight.Pages} pages against {atVolume.Pages} - " +
                "the page count is not a measure of the workload");
        });
    }

    /// <summary>
    /// Control: the payload written above genuinely cannot sit inline, read from the store itself
    /// rather than assumed. If the inline limit were larger than the payload, the overflow assertion in
    /// the probe would be checking nothing at all.
    /// </summary>
    /// <remarks>
    /// Read through <see cref="StoreBTree"/> directly, which is the only place the limit is visible -
    /// and only for an unencrypted database, since opening an encrypted one at the storage layer needs
    /// the key. That is why the combination list carries an unencrypted B+Tree case.
    /// </remarks>
    [Test]
    public void ControlTheLargeValueCannotSitInlineTest()
    {
        var dataSource = NewDataSource();
        var btree = Combinations().First(c => c.Label == "btree");

        Write(dataSource, btree, SMALL_ROWS);

        using var store = new StoreBTree(dataSource);

        TestContext.Out.WriteLine($"SCALE inline limit = {store.MaxInlineValueSize} bytes, payload = {PAYLOAD}");

        Assert.That(store.MaxInlineValueSize, Is.LessThan(PAYLOAD),
            "the payload this fixture writes fits inline, so nothing it does reaches an overflow page");
    }

    #endregion

    #region The workload

    private static void Write(string dataSource, Combination combination, int rows)
    {
        using var connection = new WitDbConnection(Compose(dataSource, combination.Settings));
        connection.Open();

        Execute(connection, "CREATE TABLE Big (Id BIGINT PRIMARY KEY, Name VARCHAR(50), Payload VARCHAR(4000))");
        Execute(connection, "CREATE INDEX IX_Big_Name ON Big (Name)");

        // One transaction per batch rather than per row: this is about volume, and an autocommit per
        // row would spend the fixture's time in the transaction layer instead of in the tree.
        using (var transaction = (WitDbTransaction)connection.BeginTransaction())
        {
            for (var i = 1; i <= rows; i++)
            {
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO Big (Id, Name, Payload) VALUES (@id, @name, @payload)";
                command.Transaction = transaction;

                command.Parameters.Add(new WitDbParameter("@id", i));
                command.Parameters.Add(new WitDbParameter("@name", $"name-{i}"));
                command.Parameters.Add(new WitDbParameter("@payload", Payload(i)));

                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    /// <summary>Every hundredth row is too large to sit inline; the rest are ordinary.</summary>
    private static string Payload(int i) => i % 100 == 0 ? new string((char)('a' + i % 26), PAYLOAD) : $"payload-{i}";

    private static int CountByScanning(WitDbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM Big ORDER BY Id";

        using var reader = command.ExecuteReader();
        var count = 0;

        while (reader.Read())
            count++;

        return count;
    }

    private static string PayloadOf(WitDbConnection connection, long id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Payload FROM Big WHERE Id = {id}";

        using var reader = command.ExecuteReader();
        return reader.Read() ? reader.GetString(0) : "";
    }

    private static string IndexLookup(WitDbConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Id FROM Big WHERE Name = '{name}'";

        using var reader = command.ExecuteReader();
        var builder = new StringBuilder();

        while (reader.Read())
            builder.Append(reader.GetInt64(0));

        return builder.ToString();
    }

    #endregion

    #region The evidence

    /// <summary>
    /// What the files say afterwards. Taken from the closed database on purpose: this is the shape the
    /// engine left behind, not a number it reports about itself.
    /// </summary>
    private static Evidence Read(string dataSource, Combination combination)
    {
        if (!combination.IsLsm)
        {
            var length = new FileInfo(dataSource).Length;
            return new Evidence(length / 4096, 0, 0);
        }

        var files = Directory.GetFiles(dataSource, "sst_*.sst");
        var highest = 0;

        foreach (var file in files)
        {
            var digits = new string(Path.GetFileNameWithoutExtension(file).Where(char.IsDigit).ToArray());

            if (int.TryParse(digits, out var id) && id > highest)
                highest = id;
        }

        return new Evidence(0, files.Length, highest);
    }

    #endregion

    #region Helpers

    private string NewDataSource()
    {
        var directory = Path.Combine(m_root, $"case{Interlocked.Increment(ref m_sequence):D3}");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "scale.witdb");
    }

    private static string Compose(string dataSource, string settings)
    {
        return string.IsNullOrEmpty(settings)
            ? $"Data Source={dataSource}"
            : $"Data Source={dataSource};{settings}";
    }

    private static void Execute(WitDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    #endregion
}
