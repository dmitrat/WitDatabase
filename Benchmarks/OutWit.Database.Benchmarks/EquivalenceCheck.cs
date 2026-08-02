using System.Collections;
using System.Reflection;
using BenchmarkDotNet.Attributes;

namespace OutWit.Database.Benchmarks;

/// <summary>
/// The control the benchmark suite spent its whole life without.
/// </summary>
/// <remarks>
/// Every benchmark here runs "the same" operation against WitDatabase, SQLite and LiteDB and times
/// it. Nothing in the suite ever checked that the three compute the same thing, or that the
/// operation returned anything at all - so a query answering zero rows, or an engine quietly
/// disagreeing with the other two, would simply have benchmarked as fast.
///
/// This runs every benchmark body exactly once, groups the return values by operation, and reports
/// any operation where the engines disagree or one of them throws. It is not a timing run and must
/// never be read as one; it is the thing that has to be green before a timing run means anything.
///
/// Run it with <c>dotnet run -c Release -- verify</c>. It exits non-zero on any disagreement, so it
/// can gate a sweep.
///
/// Known limit, stated rather than hidden: the write benchmarks return <c>void</c>, so this can say
/// nothing about them. An <c>INSERT</c> benchmark that inserts nothing still passes here.
/// </remarks>
public static class EquivalenceCheck
{
    private static readonly string[] Engines = { "WitDb", "SQLite", "LiteDB" };

    /// <summary>
    /// The suite names the same operation differently per engine ("COUNT(*)" against "Count()",
    /// "INNER JOIN 2 tables" against "Manual JOIN 2 collections"). Fold the known pairs onto one
    /// key so they are compared; anything unmatched stays in its own group and is reported as
    /// uncompared rather than silently passing.
    /// </summary>
    private static readonly (string From, string To)[] Aliases =
    {
        ("Count()", "COUNT(*)"), ("Sum(", "SUM("), ("Average(", "AVG("), ("Min/Max(", "MIN/MAX("),
        ("GroupBy", "GROUP BY"), ("with Where (HAVING)", "with HAVING"),
        ("FindAll (full scan)", "SELECT * (full scan)"), ("Find(Age > 30)", "SELECT WHERE Age > 30"),
        ("FindAll + OrderBy", "SELECT ORDER BY Name"), ("FindAll.Take(100)", "SELECT LIMIT 100"),
        ("FindById (100x)", "Point Query by PK (100x)"),
        ("Select Id, Name (projection)", "SELECT Id, Name (projection)"),
        ("Manual JOIN 2 collections", "INNER JOIN 2 tables"),
        ("Manual JOIN 3 collections", "INNER JOIN 3 tables"),
        ("Manual JOIN 4 collections", "INNER JOIN 4 tables"),
        // Note the already-rewritten spelling: the "GroupBy" alias above runs first, so by the time
        // this one is applied the text reads "Manual JOIN + GROUP BY". Matching the original
        // spelling here left the operation uncompared, which the check reported as "only LiteDB".
        ("Manual LEFT JOIN", "LEFT JOIN"), ("Manual JOIN + GROUP BY", "JOIN with GROUP BY"),
        ("Manual JOIN with filter", "JOIN with WHERE filter"),
        ("Update + FindById", "UPDATE RETURNING"), ("InsertBulk", "INSERT RETURNING"),
    };

    public static int Run(WitDbEngineMode mode)
    {
        var types = typeof(EquivalenceCheck).Assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.GetMethods().Any(m => m.GetCustomAttribute<BenchmarkAttribute>() != null))
            .OrderBy(t => t.Name)
            .ToList();

        Console.WriteLine($"Equivalence check, EngineMode = {mode}");
        Console.WriteLine();

        var problems = 0;

        foreach (var type in types)
        {
            Console.WriteLine($"## {type.Name}");
            try
            {
                problems += Check(type, mode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  !! {type.Name} failed to run: {Flatten(ex)}");
                problems++;
            }
        }

        Console.WriteLine();
        Console.WriteLine(problems == 0
            ? "OK - every compared operation agrees across WitDatabase, SQLite and LiteDB."
            : $"FAILED - {problems} operation(s) disagreed or threw.");

        return problems == 0 ? 0 : 1;
    }

