using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace OutWit.Database.AdoNet.Tests.Modularity;

/// <summary>
/// Phase 11 instrument A - for every connection-string keyword, does it reach the engine at all?
/// </summary>
/// <remarks>
/// <para>
/// Phase 10 found that <b>every</b> LSM connection-string parameter was inert - accepted by the parser,
/// documented by the builder, and dropped before it reached a store - because the builder and the
/// provider registry are two construction routes and only one of them read them. That is a structural
/// defect, not an LSM one, and nothing has ever checked whether <c>Cache</c>, <c>Journal</c>,
/// <c>Parallel Mode</c>, <c>Page Size</c> and the rest fare any better.
/// </para>
/// <para>
/// This census asks the question generically instead of one bespoke probe at a time. For each keyword it
/// builds the database <b>through the real connection-string path</b> twice with the baseline value and
/// once with a different value, takes a structural fingerprint of the built object graph by reflection,
/// and compares. Two configurations that differ in one keyword and produce the same engine mean the
/// keyword reached nothing.
/// </para>
/// <para>
/// <b>The controls are built in, in both directions</b>, because a fingerprint can fail either way:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Positive controls</b> - keywords proved to reach the engine (<c>Store</c>, <c>MemTableSize</c>,
/// <c>MVCC</c>). If one of these reports INERT the fingerprint is blind, and no INERT verdict from this
/// run may be believed.
/// </item>
/// <item>
/// <b>A negative control</b> - a keyword that exists nowhere in the product. If it reports REACHES the
/// fingerprint is picking up run-to-run noise, and no REACHES verdict may be believed.
/// </item>
/// <item>
/// <b>Per-knob noise calibration</b> - the baseline side is built twice, in two directories, and every
/// path whose value differs between those two builds is excluded before the variant is compared. This is
/// what keeps temporary paths, handles and timings out of the verdict; the negative control is what
/// proves it worked.
/// </item>
/// </list>
/// <para>
/// <b>What the census cannot say</b> is whether an option that reaches the engine is then honoured. The
/// isolation level is the standing example: it is stored on the transactional store, so it reaches, and
/// it is applied by nothing. Reaching is necessary and not sufficient, and the behavioural half is the
/// combination matrix that runs a workload through every configuration.
/// </para>
/// </remarks>
[TestFixture]
[Category("Modularity")]
public class ConfigurationCensusTests
{
    #region Types

    private enum Role
    {
        /// <summary>An unproved keyword - what the census exists to classify.</summary>
        Subject,

        /// <summary>Known to reach the engine. INERT here means the instrument is blind.</summary>
        PositiveControl,

        /// <summary>Reaches nothing by construction. REACHES here means the instrument is noisy.</summary>
        NegativeControl
    }

    private enum Verdict
    {
        /// <summary>The built engine differs structurally - the keyword arrives somewhere.</summary>
        Reaches,

        /// <summary>The built engine is identical - the keyword was accepted and dropped.</summary>
        Inert,

        /// <summary>The only difference is a copy of the keyword's own text, kept as text.</summary>
        EchoOnly,

        /// <summary>One of the two sides refused to open.</summary>
        Refused
    }

    /// <summary>What one side of a knob produced: the engine's shape, and what the workload complained about.</summary>
    private sealed record Side(SortedDictionary<string, string> Values, IReadOnlyList<string> WorkloadErrors);

    /// <summary>
    /// One keyword under census: two connection strings that differ in exactly one setting.
    /// </summary>
    /// <param name="Name">What is reported.</param>
    /// <param name="Common">Keywords both sides carry, e.g. <c>Store=lsm</c> for an LSM-only setting.</param>
    /// <param name="Baseline">The setting's baseline value, as connection-string text.</param>
    /// <param name="Variant">The setting's other value.</param>
    /// <param name="Role">Subject, or a control in one of the two directions.</param>
    /// <param name="PreCreate">Create the database with a default connection first (for Mode/Read Only).</param>
    /// <param name="Workload">Run the small workload before fingerprinting.</param>
    private sealed record Knob(
        string Name,
        string Common,
        string Baseline,
        string Variant,
        Role Role = Role.Subject,
        bool PreCreate = false,
        bool Workload = true);

