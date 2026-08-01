using System.Collections;
using System.Reflection;
using OutWit.Common.Abstract;
using OutWit.Database.Parser.Expressions;

namespace OutWit.Database.Parser.Analysis;

/// <summary>
/// Which columns an expression reads.
/// </summary>
/// <remarks>
/// <para>
/// Replaces a substring search. Until 9.0.0 the engine decided whether a write had to maintain a
/// filtered or expression index by asking whether the <b>rendered text</b> of the filter contained
/// the column's name. That is wrong in both directions: a column named <c>Age</c> matches a filter
/// mentioning <c>Agent</c>, so indexes were rebuilt for writes that could not affect them, and a
/// filter whose rendering was absent matched nothing at all, so an index that needed maintaining
/// silently did not get it.
/// </para>
/// <para>
/// The walk is by reflection rather than by a switch over the twenty expression types. A switch can
/// miss a node type - and a missed node type means missed columns, which means an index quietly
/// left stale. Reflection cannot miss one, and the answer is computed once per index and cached by
/// the caller, so the cost lands on schema changes rather than on writes.
/// </para>
/// </remarks>
public static class WitSqlColumnReferences
{
    #region Constants

    private const int MAX_DEPTH = 64;

    #endregion

    #region Functions

    /// <summary>
    /// Every column name the expression mentions, compared case-insensitively.
    /// </summary>
    public static IReadOnlySet<string> Collect(WitSqlExpression? expression)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (expression is not null)
            Walk(expression, names, 0);

        return names;
    }

    #endregion

    #region Walking

    private static void Walk(ModelBase node, HashSet<string> names, int depth)
    {
        if (depth > MAX_DEPTH)
            return;

        if (node is WitSqlExpressionColumnRef column)
            names.Add(column.ColumnName);

        foreach (var property in PropertiesOf(node.GetType()))
        {
            switch (property.GetValue(node))
            {
                case ModelBase child:
                    Walk(child, names, depth + 1);
                    break;

                case IEnumerable sequence and not string:
                    foreach (var item in sequence)
                    {
                        if (item is ModelBase element)
                            Walk(element, names, depth + 1);
                    }

                    break;
            }
        }
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
                .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                .Where(property => typeof(ModelBase).IsAssignableFrom(property.PropertyType)
                                   || typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
                .Where(property => property.PropertyType != typeof(string))
                .ToArray();

            CACHE[type] = properties;
            return properties;
        }
    }

    #endregion
}
