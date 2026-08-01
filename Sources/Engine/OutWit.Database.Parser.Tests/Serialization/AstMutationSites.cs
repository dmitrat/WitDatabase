using System.Collections;
using System.Reflection;
using OutWit.Common.Abstract;

namespace OutWit.Database.Parser.Tests.Serialization;

/// <summary>
/// Enumerates every place in an AST where a value is stored, and changes exactly one of them.
/// </summary>
/// <remarks>
/// <para>
/// This exists to give the round-trip instrument a control. The round-trip test compares two ASTs
/// with <c>ModelBase.Is</c>, and every <c>Is</c> in this assembly is <b>hand-written</b> - the same
/// failure mode as the hand-written serializer the phase is replacing. An <c>Is</c> that forgets a
/// property would make the round-trip test green and powerless.
/// </para>
/// <para>
/// Sites are addressed by an ordinal in a deterministic walk, so the same ordinal names the same
/// place in an original and in its <c>Clone()</c>. The mutation is applied to the clone and the
/// comparison is made <b>at the root</b>, not at the mutated node - that is deliberate. Comparing at
/// the node would only prove the node's own <c>Is</c> reads the property; comparing at the root also
/// proves every ancestor propagates it. An ancestor that silently ignores a child is exactly the
/// shape this phase is chasing.
/// </para>
/// </remarks>
internal static class AstMutationSites
{
    #region Constants

    /// <summary>Guards against a cycle; the AST is a tree, so this only ever bounds a bug.</summary>
    private const int MAX_DEPTH = 64;

    #endregion

    #region Functions

    /// <summary>Number of mutable sites reachable from <paramref name="root"/>.</summary>
    public static int Count(ModelBase root)
    {
        var counter = new Walker(target: -1);
        counter.Walk(root, 0);
        return counter.Visited;
    }

    /// <summary>
    /// Changes the value at <paramref name="ordinal"/>, returning a description of what it changed,
    /// or <c>null</c> when no mutation strategy exists for that property's type.
    /// </summary>
    public static string? Mutate(ModelBase root, int ordinal)
    {
        var walker = new Walker(ordinal);
        walker.Walk(root, 0);
        return walker.Description;
    }

    #endregion

    #region Walker

    private sealed class Walker
    {
        public Walker(int target) => Target = target;

        public void Walk(ModelBase node, int depth)
        {
            if (depth > MAX_DEPTH)
                return;

            foreach (var property in PropertiesOf(node.GetType()))
            {
                var value = property.GetValue(node);

                // The site itself: this property on this node.
                var ordinal = Visited++;

                if (ordinal == Target)
                    Description = Apply(node, property, value);

                // Then descend, so children get their own ordinals after their parent's.
                switch (value)
                {
                    case ModelBase child:
                        Walk(child, depth + 1);
                        break;

                    case IEnumerable sequence and not string:
                        foreach (var item in sequence)
                        {
                            if (item is ModelBase element)
                                Walk(element, depth + 1);
                        }

                        break;
                }
            }
        }

        public int Visited { get; private set; }

        public string? Description { get; private set; }

        private int Target { get; }
    }

    #endregion

    #region Mutation

    private static string? Apply(ModelBase node, PropertyInfo property, object? current)
    {
        var mutated = Different(property.PropertyType, current);

        if (mutated is null && current is null)
            return null;

        try
        {
            property.SetValue(node, mutated);
        }
        catch
        {
            // No strategy for this shape rather than a failed mutation: reporting it as a mutation
            // would make an unchanged value look like one Is failed to notice.
            return null;
        }

        // The instrument's own guard. An earlier version replaced an already-empty collection with
        // another empty collection and reported 41 phantom findings, because "a value was assigned"
        // is not "a value changed". Read it back and require a real difference.
        if (!Changed(current, property.GetValue(node)))
            return null;

        return $"{node.GetType().Name}.{property.Name}: <{Show(current)}> -> <{Show(mutated)}>";
    }

