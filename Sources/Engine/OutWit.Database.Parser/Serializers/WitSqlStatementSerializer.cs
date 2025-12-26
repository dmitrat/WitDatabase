using System.Text;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.TableSources;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Serializers;

/// <summary>
/// Serializes WitSql statement AST back to SQL text.
/// </summary>
public static class WitSqlStatementSerializer
{
    #region Serialize

    /// <summary>
    /// Serializes a statement to SQL text.
    /// </summary>
    public static string Serialize(WitSqlStatement statement)
    {
        return statement switch
        {
            WitSqlStatementSelect select => SerializeSelect(select),
            WitSqlStatementInsert insert => SerializeInsert(insert),
            WitSqlStatementUpdate update => SerializeUpdate(update),
            WitSqlStatementDelete delete => SerializeDelete(delete),
            _ => throw new NotSupportedException($"Statement serialization not supported: {statement.GetType().Name}")
        };
    }

    #endregion

    #region SELECT

    private static string SerializeSelect(WitSqlStatementSelect select)
    {
        var sb = new StringBuilder();
        sb.Append("SELECT ");

        if (select.IsDistinct)
            sb.Append("DISTINCT ");

        // Select list
        for (int i = 0; i < select.SelectList.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(SerializeSelectItem(select.SelectList[i]));
        }

        // FROM clause
        if (select.FromClause != null && select.FromClause.Count > 0)
        {
            sb.Append(" FROM ");
            for (int i = 0; i < select.FromClause.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(SerializeTableSource(select.FromClause[i]));
            }
        }

        // WHERE clause
        if (select.WhereClause != null)
        {
            sb.Append(" WHERE ");
            sb.Append(WitSqlExpressionSerializer.Serialize(select.WhereClause));
        }

        // GROUP BY clause
        if (select.GroupByClause != null && select.GroupByClause.Count > 0)
        {
            sb.Append(" GROUP BY ");
            sb.Append(string.Join(", ", select.GroupByClause.Select(WitSqlExpressionSerializer.Serialize)));
        }

        // HAVING clause
        if (select.HavingClause != null)
        {
            sb.Append(" HAVING ");
            sb.Append(WitSqlExpressionSerializer.Serialize(select.HavingClause));
        }

        // ORDER BY clause
        if (select.OrderByClause != null && select.OrderByClause.Count > 0)
        {
            sb.Append(" ORDER BY ");
            for (int i = 0; i < select.OrderByClause.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var order = select.OrderByClause[i];
                sb.Append(WitSqlExpressionSerializer.Serialize(order.Expression));
                sb.Append(order.Descending ? " DESC" : " ASC");
            }
        }

        // LIMIT clause
        if (select.LimitCount != null)
        {
            sb.Append(" LIMIT ");
            sb.Append(WitSqlExpressionSerializer.Serialize(select.LimitCount));
            if (select.LimitOffset != null)
            {
                sb.Append(" OFFSET ");
                sb.Append(WitSqlExpressionSerializer.Serialize(select.LimitOffset));
            }
        }

        return sb.ToString();
    }

    private static string SerializeSelectItem(ClauseSelectItem item)
    {
        if (item.IsStar)
        {
            return item.TableName != null ? $"{item.TableName}.*" : "*";
        }

        if (item.Expression == null)
            return "*";

        var expr = WitSqlExpressionSerializer.Serialize(item.Expression);
        return item.Alias != null ? $"{expr} AS {item.Alias}" : expr;
    }

    private static string SerializeTableSource(TableSource source)
    {
        var sb = new StringBuilder();

        switch (source)
        {
            case TableSourceSimple simple:
                sb.Append(simple.TableName);
                break;

            case TableSourceSubquery subquery:
                sb.Append('(');
                sb.Append(SerializeSelect(subquery.Subquery));
                sb.Append(')');
                break;

            case TableSourceJoin join:
                sb.Append(SerializeTableSource(join.Left));
                sb.Append(' ');
                sb.Append(GetJoinType(join.JoinType));
                sb.Append(' ');
                sb.Append(SerializeTableSource(join.Right));
                if (join.OnCondition != null)
                {
                    sb.Append(" ON ");
                    sb.Append(WitSqlExpressionSerializer.Serialize(join.OnCondition));
                }
                break;

            default:
                throw new NotSupportedException($"Table source type not supported: {source.GetType().Name}");
        }

        if (source.Alias != null)
        {
            sb.Append(" AS ");
            sb.Append(source.Alias);
        }

        return sb.ToString();
    }

    private static string GetJoinType(JoinType joinType)
    {
        return joinType switch
        {
            JoinType.Inner => "INNER JOIN",
            JoinType.Left => "LEFT JOIN",
            JoinType.Right => "RIGHT JOIN",
            JoinType.Full => "FULL JOIN",
            JoinType.Cross => "CROSS JOIN",
            _ => "JOIN"
        };
    }

    #endregion

    #region INSERT

    private static string SerializeInsert(WitSqlStatementInsert insert)
    {
        var sb = new StringBuilder();
        sb.Append("INSERT INTO ");
        sb.Append(insert.TableName);

        // Column names
        if (insert.ColumnNames != null && insert.ColumnNames.Count > 0)
        {
            sb.Append(" (");
            sb.Append(string.Join(", ", insert.ColumnNames));
            sb.Append(')');
        }

        // VALUES or SELECT
        if (insert.Values != null && insert.Values.Count > 0)
        {
            sb.Append(" VALUES ");
            for (int i = 0; i < insert.Values.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append('(');
                sb.Append(string.Join(", ", insert.Values[i].Select(WitSqlExpressionSerializer.Serialize)));
                sb.Append(')');
            }
        }
        else if (insert.SelectSource != null)
        {
            sb.Append(' ');
            sb.Append(SerializeSelect(insert.SelectSource));
        }

        return sb.ToString();
    }

    #endregion

    #region UPDATE

    private static string SerializeUpdate(WitSqlStatementUpdate update)
    {
        var sb = new StringBuilder();
        sb.Append("UPDATE ");
        sb.Append(update.TableName);

        if (update.TableAlias != null)
        {
            sb.Append(" AS ");
            sb.Append(update.TableAlias);
        }

        sb.Append(" SET ");
        for (int i = 0; i < update.SetClauses.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var set = update.SetClauses[i];
            sb.Append(set.ColumnName);
            sb.Append(" = ");
            sb.Append(WitSqlExpressionSerializer.Serialize(set.Value));
        }

        if (update.WhereClause != null)
        {
            sb.Append(" WHERE ");
            sb.Append(WitSqlExpressionSerializer.Serialize(update.WhereClause));
        }

        return sb.ToString();
    }

    #endregion

    #region DELETE

    private static string SerializeDelete(WitSqlStatementDelete delete)
    {
        var sb = new StringBuilder();
        sb.Append("DELETE FROM ");
        sb.Append(delete.TableName);

        if (delete.TableAlias != null)
        {
            sb.Append(" AS ");
            sb.Append(delete.TableAlias);
        }

        if (delete.WhereClause != null)
        {
            sb.Append(" WHERE ");
            sb.Append(WitSqlExpressionSerializer.Serialize(delete.WhereClause));
        }

        return sb.ToString();
    }

    #endregion
}
