using System.Reflection;
using System.Security.Cryptography;

namespace OutWit.Database.AdoNet.Tests.Modularity;

/// <summary>
/// A structural fingerprint of a built engine: the runtime type at every reachable field, and the value
/// of every scalar.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <see cref="ConfigurationCensusTests"/>, which is where it was built and where its
/// blind spots were found. Two instruments now ask questions of the same walk - "does this keyword
/// reach the engine at all" and "does a reopen rebuild what the keyword built" - and a second copy of
/// it would be a second set of blind spots to discover.
/// </para>
/// <para>
/// <b>Fields only, never properties.</b> A property can compute, allocate or take a lock, and this has
/// to be able to look at a live engine without changing it.
/// </para>
/// </remarks>
internal static class EngineFingerprint
{
    #region Constants

    /// <summary>How deep the reflection walk goes before it records a type name and stops.</summary>
    public const int MAX_DEPTH = 9;

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

    #region Functions

    /// <summary>
    /// Takes a fingerprint of one or more roots, each walked under its own path prefix.
    /// </summary>
    public static SortedDictionary<string, string> Take(params (string Path, object? Root)[] roots)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach (var (path, root) in roots)
            Walk(root, path, MAX_DEPTH, values, seen);

        return values;
    }

    /// <summary>
    /// The paths whose values differ between two fingerprints of the SAME configuration. Anything in
    /// here is a temporary path, a handle or a timing - noise, and never evidence about a setting.
    /// </summary>
    public static HashSet<string> Noise(
        SortedDictionary<string, string> one,
        SortedDictionary<string, string> two)
    {
        return new HashSet<string>(
            one.Keys.Union(two.Keys)
                .Where(key => !one.TryGetValue(key, out var a) ||
                              !two.TryGetValue(key, out var b) ||
                              a != b));
    }

    /// <summary>
    /// Reads a private instance field, by name. Used to reach the database and engine a connection holds.
    /// </summary>
    public static object? Field(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException($"{instance.GetType().Name} has no field {name}.");

        return field.GetValue(instance);
    }

    #endregion

    #region Tools

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

    #endregion
}
