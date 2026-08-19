using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OutWit.Database.Studio.Tests;

/// <summary>
/// A handler asked for a route its event does not travel is never called, and nothing says so.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured on 2026-08-19, and it had shipped.</b> The double click that opens a table's data
/// (WS-19) stopped working once a table had a placeholder child to expand, because the tree toggles
/// the row on a double tap and marks the event handled. The repair was to register the handler as
/// TUNNELLING, so that it would run first - and <c>DoubleTapped</c> is registered as
/// <c>Bubble</c> ALONE. A tunnelling handler for it is not an early handler, it is no handler: the
/// double click did nothing at all from that commit until this one, and 1014 tests, a green CI and a
/// signed release had nothing to say about it.
/// </para>
/// <para>
/// <c>AddHandler</c> takes the route as a plain argument and cannot refuse an impossible one, so this
/// is checked here: every <c>AddHandler</c> in Studio is read out of the source, the event is
/// resolved to the real <see cref="RoutedEvent"/>, and the route asked for must be one the event
/// actually travels.
/// </para>
/// </remarks>
[TestFixture]
public class AHandlerRunsOnlyOnARouteItsEventTravelsTests
{
    #region Constants

    /// <summary>
    /// <c>AddHandler(SomeEvent, Handler, ...)</c>, whether or not it names a route. A call that
    /// leaves the route out gets Bubble, which every routed event travels; those are counted anyway,
    /// because a walk that reports no offenders has to be able to say what it read.
    /// </summary>
    private static readonly Regex ADD_HANDLER = new(
        @"AddHandler\(\s*(?<event>[\w\.]+)\s*,",
        RegexOptions.Compiled);

    /// <summary>The route asked for, inside the same statement.</summary>
    private static readonly Regex ROUTES = new(
        @"RoutingStrategies\.(?<routes>[\w\s\|\.]+?)\s*[,\)]",
        RegexOptions.Compiled);

    #endregion

    #region Tests

    [Test]
    public void EveryHandlerInStudioAsksForARouteItsEventTravelsTest()
    {
        var events = TheRoutedEventsAvaloniaPublishes();

        var offenders = new List<string>();
        var examined = new List<string>();
        var asked = 0;

        foreach (var (file, source) in StudioSources())
        foreach (Match match in ADD_HANDLER.Matches(source))
        {
            var name = match.Groups["event"].Value.Split('.')[^1];

            Assert.That(events.ContainsKey(name), Is.True,
                $"{file}: this fixture could not resolve {name} to a routed event");

            examined.Add($"{file}: {name}");

            // The overload without a route gives the handler Bubble, which every routed event
            // travels. Only a call that NAMES one can name an impossible one.
            var written = ROUTES.Match(Statement(source, match.Index));

            if (!written.Success)
                continue;

            asked++;

            var routes = Routes(written.Groups["routes"].Value);
            var travels = events[name].RoutingStrategies;

            if ((routes & travels) != routes)
                offenders.Add($"{file}: {name} travels {travels}, and the handler asks for {routes} - "
                    + "the part that is not there is never called");
        }

        Assert.Multiple(() =>
        {
            // CONTROL: a walk that read no source, or a pattern that matched nothing, would report
            // no offenders either - which is exactly what this fixture is here to disbelieve. Both
            // halves of the reader are named: the calls it found, and the routes it read out of
            // them.
            Assert.That(examined, Has.Count.GreaterThanOrEqualTo(2),
                "CONTROL: too few AddHandler calls were found - the walk or the pattern is wrong");

            Assert.That(asked, Is.GreaterThanOrEqualTo(1),
                "CONTROL: no route was read out of any of them - " + string.Join(", ", examined));

            Assert.That(offenders, Is.Empty, string.Join(Environment.NewLine, offenders));
        });
    }

    /// <summary>
    /// The fact the case above was written for, stated on its own: <c>DoubleTapped</c> bubbles and
    /// does not tunnel.
    /// </summary>
    /// <remarks>
    /// It is asserted rather than remembered because the repair that failed was reasoned from the
    /// opposite assumption. If a later Avalonia gives the event a tunnelling route, this case is
    /// where that shows up, and the rule above quietly starts allowing what it forbids today.
    /// </remarks>
    [Test]
    public void TheDoubleTapIsABubblingEventAndNothingElseTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(InputElement.DoubleTappedEvent.RoutingStrategies,
                Is.EqualTo(RoutingStrategies.Bubble),
                "a tunnelling handler for the double tap is not an early handler, it is no handler");

            // The pointer is the route that DOES have a tunnel, which is where the double click
            // belongs once the tree wants to handle it before the row does.
            Assert.That(InputElement.PointerPressedEvent.RoutingStrategies.HasFlag(RoutingStrategies.Tunnel),
                Is.True, "the pointer tunnels, which is why the double click is read from it");
        });
    }

    #endregion

    #region Tools

    /// <summary>The statement the call is part of: from the call to the semicolon that ends it.</summary>
    private static string Statement(string source, int index)
    {
        var end = source.IndexOf(';', index);

        return end < 0 ? source[index..] : source[index..end];
    }

    private static RoutingStrategies Routes(string written)
    {
        var routes = RoutingStrategies.Direct & 0;

        foreach (var part in written.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            routes |= Enum.Parse<RoutingStrategies>(part.Split('.')[^1]);

        return routes;
    }

    /// <summary>
    /// Every routed event Avalonia publishes as a public static field, by name.
    /// </summary>
    private static IReadOnlyDictionary<string, RoutedEvent> TheRoutedEventsAvaloniaPublishes()
    {
        var assemblies = new[]
        {
            typeof(InputElement).Assembly,
            typeof(Control).Assembly,
            typeof(RoutedEvent).Assembly
        }.Distinct();

        var events = new Dictionary<string, RoutedEvent>();

        foreach (var type in assemblies.SelectMany(assembly => assembly.GetExportedTypes()))
        foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (!typeof(RoutedEvent).IsAssignableFrom(field.FieldType))
                continue;

            if (field.GetValue(null) is RoutedEvent value)
                events.TryAdd(field.Name, value);
        }

        return events;
    }

    private static IEnumerable<(string File, string Source)> StudioSources()
    {
        var root = StudioRoot();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;

            yield return (Path.GetRelativePath(root, file), File.ReadAllText(file));
        }
    }

    private static string StudioRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Tools", "OutWit.Database.Studio");

            if (Directory.Exists(Path.Combine(candidate, "Views")))
                return candidate;

            directory = directory.Parent;
        }

        throw new AssertionException("the Studio project was not found from " + AppContext.BaseDirectory);
    }

    #endregion
}