    private sealed record Diff(string Path, string Before, string After)
    {
        /// <summary>
        /// True when the difference is a type, a number, a flag or an enum - the engine really is built
        /// differently. A string that merely repeats the keyword's own text is not that.
        /// </summary>
        public bool IsStructural(string baselineText, string variantText)
        {
            if (!Before.StartsWith("s:") && !After.StartsWith("s:"))
                return true;

            return !Echoes(Before, baselineText, variantText) && !Echoes(After, baselineText, variantText);
        }

        private static bool Echoes(string value, string baselineText, string variantText)
        {
            foreach (var token in Tokens(baselineText).Concat(Tokens(variantText)))
            {
                if (value.Contains(token, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static IEnumerable<string> Tokens(string connectionStringFragment)
        {
            foreach (var part in connectionStringFragment.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = part.IndexOf('=');
                var token = equals < 0 ? part : part[(equals + 1)..];
                token = token.Trim();

                if (token.Length > 0)
                    yield return token;
            }
        }
    }

    private sealed record Result(Knob Knob, Verdict Verdict, IReadOnlyList<Diff> Structural, int NoiseCount, string? Note);

    #endregion

    #region Constants

    /// <summary>How deep the reflection walk goes before it records a type name and stops.</summary>
    private const int MAX_DEPTH = 9;

    /// <summary>How many elements of an ordered collection are walked. Enough to see a shard array.</summary>
    private const int MAX_ELEMENTS = 8;

    /// <summary>Types the walk records by name and does not open - locks, handles, threads, streams.</summary>
    private static readonly Type[] OPAQUE =
    [
        typeof(Delegate), typeof(Thread), typeof(Task), typeof(SemaphoreSlim), typeof(ReaderWriterLockSlim),
        typeof(CancellationTokenSource), typeof(Stream), typeof(System.Runtime.InteropServices.SafeHandle),
        typeof(WaitHandle), typeof(System.Data.Common.DbConnectionStringBuilder)
    ];

    #endregion

    #region Fields

    private string m_root = null!;
    private int m_sequence;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witdb_census_{Guid.NewGuid():N}");
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

    #region The census

    /// <summary>
    /// Every keyword the connection-string builder documents, plus the pass-through settings the store
    /// providers read. Ordered as the builder declares them so the two can be compared by eye.
    /// </summary>
    private static readonly Knob[] KNOBS =
    [
        // ---- controls -------------------------------------------------------------------------
        new("Store (control)", "", "Store=btree", "Store=lsm", Role.PositiveControl),
        new("MVCC (control)", "", "MVCC=true", "MVCC=false", Role.PositiveControl),
        new("MemTableSize (control)", "Store=lsm", "MemTableSize=4194304", "MemTableSize=1024", Role.PositiveControl),
        new("Nonexistent Keyword (control)", "", "", "Nonexistent Keyword=42", Role.NegativeControl),

        // ---- provider selection ---------------------------------------------------------------
        new("Store=inmemory", "", "Store=btree", "Store=inmemory"),
        new("Cache", "", "Cache=clock", "Cache=lru"),
        // Refused since 11.3.0: with MVCC nothing would use it. The census keeps the case so the refusal
        // is measured rather than assumed.
        new("Journal (MVCC on)", "", "Journal=rollback", "Journal=wal"),
        new("Journal (MVCC off)", "MVCC=false", "Journal=rollback", "Journal=wal"),
        new("Journal present or not", "MVCC=false", "", "Journal=wal"),
        new("Encryption", "", "", "Encryption=aes-gcm;Password=census-secret"),
        new("Password", "", "", "Password=census-secret"),
        new("User", "Password=census-secret", "", "User=census-user"),

        // ---- transactions ---------------------------------------------------------------------
        new("Transactions", "", "Transactions=true", "Transactions=false"),
        new("Isolation Level", "", "Isolation Level=ReadCommitted", "Isolation Level=Serializable"),
        new("Isolation Level (MVCC off)", "MVCC=false", "Isolation Level=ReadCommitted", "Isolation Level=Serializable"),
        new("Synchronous Commit", "", "Synchronous Commit=true", "Synchronous Commit=false"),

        // ---- parallelism ----------------------------------------------------------------------
        new("Parallel Mode on or off", "", "Parallel Mode=None", "Parallel Mode=Auto"),
        new("Parallel Mode Auto vs Buffered", "", "Parallel Mode=Auto", "Parallel Mode=Buffered"),
        new("Parallel Mode Auto vs Latched", "", "Parallel Mode=Auto", "Parallel Mode=Latched"),
        new("Parallel Mode Auto vs Optimistic", "", "Parallel Mode=Auto", "Parallel Mode=Optimistic"),
        new("Parallel Mode (LSM) Auto vs Buffered", "Store=lsm", "Parallel Mode=Auto", "Parallel Mode=Buffered"),
        new("Max Writers (BTree)", "Parallel Mode=Buffered", "Max Writers=2", "Max Writers=7"),
        new("Max Writers (LSM)", "Store=lsm;Parallel Mode=Buffered", "Max Writers=2", "Max Writers=7"),

        // ---- storage --------------------------------------------------------------------------
        new("PageSize", "", "PageSize=4096", "PageSize=16384"),
        new("CacheSize", "", "CacheSize=100", "CacheSize=900"),

        // The same two keywords with the store named explicitly. Store=btree is the default, so these
        // ask for exactly the same engine as the two above - and if they disagree, what decides whether
        // a setting arrives is not the setting.
        new("PageSize (Store named)", "Store=btree", "PageSize=4096", "PageSize=16384"),
        new("CacheSize (Store named)", "Store=btree", "CacheSize=100", "CacheSize=900"),
        new("SyncWrites (Store unnamed)", "", "SyncWrites=true", "SyncWrites=false"),
        new("FileLocking", "", "FileLocking=true", "FileLocking=false"),
        new("Mode=Memory", "", "", "Mode=Memory"),

        // ---- session --------------------------------------------------------------------------
        new("Read Only", "", "Read Only=false", "Read Only=true", PreCreate: true, Workload: false),
        new("Mode=ReadOnly", "", "", "Mode=ReadOnly", PreCreate: true, Workload: false),
        new("Mode=ReadWrite", "", "", "Mode=ReadWrite", PreCreate: true),

        // ---- LSM pass-through -----------------------------------------------------------------
        new("SyncWrites", "Store=lsm", "SyncWrites=true", "SyncWrites=false"),
        new("EnableWal", "Store=lsm", "EnableWal=true", "EnableWal=false"),
        new("BlockSize", "Store=lsm", "BlockSize=4096", "BlockSize=16384"),
        new("CompactionTrigger", "Store=lsm", "CompactionTrigger=4", "CompactionTrigger=9"),
        new("EnableBlockCache", "Store=lsm", "EnableBlockCache=true", "EnableBlockCache=false"),
        new("BlockCacheSize", "Store=lsm", "BlockCacheSize=67108864", "BlockCacheSize=1048576"),
        new("BackgroundCompaction", "Store=lsm", "BackgroundCompaction=true", "BackgroundCompaction=false")
    ];

    /// <summary>
    /// Probe: the census. Reported in full - the work order for this phase comes out of it.
    /// </summary>
    [Test]
    public void ProbeTheConfigurationCensusTest()
    {
        var results = KNOBS.Select(Census).ToList();

        foreach (var result in results)
        {
            var detail = result.Verdict switch
            {
                Verdict.Reaches => $"{result.Structural.Count} structural difference(s): " +
                                   string.Join(", ", result.Structural.Take(3).Select(d => d.Path)) +
                                   (result.Note == null ? "" : $" | workload: {result.Note}"),
                Verdict.Refused => result.Note ?? "",
                _ => result.Note ?? ""
            };

            TestContext.Out.WriteLine(
                $"{result.Verdict.ToString().ToUpperInvariant(),-8} {result.Knob.Name,-38} " +
                $"[{result.Knob.Baseline} -> {result.Knob.Variant}] noise={result.NoiseCount}  {detail}");
        }

        TestContext.Out.WriteLine("");

        foreach (var group in results.GroupBy(r => r.Verdict))
            TestContext.Out.WriteLine($"TOTAL   {group.Key}: {group.Count()}");

        AssertControlsHeld(results);
    }

    /// <summary>
    /// The instrument's own control, asserted rather than printed: a blind or a noisy fingerprint
    /// invalidates every verdict in the run, so it fails the test rather than colouring a report.
    /// </summary>
    private static void AssertControlsHeld(IReadOnlyList<Result> results)
    {
        var blind = results
            .Where(r => r.Knob.Role == Role.PositiveControl && r.Verdict != Verdict.Reaches)
            .Select(r => $"{r.Knob.Name} reported {r.Verdict}")
            .ToList();

        var noisy = results
            .Where(r => r.Knob.Role == Role.NegativeControl && r.Verdict != Verdict.Inert)
            .Select(r => $"{r.Knob.Name} reported {r.Verdict} " +
                         $"({string.Join(", ", r.Structural.Take(5).Select(d => $"{d.Path}: {d.Before} -> {d.After}"))})")
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(blind, Is.Empty,
                "A keyword proved to reach the engine reported otherwise - the fingerprint is blind, " +
                "so no INERT verdict in this run may be believed.");

            Assert.That(noisy, Is.Empty,
                "A keyword that reaches nothing reported a difference - the fingerprint is picking up " +
                "run-to-run noise, so no REACHES verdict in this run may be believed.");
        });
    }

    private Result Census(Knob knob)
    {
        Side baselineSide;
        Side baselineRepeat;
        Side variantSide;

        try
        {
            baselineSide = Build(knob, knob.Baseline);
            baselineRepeat = Build(knob, knob.Baseline);
        }
        catch (Exception e)
        {
            return new Result(knob, Verdict.Refused, [], 0, $"baseline refused at open: {Short(e)}");
        }

        try
        {
            variantSide = Build(knob, knob.Variant);
        }
        catch (Exception e)
        {
            return new Result(knob, Verdict.Refused, [], 0, $"variant refused at open: {Short(e)}");
        }

        var baselineOne = baselineSide.Values;
        var baselineTwo = baselineRepeat.Values;
        var variant = variantSide.Values;

        // A statement the workload could not run is not a fingerprint difference - the engine opened -
        // but it is the most interesting thing about that side, so it is carried into the verdict's note.
        var workloadNote = variantSide.WorkloadErrors.Except(baselineSide.WorkloadErrors).FirstOrDefault();

        // Self-calibration: anything that already differs between two builds of the SAME configuration is
        // noise - a temporary path, a handle, a timing - and cannot be evidence about the keyword.
        var noise = new HashSet<string>(
            baselineOne.Keys.Union(baselineTwo.Keys)
                .Where(k => !baselineOne.TryGetValue(k, out var a) ||
                            !baselineTwo.TryGetValue(k, out var b) ||
                            a != b));

        var diffs = new List<Diff>();

        foreach (var key in baselineOne.Keys.Union(variant.Keys))
        {
            if (noise.Contains(key))
                continue;

            baselineOne.TryGetValue(key, out var before);
            variant.TryGetValue(key, out var after);

            if (before != after)
                diffs.Add(new Diff(key, before ?? "<absent>", after ?? "<absent>"));
        }

        var structural = diffs.Where(d => d.IsStructural(knob.Baseline, knob.Variant)).ToList();
        var suffix = workloadNote == null ? "" : $" | workload: {workloadNote}";

        if (structural.Count > 0)
            return new Result(knob, Verdict.Reaches, structural, noise.Count, workloadNote);

        if (diffs.Count > 0)
            return new Result(knob, Verdict.EchoOnly, [], noise.Count,
                $"only the keyword's own text is kept: {diffs[0].Path}{suffix}");

        return new Result(knob, Verdict.Inert, [], noise.Count, $"the built engine is identical{suffix}");
    }

    #endregion

    #region Building

    /// <summary>
    /// Builds one side of a knob through the real connection-string path and fingerprints the result.
    /// </summary>
    private Side Build(Knob knob, string side)
    {
        var directory = Path.Combine(m_root, $"case{Interlocked.Increment(ref m_sequence):D3}");
        Directory.CreateDirectory(directory);

        var dataSource = Path.Combine(directory, "census.witdb");
        var connectionString = Compose(dataSource, knob.Common, side);

        if (knob.PreCreate)
        {
            using var seed = new WitDbConnection(Compose(dataSource, knob.Common, ""));
            seed.Open();
            RunWorkload(seed);
        }

        // Anything thrown out of Open is a refusal to build this configuration at all, and it propagates:
        // it is a different verdict from a configuration that opens and then cannot run a statement.
        using var connection = new WitDbConnection(connectionString);
        connection.Open();

        var workloadErrors = knob.Workload && !knob.PreCreate
            ? RunWorkload(connection)
            : [];

        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);

        Walk(Field(connection, "m_database"), "database", MAX_DEPTH, values, seen);
        Walk(Field(connection, "m_engine"), "engine", MAX_DEPTH, values, seen);

        return new Side(values, workloadErrors);
    }

