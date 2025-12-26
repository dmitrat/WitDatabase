using OutWit.Database.Iterators;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Iterators;

[TestFixture]
public class IteratorJoinTests : IteratorTestsBase
{
    #region Helper Methods

    private static IReadOnlyList<WitSqlColumnInfo> CreateSchemaWithTable(string tableName, params string[] columns)
    {
        return columns.Select(c => new WitSqlColumnInfo
        {
            Name = c,
            Type = WitSqlType.Integer,
            TableName = tableName
        }).ToList();
    }

    private static WitSqlExpression CreateEqualityCondition(string leftTable, string leftCol, string rightTable, string rightCol)
    {
        return new WitSqlExpressionBinary
        {
            Left = new WitSqlExpressionColumnRef { TableName = leftTable, ColumnName = leftCol },
            Operator = BinaryOperatorType.Equal,
            Right = new WitSqlExpressionColumnRef { TableName = rightTable, ColumnName = rightCol }
        };
    }

    #endregion

    #region CROSS JOIN Tests

    [Test]
    public void CrossJoinProducesCartesianProductTest()
    {
        var leftSchema = CreateSchemaWithTable("A", "Id");
        var left = CreateMockIterator(leftSchema,
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2))
        );

        var rightSchema = CreateSchemaWithTable("B", "Value");
        var right = CreateMockIterator(rightSchema,
            CreateRowWithInts(("Value", 10)),
            CreateRowWithInts(("Value", 20)),
            CreateRowWithInts(("Value", 30))
        );

        var iterator = new IteratorJoin(left, right, JoinType.Cross, null, m_context);
        var rows = CollectAllRows(iterator);

        // 2 * 3 = 6 rows
        Assert.That(rows, Has.Count.EqualTo(6));
    }

    [Test]
    public void CrossJoinWithEmptyLeftReturnsEmptyTest()
    {
        var leftSchema = CreateSchemaWithTable("A", "Id");
        var left = CreateMockIterator(leftSchema);

        var rightSchema = CreateSchemaWithTable("B", "Value");
        var right = CreateMockIterator(rightSchema,
            CreateRowWithInts(("Value", 10))
        );

        var iterator = new IteratorJoin(left, right, JoinType.Cross, null, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public void CrossJoinWithEmptyRightReturnsEmptyTest()
    {
        var leftSchema = CreateSchemaWithTable("A", "Id");
        var left = CreateMockIterator(leftSchema,
            CreateRowWithInts(("Id", 1))
        );

        var rightSchema = CreateSchemaWithTable("B", "Value");
        var right = CreateMockIterator(rightSchema);

        var iterator = new IteratorJoin(left, right, JoinType.Cross, null, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Is.Empty);
    }

    #endregion

    #region INNER JOIN Tests

    [Test]
    public void InnerJoinReturnsMatchingRowsOnlyTest()
    {
        var leftSchema = CreateSchemaWithTable("Users", "Id", "Name");
        var left = CreateMockIterator(leftSchema,
            CreateRow(("Id", WitSqlValue.FromInt(1)), ("Name", WitSqlValue.FromText("Alice"))),
            CreateRow(("Id", WitSqlValue.FromInt(2)), ("Name", WitSqlValue.FromText("Bob"))),
            CreateRow(("Id", WitSqlValue.FromInt(3)), ("Name", WitSqlValue.FromText("Charlie")))
        );

        var rightSchema = CreateSchemaWithTable("Orders", "UserId", "Amount");
        var right = CreateMockIterator(rightSchema,
            CreateRow(("UserId", WitSqlValue.FromInt(1)), ("Amount", WitSqlValue.FromInt(100))),
            CreateRow(("UserId", WitSqlValue.FromInt(1)), ("Amount", WitSqlValue.FromInt(200))),
            CreateRow(("UserId", WitSqlValue.FromInt(3)), ("Amount", WitSqlValue.FromInt(300)))
        );

        var onCondition = CreateEqualityCondition("Users", "Id", "Orders", "UserId");
        var iterator = new IteratorJoin(left, right, JoinType.Inner, onCondition, m_context);
        var rows = CollectAllRows(iterator);

        // Alice has 2 orders, Charlie has 1, Bob has none
        Assert.That(rows, Has.Count.EqualTo(3));
    }

    [Test]
    public void InnerJoinWithNoMatchesReturnsEmptyTest()
    {
        var leftSchema = CreateSchemaWithTable("A", "Id");
        var left = CreateMockIterator(leftSchema,
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2))
        );

        var rightSchema = CreateSchemaWithTable("B", "AId");
        var right = CreateMockIterator(rightSchema,
            CreateRowWithInts(("AId", 100)),
            CreateRowWithInts(("AId", 200))
        );

        var onCondition = CreateEqualityCondition("A", "Id", "B", "AId");
        var iterator = new IteratorJoin(left, right, JoinType.Inner, onCondition, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public void InnerJoinPreservesColumnValuesTest()
    {
        var leftSchema = CreateSchemaWithTable("L", "Id", "Name");
        var left = CreateMockIterator(leftSchema,
            CreateRow(("Id", WitSqlValue.FromInt(1)), ("Name", WitSqlValue.FromText("Test")))
        );

        var rightSchema = CreateSchemaWithTable("R", "LId", "Value");
        var right = CreateMockIterator(rightSchema,
            CreateRow(("LId", WitSqlValue.FromInt(1)), ("Value", WitSqlValue.FromInt(999)))
        );

        var onCondition = CreateEqualityCondition("L", "Id", "R", "LId");
        var iterator = new IteratorJoin(left, right, JoinType.Inner, onCondition, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Test"));
        Assert.That(rows[0]["Value"].AsInt64(), Is.EqualTo(999));
    }

    #endregion

    #region LEFT JOIN Tests

    [Test]
    public void LeftJoinReturnsAllLeftRowsTest()
    {
        var leftSchema = CreateSchemaWithTable("Users", "Id");
        var left = CreateMockIterator(leftSchema,
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2)),
            CreateRowWithInts(("Id", 3))
        );

        var rightSchema = CreateSchemaWithTable("Orders", "UserId");
        var right = CreateMockIterator(rightSchema,
            CreateRowWithInts(("UserId", 1))
        );

        var onCondition = CreateEqualityCondition("Users", "Id", "Orders", "UserId");
        var iterator = new IteratorJoin(left, right, JoinType.Left, onCondition, m_context);
        var rows = CollectAllRows(iterator);

        // All 3 left rows should be present
        Assert.That(rows, Has.Count.EqualTo(3));
    }

    [Test]
    public void LeftJoinPadsUnmatchedWithNullsTest()
    {
        var leftSchema = CreateSchemaWithTable("L", "Id");
        var left = CreateMockIterator(leftSchema,
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2))
        );

        var rightSchema = CreateSchemaWithTable("R", "LId", "Value");
        var right = CreateMockIterator(rightSchema,
            CreateRow(("LId", WitSqlValue.FromInt(1)), ("Value", WitSqlValue.FromInt(100)))
        );

        var onCondition = CreateEqualityCondition("L", "Id", "R", "LId");
        var iterator = new IteratorJoin(left, right, JoinType.Left, onCondition, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(2));

        // First row should have Value = 100
        var matchedRow = rows.First(r => r["Id"].AsInt64() == 1);
        Assert.That(matchedRow["Value"].AsInt64(), Is.EqualTo(100));

        // Second row should have Value = NULL
        var unmatchedRow = rows.First(r => r["Id"].AsInt64() == 2);
        Assert.That(unmatchedRow["Value"].IsNull, Is.True);
    }

    [Test]
    public void LeftJoinWithEmptyRightReturnsAllLeftWithNullsTest()
    {
        var leftSchema = CreateSchemaWithTable("L", "Id");
        var left = CreateMockIterator(leftSchema,
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2))
        );

        var rightSchema = CreateSchemaWithTable("R", "LId");
        var right = CreateMockIterator(rightSchema);

        var onCondition = CreateEqualityCondition("L", "Id", "R", "LId");
        var iterator = new IteratorJoin(left, right, JoinType.Left, onCondition, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows.All(r => r["LId"].IsNull), Is.True);
    }

    [Test]
    public void LeftJoinWithMultipleMatchesReturnsAllTest()
    {
        var leftSchema = CreateSchemaWithTable("L", "Id");
        var left = CreateMockIterator(leftSchema,
            CreateRowWithInts(("Id", 1))
        );

        var rightSchema = CreateSchemaWithTable("R", "LId", "Seq");
        var right = CreateMockIterator(rightSchema,
            CreateRow(("LId", WitSqlValue.FromInt(1)), ("Seq", WitSqlValue.FromInt(1))),
            CreateRow(("LId", WitSqlValue.FromInt(1)), ("Seq", WitSqlValue.FromInt(2))),
            CreateRow(("LId", WitSqlValue.FromInt(1)), ("Seq", WitSqlValue.FromInt(3)))
        );

        var onCondition = CreateEqualityCondition("L", "Id", "R", "LId");
        var iterator = new IteratorJoin(left, right, JoinType.Left, onCondition, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(3));
    }

    #endregion

    #region RIGHT JOIN Tests

    [Test]
    public void RightJoinReturnsAllRightRowsTest()
    {
        var leftSchema = CreateSchemaWithTable("Users", "Id");
        var left = CreateMockIterator(leftSchema,
            CreateRowWithInts(("Id", 1))
        );

        var rightSchema = CreateSchemaWithTable("Orders", "UserId");
        var right = CreateMockIterator(rightSchema,
            CreateRowWithInts(("UserId", 1)),
            CreateRowWithInts(("UserId", 2)),
            CreateRowWithInts(("UserId", 3))
        );

        var onCondition = CreateEqualityCondition("Users", "Id", "Orders", "UserId");
        var iterator = new IteratorJoin(left, right, JoinType.Right, onCondition, m_context);
        var rows = CollectAllRows(iterator);

        // All 3 right rows should be present
        Assert.That(rows, Has.Count.EqualTo(3));
    }

    [Test]
    public void RightJoinPadsUnmatchedWithNullsTest()
    {
        var leftSchema = CreateSchemaWithTable("L", "Id", "Name");
        var left = CreateMockIterator(leftSchema,
            CreateRow(("Id", WitSqlValue.FromInt(1)), ("Name", WitSqlValue.FromText("One")))
        );

        var rightSchema = CreateSchemaWithTable("R", "LId");
        var right = CreateMockIterator(rightSchema,
            CreateRowWithInts(("LId", 1)),
            CreateRowWithInts(("LId", 2))
        );

        var onCondition = CreateEqualityCondition("L", "Id", "R", "LId");
        var iterator = new IteratorJoin(left, right, JoinType.Right, onCondition, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(2));

        // First row should have Name
        var matchedRow = rows.First(r => r["LId"].AsInt64() == 1);
        Assert.That(matchedRow["Name"].AsString(), Is.EqualTo("One"));

        // Second row should have Name = NULL
        var unmatchedRow = rows.First(r => r["LId"].AsInt64() == 2);
        Assert.That(unmatchedRow["Name"].IsNull, Is.True);
    }

    #endregion

    #region FULL JOIN Tests

    [Test]
    public void FullJoinReturnsAllRowsFromBothSidesTest()
    {
        var leftSchema = CreateSchemaWithTable("L", "Id");
        var left = CreateMockIterator(leftSchema,
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2))
        );

        var rightSchema = CreateSchemaWithTable("R", "LId");
        var right = CreateMockIterator(rightSchema,
            CreateRowWithInts(("LId", 2)),
            CreateRowWithInts(("LId", 3))
        );

        var onCondition = CreateEqualityCondition("L", "Id", "R", "LId");
        var iterator = new IteratorJoin(left, right, JoinType.Full, onCondition, m_context);
        var rows = CollectAllRows(iterator);

        // Id=1 (no match), Id=2+LId=2 (match), LId=3 (no match)
        Assert.That(rows, Has.Count.EqualTo(3));
    }

    [Test]
    public void FullJoinPadsUnmatchedOnBothSidesTest()
    {
        var leftSchema = CreateSchemaWithTable("L", "Id");
        var left = CreateMockIterator(leftSchema,
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2))
        );

        var rightSchema = CreateSchemaWithTable("R", "LId");
        var right = CreateMockIterator(rightSchema,
            CreateRowWithInts(("LId", 2)),
            CreateRowWithInts(("LId", 3))
        );

        var onCondition = CreateEqualityCondition("L", "Id", "R", "LId");
        var iterator = new IteratorJoin(left, right, JoinType.Full, onCondition, m_context);
        var rows = CollectAllRows(iterator);

        // Row with Id=1 should have LId=NULL
        var leftOnlyRow = rows.FirstOrDefault(r => r["Id"].AsInt64() == 1);
        Assert.That(leftOnlyRow["LId"].IsNull, Is.True);

        // Row with Id=2 should match LId=2
        var matchedRow = rows.FirstOrDefault(r => !r["Id"].IsNull && r["Id"].AsInt64() == 2);
        Assert.That(matchedRow["LId"].AsInt64(), Is.EqualTo(2));

        // Row with LId=3 should have Id=NULL
        var rightOnlyRow = rows.FirstOrDefault(r => !r["LId"].IsNull && r["LId"].AsInt64() == 3);
        Assert.That(rightOnlyRow["Id"].IsNull, Is.True);
    }

    #endregion

    #region Schema Tests

    [Test]
    public void JoinSchemaContainsBothSidesColumnsTest()
    {
        var leftSchema = CreateSchemaWithTable("L", "Id", "Name");
        var left = CreateMockIterator(leftSchema);

        var rightSchema = CreateSchemaWithTable("R", "LId", "Value");
        var right = CreateMockIterator(rightSchema);

        var iterator = new IteratorJoin(left, right, JoinType.Inner, null, m_context);

        Assert.That(iterator.Schema, Has.Count.EqualTo(4));
        Assert.That(iterator.Schema.Any(c => c.Name == "Id" && c.TableName == "L"), Is.True);
        Assert.That(iterator.Schema.Any(c => c.Name == "Name" && c.TableName == "L"), Is.True);
        Assert.That(iterator.Schema.Any(c => c.Name == "LId" && c.TableName == "R"), Is.True);
        Assert.That(iterator.Schema.Any(c => c.Name == "Value" && c.TableName == "R"), Is.True);
    }

    #endregion

    #region Reset Tests

    [Test]
    public void JoinResetWorksCorrectlyTest()
    {
        var leftSchema = CreateSchemaWithTable("L", "Id");
        var left = CreateMockIterator(leftSchema,
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2))
        );

        var rightSchema = CreateSchemaWithTable("R", "LId");
        var right = CreateMockIterator(rightSchema,
            CreateRowWithInts(("LId", 1)),
            CreateRowWithInts(("LId", 2))
        );

        var onCondition = CreateEqualityCondition("L", "Id", "R", "LId");
        var iterator = new IteratorJoin(left, right, JoinType.Inner, onCondition, m_context);

        var rows1 = CollectAllRows(iterator);
        Assert.That(rows1, Has.Count.EqualTo(2));

        iterator.Reset();
        var rows2 = CollectAllRows(iterator);
        Assert.That(rows2, Has.Count.EqualTo(2));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void JoinWithBothSidesEmptyReturnsEmptyTest()
    {
        var leftSchema = CreateSchemaWithTable("L", "Id");
        var left = CreateMockIterator(leftSchema);

        var rightSchema = CreateSchemaWithTable("R", "LId");
        var right = CreateMockIterator(rightSchema);

        var iterator = new IteratorJoin(left, right, JoinType.Inner, null, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public void JoinWithSingleRowEachReturnsCorrectResultTest()
    {
        var leftSchema = CreateSchemaWithTable("L", "Id");
        var left = CreateMockIterator(leftSchema,
            CreateRowWithInts(("Id", 1))
        );

        var rightSchema = CreateSchemaWithTable("R", "LId");
        var right = CreateMockIterator(rightSchema,
            CreateRowWithInts(("LId", 1))
        );

        var onCondition = CreateEqualityCondition("L", "Id", "R", "LId");
        var iterator = new IteratorJoin(left, right, JoinType.Inner, onCondition, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(1));
    }

    [Test]
    public void CrossJoinWithSingleRowEachReturnsSingleRowTest()
    {
        var leftSchema = CreateSchemaWithTable("L", "A");
        var left = CreateMockIterator(leftSchema,
            CreateRowWithInts(("A", 1))
        );

        var rightSchema = CreateSchemaWithTable("R", "B");
        var right = CreateMockIterator(rightSchema,
            CreateRowWithInts(("B", 2))
        );

        var iterator = new IteratorJoin(left, right, JoinType.Cross, null, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["A"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[0]["B"].AsInt64(), Is.EqualTo(2));
    }

    #endregion
}
