using System.Collections;
using System.Reflection;
using OutWit.Common.Abstract;
using OutWit.Database.Parser.Nodes;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Analysis;

/// <summary>
/// Walks a parse tree without a hand-written case per node type.
/// </summary>
/// <remarks>
/// <para>
/// Every hand-written walk over this AST has turned out to be incomplete. The aggregate detector
/// covered 4 of the 19 expression types and returned <c>false</c> for the rest, so an aggregate
/// inside <c>BETWEEN</c> or <c>IN</c> was invisible to it; the index-maintenance check searched the
/// rendered <b>text</b> for a column name. A walk driven by whatever properties a type declares
/// cannot omit one by hand, and it keeps working when the grammar gains a node.
/// </para>
/// <para>
/// <b>Statements are a boundary.</b> An expression's subquery children are statements, and what is
/// inside a subquery belongs to that subquery: <c>HAVING COUNT(*) &gt; (SELECT COUNT(*) FROM X)</c>
/// has one aggregate in the outer query, not two. So the walk stops at a nested statement, which is
/// exactly the line the old switch drew by accident when it fell through to <c>false</c>.
/// </para>
/// </remarks>
public static class WitSqlNodes
{
    #region Constants

    private const int MAX_DEPTH = 64;

    #endregion

    #region Functions

    /// <summary>
    /// <paramref name="root"/> and every node beneath it, not entering a nested statement.
    /// </summary>
    public static IEnumerable<WitSqlNode> SelfAndDescendants(WitSqlNode? root)
    {
        if (root is null)
            yield break;

        yield return root;

        foreach (var descendant in Children(root, 0))
            yield return descendant;
    }

    #endregion

    #region Walking

    private static IEnumerable<WitSqlNode> Children(ModelBase node, int depth)
    {
        if (depth > MAX_DEPTH)
            yield break;

        foreach (var property in PropertiesOf(node.GetType()))
        {
            var value = property.GetValue(node);

            foreach (var child in Flatten(value))
            {
                // A nested statement is a query of its own; what it contains is its business.
                if (child is WitSqlStatement)
                    continue;

                if (child is WitSqlNode witNode)
                    yield return witNode;

                foreach (var deeper in Children(child, depth + 1))
                    yield return deeper;
            }
        }
    }

    private static IEnumerable<ModelBase> Flatten(object? value)
    {
        switch (value)
        {
            case ModelBase single:
                yield return single;
                break;

            case IEnumerable sequence and not string:
                foreach (var item in sequence)
                {
                    if (item is ModelBase element)
                    {
                        yield return element;
                    }
                    else if (item is IEnumerable nested and not string)
                    {
                        // VALUES rows are a list of lists.
                        foreach (var inner in nested)
                        {
                            if (inner is ModelBase deep)
                                yield return deep;
                        }
                    }
                }

                break;
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
                .Where(property => property.PropertyType != typeof(string))
                .Where(property => typeof(ModelBase).IsAssignableFrom(property.PropertyType)
                                   || typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
                .ToArray();

            CACHE[type] = properties;
            return properties;
        }
    }

    #endregion
}