    private static string Compose(string dataSource, string common, string side)
    {
        var builder = new StringBuilder($"Data Source={dataSource}");

        if (!string.IsNullOrEmpty(common))
            builder.Append(';').Append(common);

        if (!string.IsNullOrEmpty(side))
            builder.Append(';').Append(side);

        return builder.ToString();
    }

    /// <summary>
    /// The smallest workload that makes the engine build the parts a fresh open leaves unbuilt: a table,
    /// rows, a secondary index, a read, a write inside an explicit transaction, and a rollback.
    /// </summary>
    private static IReadOnlyList<string> RunWorkload(WitDbConnection connection)
    {
        var errors = new List<string>();

        Try(errors, "create", () =>
        {
            Execute(connection, "CREATE TABLE Census (Id BIGINT PRIMARY KEY, Name VARCHAR(50), Amount DECIMAL(9,2))");
            Execute(connection, "CREATE INDEX IX_Census_Name ON Census (Name)");
        });

        Try(errors, "write", () =>
        {
            for (var i = 1; i <= 5; i++)
                Execute(connection, $"INSERT INTO Census (Id, Name, Amount) VALUES ({i}, 'row{i}', {i}.25)");

            Execute(connection, "UPDATE Census SET Name = 'updated' WHERE Id = 2");
            Execute(connection, "DELETE FROM Census WHERE Id = 5");
        });

        Try(errors, "commit", () =>
        {
            using var transaction = (WitDbTransaction)connection.BeginTransaction();
            Execute(connection, "INSERT INTO Census (Id, Name, Amount) VALUES (10, 'committed', 10.00)", transaction);
            transaction.Commit();
        });

        Try(errors, "rollback", () =>
        {
            using var transaction = (WitDbTransaction)connection.BeginTransaction();
            Execute(connection, "INSERT INTO Census (Id, Name, Amount) VALUES (11, 'rolled back', 11.00)", transaction);
            transaction.Rollback();
        });

        Try(errors, "read", () =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Name FROM Census WHERE Name = 'row1'";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                _ = reader.GetValue(0);
            }
        });

