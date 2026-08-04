using System.Diagnostics;
using System.Text;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Transactions;

namespace OutWit.Database.Core.Tests.Mvcc;

/// <summary>
/// What does an MVCC commit cost, and what does the cost depend on?
/// </summary>
/// <remarks>
/// <para>
/// Phase 11 measured, without chasing it, that four writers doing batches of 1,000 rows in a
/// transaction took <b>181 s</b> against autocommit's <b>61 s</b> for the same 100,000 rows - a
/// transaction being three times slower than no transaction at all is the wrong way round, and it was
/// the largest unexplained number the phase produced.
/// </para>
/// <para>
/// <b>This asks the question that separates the two candidate explanations</b> - contention between
/// writers, or a commit whose cost is not proportional to what the transaction wrote. It runs a
/// <b>single writer</b>, so there is no contention at all, and commits a transaction of the <b>same ten
/// rows</b> against databases of growing size. A commit that costs the same everywhere depends on the
/// transaction; a commit that grows with the database depends on the database, and then a hundred
/// commits over a growing store are quadratic.
/// </para>
/// <para>
/// <b>Timing discipline, because one run has lied here before.</b> Every size is measured three times
/// and the median is reported, the sizes are measured in one pass so that machine state is shared, and
/// the report prints every sample rather than the summary alone.
/// </para>
/// </remarks>
[TestFixture]
[Category("Modularity")]
public class CommitCostProbeTests
{
    #region Constants

    /// <summary>Rows already committed in the database before the measured transaction runs.</summary>
    private static readonly int[] SIZES = [1000, 2000, 4000, 8000];

    /// <summary>Rows the measured transaction writes - the same at every size, which is the point.</summary>
    private const int TRANSACTION_ROWS = 10;

    private const int REPEATS = 3;

    #endregion

    #region Fields

    private string m_root = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witdb_commitcost_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
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

    #region The probe

    [Test]
    public void WhatACommitCostsDependsOnTheDatabaseNotTheTransactionTest()
    {
        var medians = new Dictionary<int, double>();
        var report = new StringBuilder();

        foreach (var size in SIZES)
        {
            var samples = new List<double>();

            for (var repeat = 0; repeat < REPEATS; repeat++)
                samples.Add(MeasureCommit(size));

            samples.Sort();
            medians[size] = samples[samples.Count / 2];

            report.AppendLine(
                $"COMMIT COST  {size,6} rows in the store -> {medians[size],8:0.00} ms per commit of " +
                $"{TRANSACTION_ROWS} rows   [{string.Join(", ", samples.Select(s => $"{s:0.00}"))}]");
        }

        var smallest = medians[SIZES[0]];
        var largest = medians[SIZES[^1]];
        var growth = largest / Math.Max(0.001, smallest);

        report.AppendLine(
            $"COMMIT COST  {SIZES[^1]}/{SIZES[0]} rows = {SIZES[^1] / SIZES[0]}x the data, " +
            $"{growth:0.0}x the commit");

        TestContext.Out.Write(report.ToString());

        // This pinned the defect and now asserts the fix. Before it, CommitTransaction scanned the
        // whole store to find the versions the transaction had just written, so committing ten rows
        // cost 2.80 ms against 1,000 rows and 6.96 ms against 8,000 - eight times the data for two and
        // a half times the commit. The store remembers what each transaction wrote now: 2.08 ms and
        // 2.14 ms, which is 1.0x.
        //
        // The bound is deliberately loose. What is being asserted is that the commit no longer depends
        // on the size of the database, and a timing ratio on a shared build machine needs room; a
        // return of the scan shows up as 2.5x, not as 1.6x.
        Assert.That(growth, Is.LessThan(1.6),
            $"committing {TRANSACTION_ROWS} rows costs {growth:0.0}x more on {SIZES[^1] / SIZES[0]}x the " +
            "data - the commit is reading something proportional to the database again");
    }

    #endregion

    #region Tools

    /// <summary>
    /// Builds a database of <paramref name="size"/> committed rows, then times one transaction that
    /// writes <see cref="TRANSACTION_ROWS"/> rows and commits.
    /// </summary>
    private double MeasureCommit(int size)
    {
        var path = Path.Combine(m_root, $"commit_{size}_{Guid.NewGuid():N}.witdb");

        using var store = new StoreBTree(path);
        using var transactional = new MvccTransactionalStore(store, ownsStore: false);

        // The bulk, written outside the measurement and committed, so what is being timed is one
        // commit against a store of a known size rather than the filling of it.
        using (var seeding = transactional.BeginTransaction())
        {
            for (var i = 0; i < size; i++)
                seeding.Put(Key($"seed{i:D7}"), Value(i));

            seeding.Commit();
        }

        using var measured = transactional.BeginTransaction();

        for (var i = 0; i < TRANSACTION_ROWS; i++)
            measured.Put(Key($"measured{i:D3}"), Value(i));

        var stopwatch = Stopwatch.StartNew();
        measured.Commit();
        stopwatch.Stop();

        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private static byte[] Key(string key) => System.Text.Encoding.UTF8.GetBytes(key);

    private static byte[] Value(int i) => System.Text.Encoding.UTF8.GetBytes($"value-{i}");

    #endregion
}