    /// <summary>Whether two stored values genuinely differ, collections compared by content.</summary>
    private static bool Changed(object? before, object? after)
    {
        if (before is null || after is null)
            return !ReferenceEquals(before, after);

        if (before is IEnumerable first and not string && after is IEnumerable second and not string)
        {
            var left = first.Cast<object?>().ToArray();
            var right = second.Cast<object?>().ToArray();

            if (left.Length != right.Length)
                return true;

            return left.Where((item, i) => !SameElement(item, right[i])).Any();
        }

        return !Equals(before, after);
    }

    private static bool SameElement(object? left, object? right) => (left, right) switch
    {
        (null, null) => true,
        (ModelBase a, ModelBase b) => a.Is(b),
        _ => Equals(left, right)
    };

    /// <summary>
    /// A value of <paramref name="type"/> that is not <paramref name="current"/>, or <c>null</c> when
    /// this type has no strategy. Returning null is reported by the caller rather than skipped: a
    /// property nobody can mutate is a property the control does not cover, and that has to be
    /// visible.
    /// </summary>
    private static object? Different(Type type, object? current)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (current is null)
            return Populated(underlying);

        if (underlying == typeof(string))
            return (string)current + "§";

        if (underlying == typeof(bool))
            return !(bool)current;

        if (underlying.IsEnum)
        {
            var values = Enum.GetValues(underlying).Cast<object>().ToArray();
            return values.FirstOrDefault(value => !value.Equals(current));
        }

        if (underlying == typeof(int)) return (int)current + 1;
        if (underlying == typeof(long)) return (long)current + 1;
        if (underlying == typeof(short)) return (short)((short)current + 1);
        if (underlying == typeof(byte)) return (byte)((byte)current + 1);
        if (underlying == typeof(double)) return (double)current + 1;
        if (underlying == typeof(float)) return (float)current + 1;
        if (underlying == typeof(decimal)) return (decimal)current + 1;

        // A node: drop it. Removing a child is the sharpest possible change, and any Is that reads
        // the property at all must notice.
        if (typeof(ModelBase).IsAssignableFrom(underlying))
            return null;

        // A collection: emptying it is a change only if it held something. An already-empty one has
        // no strategy, so hand the same value back and let the guard in Apply classify it.
        if (current is IEnumerable sequence and not string)
            return sequence.Cast<object?>().Any() ? Empty(type) : current;

        return null;
    }

    /// <summary>A non-null value for a property that is currently null.</summary>
    private static object? Populated(Type underlying)
    {
        if (underlying == typeof(string)) return "§";
        if (underlying == typeof(bool)) return true;
        if (underlying == typeof(int)) return 1;
        if (underlying == typeof(long)) return 1L;
        if (underlying == typeof(short)) return (short)1;
        if (underlying == typeof(byte)) return (byte)1;
        if (underlying == typeof(double)) return 1d;
        if (underlying == typeof(float)) return 1f;
        if (underlying == typeof(decimal)) return 1m;
        if (underlying.IsEnum) return Enum.GetValues(underlying).Cast<object>().FirstOrDefault();

        return null;
    }

    private static object? Empty(Type type)
    {
        var element = type.IsArray
            ? type.GetElementType()
            : type.IsGenericType
                ? type.GetGenericArguments().FirstOrDefault()
                : null;

        return element is null ? null : Array.CreateInstance(element, 0);
    }

    #endregion

    #region Reflection

    private static readonly Dictionary<Type, PropertyInfo[]> CACHE = new();

    private static PropertyInfo[] PropertiesOf(Type type)
    {
        if (CACHE.TryGetValue(type, out var cached))
            return cached;

        // Ordered by name so the walk is identical for an original and its clone, whatever order
        // reflection hands the members back in.
        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.CanWrite)
            .Where(property => property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

        CACHE[type] = properties;
        return properties;
    }

    private static string Show(object? value) => value switch
    {
        null => "null",
        string text => text,
        IEnumerable sequence and not string => $"[{sequence.Cast<object?>().Count()} items]",
        _ => value.ToString() ?? "?"
    };

    #endregion
}