    private static int Check(Type type, WitDbEngineMode mode)
    {
        var instance = Activator.CreateInstance(type)!;

        foreach (var property in type.GetProperties())
        {
            if (property.GetCustomAttribute<ParamsSourceAttribute>() == null)
                continue;

            if (property.PropertyType == typeof(WitDbEngineMode))
            {
                property.SetValue(instance, mode);
                continue;
            }

            var source = type.GetProperty(property.Name + "Values")?.GetValue(instance);
            if (source is IEnumerable<int> sizes)
                property.SetValue(instance, sizes.Min());
        }

        Hook(type, typeof(GlobalSetupAttribute))?.Invoke(instance, null);

        var iterationSetup = Hook(type, typeof(IterationSetupAttribute));
        var iterationCleanup = Hook(type, typeof(IterationCleanupAttribute));

        var measured = new List<(string Operation, string Engine, string Value)>();

        foreach (var method in type.GetMethods())
        {
            var benchmark = method.GetCustomAttribute<BenchmarkAttribute>();
            if (benchmark == null)
                continue;

            var (operation, engine) = Split(benchmark.Description ?? method.Name);

            string value;
            try
            {
                iterationSetup?.Invoke(instance, null);
                value = Render(method.Invoke(instance, null));
                iterationCleanup?.Invoke(instance, null);
            }
            catch (Exception ex)
            {
                value = "THREW: " + Flatten(ex);
            }

            measured.Add((operation, engine, value));
        }

        var problems = 0;

        foreach (var group in measured.GroupBy(m => m.Operation))
        {
            var threw = group.Where(g => g.Value.StartsWith("THREW", StringComparison.Ordinal)).ToList();
            var distinct = group.Select(g => g.Value).Distinct().Count();

            // A void benchmark renders as "void" on every engine, so it agrees trivially. Say so
            // rather than counting it as a pass.
            if (group.All(g => g.Value == "void"))
            {
                Console.WriteLine($"  ~  {group.Key}: returns void, nothing to compare");
                continue;
            }

            if (threw.Count > 0)
            {
                Console.WriteLine($"  !! {group.Key}");
                foreach (var m in group)
                    Console.WriteLine($"       {m.Engine,-8} {m.Value}");
                problems++;
            }
            else if (distinct > 1)
            {
                Console.WriteLine($"  !! {group.Key}: engines DISAGREE");
                foreach (var m in group)
                    Console.WriteLine($"       {m.Engine,-8} {m.Value}");
                problems++;
            }
            else if (group.Count() == 1)
            {
                Console.WriteLine($"  ?  {group.Key}: only {group.First().Engine} - not compared");
            }
            else
            {
                Console.WriteLine($"  ok {group.Key} = {group.First().Value}");
            }
        }

        Hook(type, typeof(GlobalCleanupAttribute))?.Invoke(instance, null);
        (instance as IDisposable)?.Dispose();

        return problems;
    }

    private static (string Operation, string Engine) Split(string description)
    {
        foreach (var engine in Engines)
        {
            if (!description.EndsWith(" - " + engine, StringComparison.Ordinal))
                continue;

            var operation = description[..^(engine.Length + 3)].Trim();
            foreach (var (from, to) in Aliases)
                operation = operation.Replace(from, to);

            return (operation, engine);
        }

        return (description, "?");
    }

    private static MethodInfo? Hook(Type type, Type attribute) =>
        type.GetMethods().FirstOrDefault(m => m.GetCustomAttributes(attribute, true).Any());

    private static string Render(object? value)
    {
        switch (value)
        {
            case null:
                return "void";
            case string s:
                return "\"" + s + "\"";
            case double d:
                return d.ToString("F4");
            case float f:
                return f.ToString("F4");
            case decimal m:
                return m.ToString("F4");
            case IEnumerable enumerable:
            {
                var items = enumerable.Cast<object>().ToList();
                return $"[{items.Count} items] {string.Join(", ", items.Take(3).Select(Render))}";
            }
        }

        var type = value.GetType();
        if (type.IsGenericType && type.Name.StartsWith("ValueTuple", StringComparison.Ordinal))
            return "(" + string.Join(", ", type.GetFields().Select(f => Render(f.GetValue(value)))) + ")";

        return value.ToString() ?? "null";
    }

    private static string Flatten(Exception ex)
    {
        while (ex is TargetInvocationException { InnerException: not null } wrapped)
            ex = wrapped.InnerException;

        return ex.GetType().Name + ": " + ex.Message.Replace("\r", " ").Replace("\n", " ");
    }
}
