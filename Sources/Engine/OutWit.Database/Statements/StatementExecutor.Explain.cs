using System.Text;
using OutWit.Database.Interfaces;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Sql;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Statements;

public sealed partial class StatementExecutor
{
    #region EXPLAIN

    /// <summary>
    /// Executes an EXPLAIN statement and returns the query execution plan.
    /// </summary>
    private WitSqlResult ExecuteExplain(WitSqlStatementExplain explain)
    {
        // Build the query plan (but don't execute it)
        var iterator = m_planner.Plan(explain.Statement);
        
        // Generate the plan description
        var planRows = new List<WitSqlRow>();
        var planDescription = BuildPlanDescription(iterator, 0, explain.QueryPlan);
        
        var columnNames = new[] { "id", "parent", "detail" };
        int id = 0;
        foreach (var line in planDescription)
        {
            var values = new WitSqlValue[]
            {
                WitSqlValue.FromInt(id++),
                WitSqlValue.FromInt(line.Parent),
                WitSqlValue.FromText(line.Detail)
            };
            planRows.Add(new WitSqlRow(values, columnNames));
        }
        
        // Dispose the iterator (we didn't execute it)
        iterator.Dispose();
        
        // Define schema
        var schema = new List<WitSqlColumnInfo>
        {
            new() { Name = "id", Type = WitSqlType.Integer },
            new() { Name = "parent", Type = WitSqlType.Integer },
            new() { Name = "detail", Type = WitSqlType.Text }
        };
        
        return new WitSqlResult(planRows, schema);
    }

    private List<PlanLine> BuildPlanDescription(IResultIterator iterator, int depth, bool queryPlan)
    {
        var lines = new List<PlanLine>();
        var indent = new string(' ', depth * 2);
        
        var detail = queryPlan 
            ? GetQueryPlanDescription(iterator)
            : GetDetailedDescription(iterator);
        
        // This node is line 0 of its own subtree, and its parent is filled in by whoever appends
        // that subtree - the top-level call is the one that keeps -1.
        lines.Add(new PlanLine(-1, $"{indent}{detail}"));

        // Add child iterators if they exist
        var children = GetChildIterators(iterator);
        foreach (var child in children)
        {
            // WHERE THIS CHILD'S SUBTREE STARTS, which is the whole of a defect shipped in 14.0.0:
            // the lines used to be re-based by a constant (`currentId + 1`), which is right for the
            // FIRST child and wrong for every one after it by the size of everything already
            // appended. A join has two children, and so does every set operation - so EXPLAIN put
            // the right input's scan under the LEFT input's alias, and anything drawing the plan as
            // a tree drew the wrong tree faithfully (KnownIssues 26).
            var offset = lines.Count;

            foreach (var line in BuildPlanDescription(child, depth + 1, queryPlan))
            {
                // A subtree's own root is the child itself: it goes under THIS node, which is line
                // 0 of this list. Everything else keeps the parent it had, moved to where the
                // subtree has landed.
                lines.Add(new PlanLine(line.Parent == -1 ? 0 : line.Parent + offset, line.Detail));
            }
        }

        return lines;
    }

    private string GetQueryPlanDescription(IResultIterator iterator)
    {
        var typeName = iterator.GetType().Name;
        
        return typeName switch
        {
            "IteratorTableScan" => $"SCAN TABLE {GetTableName(iterator)}",
            "IteratorIndexSeek" => $"SEARCH TABLE {GetTableName(iterator)} USING INDEX {GetIndexName(iterator)} (=)",
            "IteratorIndexRangeScan" => $"SEARCH TABLE {GetTableName(iterator)} USING INDEX {GetIndexName(iterator)} (range)",
            "IteratorFilter" => "FILTER",
            "IteratorProject" => "PROJECT",
            "IteratorSort" => "SORT",
            "IteratorLimit" => "LIMIT",
            "IteratorDistinct" => "DISTINCT",
            "IteratorGroupBy" => "AGGREGATE",
            "IteratorHideGroupingKeys" => "HIDE GROUPING KEYS",
            "IteratorWindow" => "WINDOW",
            "IteratorJoin" => $"NESTED LOOP {GetJoinType(iterator)} JOIN",
            "IteratorNestedLoopJoin" => $"NESTED LOOP {GetJoinType(iterator)} JOIN",
            "IteratorHashJoin" => $"HASH {GetJoinType(iterator)} JOIN",
            "IteratorAlias" => $"ALIAS {GetAliasName(iterator)}",
            "IteratorUnion" => "UNION",
            "IteratorIntersect" => "INTERSECT",
            "IteratorExcept" => "EXCEPT",
            "IteratorLocking" => "LOCK",
            "IteratorInMemory" => "IN-MEMORY",
            "IteratorColumnRename" => "COLUMN RENAME",
            "IteratorInformationSchema" => $"VIRTUAL TABLE {GetSchemaTableName(iterator)}",
            _ => typeName.Replace("Iterator", "")
        };
    }

