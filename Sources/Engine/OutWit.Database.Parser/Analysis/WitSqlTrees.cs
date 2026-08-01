using System.Collections;
using System.Reflection;
using OutWit.Common.Abstract;
using OutWit.Database.Parser.Nodes;

namespace OutWit.Database.Parser.Analysis;

/// <summary>
/// Compares two parse trees for meaning, ignoring where in the source text they came from.
/// </summary>
/// <remarks>
/// <para>
/// <c>ModelBase.Is</c> compares <see cref="WitSqlNode.Line"/> and <see cref="WitSqlNode.Column"/>,
/// and it should: they are stored, so a round trip through the catalog has to bring them back. But
/// they describe a <b>source text</b>, and two renderings of the same schema legitimately differ in
/// them - a view's body parsed inside a <c>CREATE VIEW</c> starts at a different column from the
/// same body parsed on its own. Asking <c>Is</c> whether a rendering is faithful therefore answers
/// "no" for every faithful rendering there is.
/// </para>
/// <para>
/// This is deliberately a <b>generic reflection walk</b> rather than a second hand-written
/// comparison. Ninety hand-written <c>Is</c> methods are exactly what needed a mutation control to
/// trust, and three of them turned out to be wrong; a walk over whatever properties a type declares
/// cannot omit one by hand. <c>WitSqlTreesTests</c> pins it against <c>Is</c> so the two cannot
/// disagree about anything except position.
/// </para>
/// </remarks>
public static class WitSqlTrees
{
    #region Constants

    private const int MAX_DEPTH = 64;

    #endregion

    #region Functions

    /// <summary>
    /// Whether the two trees are the same but for the source positions their nodes carry.
    /// </summary>
    public static bool SameIgnoringPositions(ModelBase? left, ModelBase? right) =>
        Same(left, right, 0);

    #endregion

    #region Comparison

    private static bool Same(object? left, object? right, int depth)
    {
        if (depth > MAX_DEPTH)
            return true;

        if (left is null || right is null)
            return left is null && right is null;

        if (left is ModelBase leftNode && right is ModelBase rightNode)
        {
            if (leftNode.GetType() != rightNode.GetType())
                return false;

            foreach (var property in PropertiesOf(leftNode.GetType()))
            {
                if (!Same(property.GetValue(leftNode), property.GetValue(rightNode), depth + 1))
                    return false;
            }

            return true;
        }

        if (left is string || right is string)
            return Equals(left, right);

        if (left is IEnumerable leftItems && right is IEnumerable rightItems)
        {
            var first = leftItems.Cast<object?>().ToArray();
            var second = rightItems.Cast<object?>().ToArray();

            if (first.Length != second.Length)
                return false;

            return !first.Where((item, i) => !Same(item, second[i], depth + 1)).Any();
        }

        if (left is byte[] leftBytes && right is byte[] rightBytes)
            return leftBytes.AsSpan().SequenceEqual(rightBytes);

        return Equals(left, right);
    }

    #endregion

    #region Reflection

    private static readonly Dictionary<Type, PropertyInfo[]> CACHE = new();

    private static PropertyInfo[] PropertiesOf(Type type)
    {
        lock (CACHE)
        {
            if (CACHE.TryGetValue(type, out var cached))
                return cached;

            var properties = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.CanRead && property.CanWrite)
                .Where(property => property.GetIndexParameters().Length == 0)
                // The two properties this type exists to ignore.
                .Where(property => property.Name is not (nameof(WitSqlNode.Line) or nameof(WitSqlNode.Column)))
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .ToArray();

            CACHE[type] = properties;
            return properties;
        }
    }

    #endregion
}
