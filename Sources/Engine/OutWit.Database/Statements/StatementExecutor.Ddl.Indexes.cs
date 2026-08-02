using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Parser.Serializers;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Sql;
using OutWit.Database.Parser.Expressions;

namespace OutWit.Database.Statements;

/// <summary>
/// DDL execution for INDEX operations (CREATE, DROP).
/// </summary>
public sealed partial class StatementExecutor
{
    #region CREATE INDEX

    private WitSqlResult ExecuteCreateIndex(WitSqlStatementCreateIndex createIndex)
    {
        RefuseUnknownFunctions(createIndex, createIndex.IndexName);

        // Check if table exists
        var table = m_context.Database.GetTable(createIndex.TableName);
        if (table == null)
        {
            throw new InvalidOperationException($"Table '{createIndex.TableName}' not found");
        }

        // Check if index already exists when IF NOT EXISTS is specified
        if (createIndex.IfNotExists)
        {
            var existingIndex = m_context.Database.GetIndex(createIndex.IndexName);
            if (existingIndex != null)
            {
                return new WitSqlResult(); // Index already exists, do nothing
            }
        }

        // Build Columns list: for expression elements, use synthetic placeholder names
        // This ensures Columns.Count == ExpressionColumns.Count
        var columns = new List<string>();
        var expressionColumns = new List<string?>();
        var expressions = new List<WitSqlExpression?>();
        
        for (int i = 0; i < createIndex.Elements.Count; i++)
        {
            var element = createIndex.Elements[i];
            if (element.ColumnName != null)
            {
                columns.Add(element.ColumnName);
                expressions.Add(element.Expression);
            }
            else if (element.Expression != null)
            {
                // Pure expression index element - use placeholder column name
                columns.Add($"$expr{i}");
                expressions.Add(element.Expression);
            }

            RefuseNonDeterministicKey(createIndex.IndexName, element.Expression);
        }

        var metadata = new DefinitionIndex
        {
            Name = createIndex.IndexName,
            TableName = createIndex.TableName,
            Columns = columns,
            IsUnique = createIndex.IsUnique,
            ColumnDescending = createIndex.Elements.Select(e => e.Descending).ToList(),
            Where = createIndex.WhereClause,
            IncludeColumns = createIndex.IncludeColumns,
            Expressions = expressions
        };

        m_context.Database.CreateIndex(metadata);
        return new WitSqlResult();
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Refuses an index key the engine could not keep up to date.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An index key is written from the expression once, at insert time, and read by every query
    /// afterwards. Nothing recomputes it. So an expression whose answer can move - because it reads
    /// another table, or because it is <c>RANDOM()</c> - leaves the index holding a value the row no
    /// longer has, and a query's answer then depends on whether the plan happened to use the index.
    /// </para>
    /// <para>
    /// Measured accepted at head on 2026-08-01, both the subquery form and the plain
    /// <c>RANDOM()</c>. Refused at declaration rather than repaired later, because there is no later:
    /// by the time the two disagree the wrong key is already on the media.
    /// </para>
    /// <para>
    /// This is phase 7's rule - accepted, enforced, or refused - and it is also the precondition
    /// phase 9d needs before a user-defined function may appear in an index expression.
    /// </para>
    /// </remarks>
    private static void RefuseNonDeterministicKey(string indexName, WitSqlExpression? expression)
    {
        var reason = ExpressionDeterminism.ReasonItIsNotDeterministic(expression);

        if (reason == null)
            return;

        throw new NotSupportedException(
            $"Index '{indexName}' cannot be built on this expression because {reason}. "
            + "An index key is computed once when the row is written and is never recomputed, so an "
            + "expression whose value can change would leave the index describing a value the row no "
            + "longer has.");
    }

    #endregion

    #region DROP INDEX

    private WitSqlResult ExecuteDropIndex(WitSqlStatementDropIndex dropIndex)
    {
        // Check IF EXISTS
        if (dropIndex.IfExists)
        {
            var existingIndex = m_context.Database.GetIndex(dropIndex.IndexName);
            if (existingIndex == null)
            {
                return new WitSqlResult(); // Index doesn't exist, do nothing
            }
        }

        m_context.Database.DropIndex(dropIndex.IndexName);
        return new WitSqlResult();
    }

    #endregion
}