        return errors;
    }

    /// <summary>
    /// Runs one step of the workload and records rather than throws. A configuration that opens and then
    /// refuses a statement has to be fingerprinted like any other - refusing to open is the other verdict.
    /// </summary>
    private static void Try(List<string> errors, string step, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            errors.Add($"{step}: {Short(e)}");
        }
    }

    private static void Execute(WitDbConnection connection, string sql, WitDbTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.ExecuteNonQuery();
    }

    #endregion

    #region Fingerprinting

    /// <summary>
    /// Records the shape of an object graph: the runtime type at every reachable field, and the value of
    /// every scalar. Fields only, never properties - a property can compute, allocate or lock, and this
    /// has to be able to look at a live engine without changing it.
    /// </summary>
    private static void Walk(object? node, string path, int depth,
        SortedDictionary<string, string> into, HashSet<object> seen)
    {
        if (node == null)
        {
            into[path] = "null";
            return;
        }

        var type = node.GetType();

        if (TryScalar(node, type, out var scalar))
        {
            into[path] = scalar;
            return;
        }

        into[$"{path}::type"] = $"t:{type.Name}";

        if (depth <= 0 || IsOpaque(type) || !seen.Add(node))
            return;

        if (node is System.Collections.ICollection collection)
        {
            into[$"{path}::count"] = $"n:{collection.Count}";

            // An ORDERED collection is walked as well as counted, up to a bound. Stopping at the count
            // made the census blind in a way the controls did not cover: the page cache keeps its
            // capacity inside an array of shards, so CacheSize reported INERT while it was arriving
            // perfectly well. Dictionaries and sets are left alone - their order is not stable enough to
            // compare, and the noise calibration would only throw the whole subtree away.
            if (node is System.Collections.IList list)
            {
                for (var index = 0; index < Math.Min(list.Count, MAX_ELEMENTS); index++)
                    Walk(list[index], $"{path}[{index}]", depth - 1, into, seen);
            }

            return;
        }

        // Anything outside this product is recorded by type and not opened: its internals are the
        // framework's business and a rich source of noise.
        if (type.Namespace?.StartsWith("OutWit", StringComparison.Ordinal) != true)
            return;

        foreach (var field in FieldsOf(type))
        {
            object? value;

            try
            {
                value = field.GetValue(node);
            }
            catch
            {
                continue;
            }

            Walk(value, $"{path}.{field.Name}", depth - 1, into, seen);
        }
    }

    private static bool TryScalar(object node, Type type, out string value)
    {
        value = node switch
        {
            string s => $"s:{s}",
            bool b => $"b:{b}",
            Enum e => $"e:{e}",
            TimeSpan t => $"n:{t.Ticks}",
            byte[] bytes => $"h:{bytes.Length}:{Convert.ToHexString(SHA256.HashData(bytes))[..8]}",
            _ when type.IsPrimitive || type == typeof(decimal) => $"n:{Convert.ToString(node, System.Globalization.CultureInfo.InvariantCulture)}",
            _ => ""
        };

        return value.Length > 0;
    }

    private static bool IsOpaque(Type type)
    {
        return OPAQUE.Any(opaque => opaque.IsAssignableFrom(type)) ||
               type.Name.Contains("Lock", StringComparison.Ordinal);
    }

    /// <summary>
    /// Every instance field the type has, including private fields declared on its base types - which is
    /// what <c>GetFields</c> alone does not return.
    /// </summary>
    private static IEnumerable<FieldInfo> FieldsOf(Type type)
    {
        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            foreach (var field in current.GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                yield return field;
            }
        }
    }

    private static object? Field(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException($"{instance.GetType().Name} has no field {name}.");

        return field.GetValue(instance);
    }

    private static string Short(Exception exception)
    {
        var line = exception.Message.Split('\n')[0].Trim();
        return line.Length > 140 ? line[..140] : line;
    }

    #endregion
}