    private string GetDetailedDescription(IResultIterator iterator)
    {
        var sb = new StringBuilder();
        sb.Append(GetQueryPlanDescription(iterator));
        
        // Add additional details based on iterator type
        var schema = iterator.Schema;
        if (schema != null && schema.Count > 0)
        {
            sb.Append($" -> [{string.Join(", ", schema.Select(c => c.Name))}]");
        }
        
        return sb.ToString();
    }

    private IEnumerable<IResultIterator> GetChildIterators(IResultIterator iterator)
    {
        // Use reflection to get child iterators
        var type = iterator.GetType();
        
        // Check for common field names
        var sourceField = type.GetField("m_source", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (sourceField?.GetValue(iterator) is IResultIterator source)
        {
            yield return source;
        }
        
        // Standard left/right fields (IteratorJoin)
        var leftField = type.GetField("m_left", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (leftField?.GetValue(iterator) is IResultIterator left)
        {
            yield return left;
        }
        
        var rightField = type.GetField("m_right", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (rightField?.GetValue(iterator) is IResultIterator right)
        {
            yield return right;
        }
        
        // Hash join uses build/probe sides
        var buildSideField = type.GetField("m_buildSide", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (buildSideField?.GetValue(iterator) is IResultIterator buildSide)
        {
            yield return buildSide;
        }
        
        var probeSideField = type.GetField("m_probeSide", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (probeSideField?.GetValue(iterator) is IResultIterator probeSide)
        {
            yield return probeSide;
        }
    }

    private string GetTableName(IResultIterator iterator)
    {
        var type = iterator.GetType();
        var tableField = type.GetField("m_tableName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (tableField?.GetValue(iterator) is string tableName)
            return tableName;
        
        var tableDefField = type.GetField("m_table", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var tableDef = tableDefField?.GetValue(iterator);
        if (tableDef != null)
        {
            var nameProperty = tableDef.GetType().GetProperty("Name");
            if (nameProperty?.GetValue(tableDef) is string name)
                return name;
        }
        
        return "?";
    }

    private string GetIndexName(IResultIterator iterator)
    {
        var type = iterator.GetType();
        var indexField = type.GetField("m_index", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var indexDef = indexField?.GetValue(iterator);
        if (indexDef != null)
        {
            var nameProperty = indexDef.GetType().GetProperty("Name");
            if (nameProperty?.GetValue(indexDef) is string name)
                return name;
        }
        return "?";
    }

    private string GetJoinType(IResultIterator iterator)
    {
        var type = iterator.GetType();
        var joinTypeField = type.GetField("m_joinType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (joinTypeField?.GetValue(iterator) is { } joinType)
            return joinType.ToString()?.ToUpper() ?? "JOIN";
        return "JOIN";
    }

    private string GetAliasName(IResultIterator iterator)
    {
        var type = iterator.GetType();
        var aliasField = type.GetField("m_alias", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (aliasField?.GetValue(iterator) is string alias)
            return alias;
        return "?";
    }

    private string GetSchemaTableName(IResultIterator iterator)
    {
        var type = iterator.GetType();
        var tableNameField = type.GetField("m_tableName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (tableNameField?.GetValue(iterator) is string tableName)
            return tableName;
        return "?";
    }

    #endregion

    #region Helper Types

    private record PlanLine(int Parent, string Detail);

    #endregion
}
