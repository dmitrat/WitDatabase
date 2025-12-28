namespace OutWit.Database.Tests;

/// <summary>
/// Tests for JSON functions: JSON_VALUE, JSON_QUERY, JSON_EXTRACT, JSON_SET, 
/// JSON_INSERT, JSON_REPLACE, JSON_REMOVE, JSON_TYPE, JSON_VALID, JSON_ARRAY, JSON_OBJECT.
/// </summary>
[TestFixture]
public sealed class WitSqlEngineJsonFunctionTests : WitSqlEngineTestsBase
{
    #region Setup

    [SetUp]
    public override void Setup()
    {
        base.Setup();
        CreateTestTables();
    }

    private void CreateTestTables()
    {
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Metadata TEXT
            )");

        m_engine.Execute(@"
            CREATE TABLE Settings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Config TEXT NOT NULL
            )");
    }

    #endregion

    #region JSON_EXTRACT Tests

    [Test]
    public void JsonExtractSimplePropertyTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"color\": \"red\", \"weight\": 150}')");

        var result = m_engine.Query("SELECT JSON_EXTRACT(Metadata, '$.color') AS Color FROM Products");
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0]["Color"].AsString(), Is.EqualTo("red"));
    }

    [Test]
    public void JsonExtractNumericValueTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"price\": 29.99, \"stock\": 100}')");

        var result = m_engine.Query("SELECT JSON_EXTRACT(Metadata, '$.stock') AS Stock FROM Products");
        Assert.That(result[0]["Stock"].AsInt64(), Is.EqualTo(100));
    }

    [Test]
    public void JsonExtractNestedPropertyTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"specs\": {\"dimensions\": {\"width\": 10, \"height\": 20}}}')");

        var result = m_engine.Query("SELECT JSON_EXTRACT(Metadata, '$.specs.dimensions.width') AS Width FROM Products");
        Assert.That(result[0]["Width"].AsInt64(), Is.EqualTo(10));
    }

    [Test]
    public void JsonExtractArrayElementTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"tags\": [\"sale\", \"new\", \"popular\"]}')");

        var result = m_engine.Query("SELECT JSON_EXTRACT(Metadata, '$.tags[0]') AS FirstTag FROM Products");
        Assert.That(result[0]["FirstTag"].AsString(), Is.EqualTo("sale"));
    }

    [Test]
    public void JsonExtractMissingPropertyReturnsNullTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"color\": \"red\"}')");

        var result = m_engine.Query("SELECT JSON_EXTRACT(Metadata, '$.missing') AS Missing FROM Products");
        Assert.That(result[0]["Missing"].IsNull, Is.True);
    }

    [Test]
    public void JsonExtractObjectReturnsJsonTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"specs\": {\"width\": 10, \"height\": 20}}')");

        var result = m_engine.Query("SELECT JSON_EXTRACT(Metadata, '$.specs') AS Specs FROM Products");
        Assert.That(result[0]["Specs"].AsString(), Does.Contain("width"));
        Assert.That(result[0]["Specs"].AsString(), Does.Contain("height"));
    }

    #endregion

    #region JSON_VALUE Tests

    [Test]
    public void JsonValueExtractsScalarTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"name\": \"Test\", \"count\": 42}')");

        var result = m_engine.Query("SELECT JSON_VALUE(Metadata, '$.name') AS Name, JSON_VALUE(Metadata, '$.count') AS Count FROM Products");
        Assert.That(result[0]["Name"].AsString(), Is.EqualTo("Test"));
        Assert.That(result[0]["Count"].AsInt64(), Is.EqualTo(42));
    }

    [Test]
    public void JsonValueReturnsNullForObjectTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"specs\": {\"width\": 10}}')");

        // JSON_VALUE returns NULL for non-scalar values
        var result = m_engine.Query("SELECT JSON_VALUE(Metadata, '$.specs') AS Specs FROM Products");
        Assert.That(result[0]["Specs"].IsNull, Is.True);
    }

    [Test]
    public void JsonValueReturnsNullForArrayTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"tags\": [\"a\", \"b\"]}')");

        var result = m_engine.Query("SELECT JSON_VALUE(Metadata, '$.tags') AS Tags FROM Products");
        Assert.That(result[0]["Tags"].IsNull, Is.True);
    }

    #endregion

    #region JSON_QUERY Tests

    [Test]
    public void JsonQueryExtractsObjectTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"specs\": {\"width\": 10, \"height\": 20}}')");

        var result = m_engine.Query("SELECT JSON_QUERY(Metadata, '$.specs') AS Specs FROM Products");
        Assert.That(result[0]["Specs"].AsString(), Does.Contain("width"));
    }

    [Test]
    public void JsonQueryExtractsArrayTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"tags\": [\"a\", \"b\", \"c\"]}')");

        var result = m_engine.Query("SELECT JSON_QUERY(Metadata, '$.tags') AS Tags FROM Products");
        Assert.That(result[0]["Tags"].AsString(), Does.Contain("["));
        Assert.That(result[0]["Tags"].AsString(), Does.Contain("\"a\""));
    }

    [Test]
    public void JsonQueryReturnsNullForScalarTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"name\": \"Test\"}')");

        // JSON_QUERY returns NULL for scalar values
        var result = m_engine.Query("SELECT JSON_QUERY(Metadata, '$.name') AS Name FROM Products");
        Assert.That(result[0]["Name"].IsNull, Is.True);
    }

    #endregion

    #region JSON_TYPE Tests

    [Test]
    public void JsonTypeReturnsCorrectTypesTest()
    {
        // JSON_TYPE requires a JSON input, so we test it on raw JSON strings
        Assert.That(m_engine.Query("SELECT JSON_TYPE('{\"key\": \"value\"}') AS T")[0]["T"].AsString(), Is.EqualTo("object"));
        Assert.That(m_engine.Query("SELECT JSON_TYPE('[1, 2, 3]') AS T")[0]["T"].AsString(), Is.EqualTo("array"));
        Assert.That(m_engine.Query("SELECT JSON_TYPE('\"hello\"') AS T")[0]["T"].AsString(), Is.EqualTo("string"));
        Assert.That(m_engine.Query("SELECT JSON_TYPE('42') AS T")[0]["T"].AsString(), Is.EqualTo("number"));
        Assert.That(m_engine.Query("SELECT JSON_TYPE('true') AS T")[0]["T"].AsString(), Is.EqualTo("boolean"));
        Assert.That(m_engine.Query("SELECT JSON_TYPE('null') AS T")[0]["T"].AsString(), Is.EqualTo("null"));
    }

    [Test]
    public void JsonTypeOnExtractedObjectTest()
    {
        m_engine.Execute("INSERT INTO Settings (Config) VALUES ('{\"nested\": {\"array\": [1, 2]}}')");

        // JSON_TYPE on extracted object
        var result = m_engine.Query("SELECT JSON_TYPE(JSON_QUERY(Config, '$.nested')) AS T FROM Settings");
        Assert.That(result[0]["T"].AsString(), Is.EqualTo("object"));

        // JSON_TYPE on extracted array
        result = m_engine.Query("SELECT JSON_TYPE(JSON_QUERY(Config, '$.nested.array')) AS T FROM Settings");
        Assert.That(result[0]["T"].AsString(), Is.EqualTo("array"));
    }

    [Test]
    public void JsonTypeOnNullReturnsNullTest()
    {
        var result = m_engine.Query("SELECT JSON_TYPE(NULL) AS T");
        Assert.That(result[0]["T"].AsString(), Is.EqualTo("null"));
    }

    #endregion

    #region JSON_ARRAY_LENGTH Tests

    [Test]
    public void JsonArrayLengthReturnsCountTest()
    {
        m_engine.Execute("INSERT INTO Settings (Config) VALUES ('{\"items\": [1, 2, 3, 4, 5]}')");

        var result = m_engine.Query("SELECT JSON_ARRAY_LENGTH(JSON_EXTRACT(Config, '$.items')) AS Len FROM Settings");
        Assert.That(result[0]["Len"].AsInt64(), Is.EqualTo(5));
    }

    [Test]
    public void JsonArrayLengthOnEmptyArrayTest()
    {
        m_engine.Execute("INSERT INTO Settings (Config) VALUES ('{\"items\": []}')");

        var result = m_engine.Query("SELECT JSON_ARRAY_LENGTH(JSON_EXTRACT(Config, '$.items')) AS Len FROM Settings");
        Assert.That(result[0]["Len"].AsInt64(), Is.EqualTo(0));
    }

    [Test]
    public void JsonArrayLengthOnNonArrayReturnsNullTest()
    {
        m_engine.Execute("INSERT INTO Settings (Config) VALUES ('{\"items\": \"not an array\"}')");

        var result = m_engine.Query("SELECT JSON_ARRAY_LENGTH(JSON_EXTRACT(Config, '$.items')) AS Len FROM Settings");
        Assert.That(result[0]["Len"].IsNull, Is.True);
    }

    #endregion

    #region JSON_VALID Tests

    [Test]
    public void JsonValidReturnsTrueForValidJsonTest()
    {
        var result = m_engine.Query("SELECT JSON_VALID('{\"key\": \"value\"}') AS IsValid");
        Assert.That(result[0]["IsValid"].AsBool(), Is.True);
    }

    [Test]
    public void JsonValidReturnsFalseForInvalidJsonTest()
    {
        var result = m_engine.Query("SELECT JSON_VALID('not valid json') AS IsValid");
        Assert.That(result[0]["IsValid"].AsBool(), Is.False);
    }

    [Test]
    public void JsonValidReturnsFalseForNullTest()
    {
        var result = m_engine.Query("SELECT JSON_VALID(NULL) AS IsValid");
        Assert.That(result[0]["IsValid"].AsBool(), Is.False);
    }

    [Test]
    public void JsonValidWorksInWhereClauseTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Valid', '{\"ok\": true}')");
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Invalid', 'not json')");

        var result = m_engine.Query("SELECT Name FROM Products WHERE JSON_VALID(Metadata) = TRUE");
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0]["Name"].AsString(), Is.EqualTo("Valid"));
    }

    #endregion

    #region JSON_SET Tests

    [Test]
    public void JsonSetAddsNewPropertyTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"color\": \"red\"}')");

        m_engine.Execute("UPDATE Products SET Metadata = JSON_SET(Metadata, '$.size', 'large') WHERE Name = 'Widget'");

        var result = m_engine.Query("SELECT JSON_EXTRACT(Metadata, '$.size') AS Size FROM Products WHERE Name = 'Widget'");
        Assert.That(result[0]["Size"].AsString(), Is.EqualTo("large"));
    }

    [Test]
    public void JsonSetReplacesExistingPropertyTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"color\": \"red\"}')");

        m_engine.Execute("UPDATE Products SET Metadata = JSON_SET(Metadata, '$.color', 'blue') WHERE Name = 'Widget'");

        var result = m_engine.Query("SELECT JSON_EXTRACT(Metadata, '$.color') AS Color FROM Products WHERE Name = 'Widget'");
        Assert.That(result[0]["Color"].AsString(), Is.EqualTo("blue"));
    }

    [Test]
    public void JsonSetWithNumericValueTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"stock\": 100}')");

        m_engine.Execute("UPDATE Products SET Metadata = JSON_SET(Metadata, '$.stock', 150) WHERE Name = 'Widget'");

        var result = m_engine.Query("SELECT JSON_EXTRACT(Metadata, '$.stock') AS Stock FROM Products WHERE Name = 'Widget'");
        Assert.That(result[0]["Stock"].AsInt64(), Is.EqualTo(150));
    }

    #endregion

    #region JSON_INSERT Tests

    [Test]
    public void JsonInsertAddsNewPropertyTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"color\": \"red\"}')");

        m_engine.Execute("UPDATE Products SET Metadata = JSON_INSERT(Metadata, '$.size', 'large') WHERE Name = 'Widget'");

        var result = m_engine.Query("SELECT JSON_EXTRACT(Metadata, '$.size') AS Size FROM Products WHERE Name = 'Widget'");
        Assert.That(result[0]["Size"].AsString(), Is.EqualTo("large"));
    }

    [Test]
    public void JsonInsertDoesNotReplaceExistingTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"color\": \"red\"}')");

        m_engine.Execute("UPDATE Products SET Metadata = JSON_INSERT(Metadata, '$.color', 'blue') WHERE Name = 'Widget'");

        // Should keep original value
        var result = m_engine.Query("SELECT JSON_EXTRACT(Metadata, '$.color') AS Color FROM Products WHERE Name = 'Widget'");
        Assert.That(result[0]["Color"].AsString(), Is.EqualTo("red"));
    }

    #endregion

    #region JSON_REPLACE Tests

    [Test]
    public void JsonReplaceReplacesExistingTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"color\": \"red\"}')");

        m_engine.Execute("UPDATE Products SET Metadata = JSON_REPLACE(Metadata, '$.color', 'green') WHERE Name = 'Widget'");

        var result = m_engine.Query("SELECT JSON_EXTRACT(Metadata, '$.color') AS Color FROM Products WHERE Name = 'Widget'");
        Assert.That(result[0]["Color"].AsString(), Is.EqualTo("green"));
    }

    [Test]
    public void JsonReplaceDoesNotAddNewTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"color\": \"red\"}')");

        m_engine.Execute("UPDATE Products SET Metadata = JSON_REPLACE(Metadata, '$.newprop', 'value') WHERE Name = 'Widget'");

        // Should not add new property
        var result = m_engine.Query("SELECT JSON_EXTRACT(Metadata, '$.newprop') AS NewProp FROM Products WHERE Name = 'Widget'");
        Assert.That(result[0]["NewProp"].IsNull, Is.True);
    }

    #endregion

    #region JSON_REMOVE Tests

    [Test]
    public void JsonRemoveDeletesPropertyTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"color\": \"red\", \"size\": \"large\"}')");

        m_engine.Execute("UPDATE Products SET Metadata = JSON_REMOVE(Metadata, '$.color') WHERE Name = 'Widget'");

        var result = m_engine.Query("SELECT JSON_EXTRACT(Metadata, '$.color') AS Color, JSON_EXTRACT(Metadata, '$.size') AS Size FROM Products WHERE Name = 'Widget'");
        Assert.That(result[0]["Color"].IsNull, Is.True);
        Assert.That(result[0]["Size"].AsString(), Is.EqualTo("large"));
    }

    [Test]
    public void JsonRemoveOnMissingPropertyNoErrorTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"color\": \"red\"}')");

        // Should not throw
        Assert.DoesNotThrow(() =>
            m_engine.Execute("UPDATE Products SET Metadata = JSON_REMOVE(Metadata, '$.missing') WHERE Name = 'Widget'"));
    }

    #endregion

    #region JSON_ARRAY Tests

    [Test]
    public void JsonArrayCreatesArrayTest()
    {
        var result = m_engine.Query("SELECT JSON_ARRAY(1, 2, 3) AS Arr");
        Assert.That(result[0]["Arr"].AsString(), Is.EqualTo("[1,2,3]"));
    }

    [Test]
    public void JsonArrayWithMixedTypesTest()
    {
        var result = m_engine.Query("SELECT JSON_ARRAY('hello', 42, TRUE, NULL) AS Arr");
        var arr = result[0]["Arr"].AsString();
        Assert.That(arr, Does.Contain("hello"));
        Assert.That(arr, Does.Contain("42"));
        Assert.That(arr, Does.Contain("true"));
        Assert.That(arr, Does.Contain("null"));
    }

    [Test]
    public void JsonArrayEmptyTest()
    {
        var result = m_engine.Query("SELECT JSON_ARRAY() AS Arr");
        Assert.That(result[0]["Arr"].AsString(), Is.EqualTo("[]"));
    }

    [Test]
    public void JsonArrayWithExpressionsTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"price\": 10}')");

        var result = m_engine.Query("SELECT JSON_ARRAY(Id, Name, JSON_EXTRACT(Metadata, '$.price') * 2) AS Arr FROM Products");
        var arr = result[0]["Arr"].AsString();
        Assert.That(arr, Does.Contain("Widget"));
        Assert.That(arr, Does.Contain("20"));
    }

    #endregion

    #region JSON_OBJECT Tests

    [Test]
    public void JsonObjectCreatesObjectTest()
    {
        var result = m_engine.Query("SELECT JSON_OBJECT('name', 'John', 'age', 30) AS Obj");
        var obj = result[0]["Obj"].AsString();
        Assert.That(obj, Does.Contain("name"));
        Assert.That(obj, Does.Contain("John"));
        Assert.That(obj, Does.Contain("age"));
        Assert.That(obj, Does.Contain("30"));
    }

    [Test]
    public void JsonObjectEmptyTest()
    {
        var result = m_engine.Query("SELECT JSON_OBJECT() AS Obj");
        Assert.That(result[0]["Obj"].AsString(), Is.EqualTo("{}"));
    }

    [Test]
    public void JsonObjectFromColumnsTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"price\": 29.99}')");

        var result = m_engine.Query("SELECT JSON_OBJECT('product', Name, 'id', Id) AS Obj FROM Products");
        var obj = result[0]["Obj"].AsString();
        Assert.That(obj, Does.Contain("product"));
        Assert.That(obj, Does.Contain("Widget"));
        Assert.That(obj, Does.Contain("id"));
    }

    #endregion

    #region Integration Tests

    [Test]
    public void JsonInWhereClauseTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Red Widget', '{\"color\": \"red\"}')");
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Blue Widget', '{\"color\": \"blue\"}')");

        var result = m_engine.Query("SELECT Name FROM Products WHERE JSON_EXTRACT(Metadata, '$.color') = 'red'");
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0]["Name"].AsString(), Is.EqualTo("Red Widget"));
    }

    [Test]
    public void JsonWithCoalesceTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget1', '{\"nickname\": \"W1\"}')");
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget2', '{\"fullname\": \"Widget Two\"}')");

        var result = m_engine.Query(@"
            SELECT 
                COALESCE(
                    JSON_EXTRACT(Metadata, '$.nickname'), 
                    JSON_EXTRACT(Metadata, '$.fullname'), 
                    'Unknown'
                ) AS DisplayName 
            FROM Products 
            ORDER BY Id");
        
        Assert.That(result[0]["DisplayName"].AsString(), Is.EqualTo("W1"));
        Assert.That(result[1]["DisplayName"].AsString(), Is.EqualTo("Widget Two"));
    }

    [Test]
    public void JsonInCaseExpressionTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Valid', '{\"status\": \"active\"}')");
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Invalid', 'not json')");

        var result = m_engine.Query(@"
            SELECT 
                Name,
                CASE 
                    WHEN JSON_VALID(Metadata) = TRUE THEN JSON_EXTRACT(Metadata, '$.status')
                    ELSE 'Invalid JSON'
                END AS Status
            FROM Products
            ORDER BY Id");
        
        Assert.That(result[0]["Status"].AsString(), Is.EqualTo("active"));
        Assert.That(result[1]["Status"].AsString(), Is.EqualTo("Invalid JSON"));
    }

    [Test]
    public void JsonNestedOperationsTest()
    {
        m_engine.Execute("INSERT INTO Products (Name, Metadata) VALUES ('Widget', '{\"data\": {\"value\": 100}}')");

        // Nested JSON_SET followed by JSON_EXTRACT
        m_engine.Execute(@"
            UPDATE Products 
            SET Metadata = JSON_SET(JSON_REMOVE(Metadata, '$.data.value'), '$.data.newValue', 200) 
            WHERE Name = 'Widget'");

        var result = m_engine.Query("SELECT JSON_EXTRACT(Metadata, '$.data.newValue') AS NewValue FROM Products");
        Assert.That(result[0]["NewValue"].AsInt64(), Is.EqualTo(200));
    }

    #endregion
}
