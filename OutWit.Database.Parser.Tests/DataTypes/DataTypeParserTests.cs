using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Tests.DataTypes;

/// <summary>
/// Tests for data type parsing (SS1.1 - SS1.8, SS15.1, SS21.1).
/// Covers: integers, floating-point, boolean, date/time, GUID, string, binary, ROWVERSION, JSON.
/// </summary>
[TestFixture]
public class DataTypeParserTests
{
    #region Integer Types (SS1.2)

    [Test]
    [TestCase("TINYINT")]
    [TestCase("INT8")]
    public void ParseTinyIntTypeTest(string typeName)
    {
        var stmt = WitSql.ParseStatement($"CREATE TABLE T (Value {typeName})");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo(typeName));
    }

    [Test]
    [TestCase("UTINYINT")]
    [TestCase("UINT8")]
    public void ParseUnsignedTinyIntTypeTest(string typeName)
    {
        var stmt = WitSql.ParseStatement($"CREATE TABLE T (Value {typeName})");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo(typeName));
    }

    [Test]
    [TestCase("SMALLINT")]
    [TestCase("INT16")]
    public void ParseSmallIntTypeTest(string typeName)
    {
        var stmt = WitSql.ParseStatement($"CREATE TABLE T (Value {typeName})");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo(typeName));
    }

    [Test]
    [TestCase("USMALLINT")]
    [TestCase("UINT16")]
    public void ParseUnsignedSmallIntTypeTest(string typeName)
    {
        var stmt = WitSql.ParseStatement($"CREATE TABLE T (Value {typeName})");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo(typeName));
    }

    [Test]
    [TestCase("INT")]
    [TestCase("INT32")]
    [TestCase("INTEGER")]
    public void ParseIntTypeTest(string typeName)
    {
        var stmt = WitSql.ParseStatement($"CREATE TABLE T (Value {typeName})");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo(typeName));
    }

    [Test]
    [TestCase("UINT")]
    [TestCase("UINT32")]
    public void ParseUnsignedIntTypeTest(string typeName)
    {
        var stmt = WitSql.ParseStatement($"CREATE TABLE T (Value {typeName})");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo(typeName));
    }

    [Test]
    [TestCase("BIGINT")]
    [TestCase("INT64")]
    [TestCase("LONG")]
    public void ParseBigIntTypeTest(string typeName)
    {
        var stmt = WitSql.ParseStatement($"CREATE TABLE T (Value {typeName})");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo(typeName));
    }

    [Test]
    [TestCase("UBIGINT")]
    [TestCase("UINT64")]
    [TestCase("ULONG")]
    public void ParseUnsignedBigIntTypeTest(string typeName)
    {
        var stmt = WitSql.ParseStatement($"CREATE TABLE T (Value {typeName})");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo(typeName));
    }

    #endregion

    #region Floating-Point Types (SS1.3)

    [Test]
    [TestCase("FLOAT16")]
    [TestCase("HALF")]
    public void ParseFloat16TypeTest(string typeName)
    {
        var stmt = WitSql.ParseStatement($"CREATE TABLE T (Value {typeName})");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo(typeName));
    }

    [Test]
    [TestCase("FLOAT")]
    [TestCase("FLOAT32")]
    [TestCase("REAL")]
    public void ParseFloatTypeTest(string typeName)
    {
        var stmt = WitSql.ParseStatement($"CREATE TABLE T (Value {typeName})");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo(typeName));
    }

    [Test]
    [TestCase("DOUBLE")]
    [TestCase("FLOAT64")]
    public void ParseDoubleTypeTest(string typeName)
    {
        var stmt = WitSql.ParseStatement($"CREATE TABLE T (Value {typeName})");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo(typeName));
    }

    [Test]
    [TestCase("DECIMAL")]
    [TestCase("MONEY")]
    [TestCase("NUMERIC")]
    public void ParseDecimalTypeTest(string typeName)
    {
        var stmt = WitSql.ParseStatement($"CREATE TABLE T (Value {typeName})");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo(typeName));
    }

    [Test]
    public void ParseDecimalWithPrecisionAndScaleTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Price DECIMAL(18, 4))");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo("DECIMAL"));
        Assert.That(create.Columns[0].DataType.Precision, Is.EqualTo(18));
        Assert.That(create.Columns[0].DataType.Scale, Is.EqualTo(4));
    }

    #endregion

    #region Boolean Type (SS1.4)

    [Test]
    [TestCase("BOOLEAN")]
    [TestCase("BOOL")]
    public void ParseBooleanTypeTest(string typeName)
    {
        var stmt = WitSql.ParseStatement($"CREATE TABLE T (Flag {typeName} DEFAULT TRUE)");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo(typeName));
    }

    #endregion

    #region Date/Time Types (SS1.5)

    [Test]
    [TestCase("DATE")]
    [TestCase("DATEONLY")]
    public void ParseDateTypeTest(string typeName)
    {
        var stmt = WitSql.ParseStatement($"CREATE TABLE T (Value {typeName})");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo(typeName));
    }

    [Test]
    [TestCase("TIME")]
    [TestCase("TIMEONLY")]
    public void ParseTimeTypeTest(string typeName)
    {
        var stmt = WitSql.ParseStatement($"CREATE TABLE T (Value {typeName})");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo(typeName));
    }

    [Test]
    [TestCase("DATETIME")]
    [TestCase("TIMESTAMP")]
    public void ParseDateTimeTypeTest(string typeName)
    {
        var stmt = WitSql.ParseStatement($"CREATE TABLE T (Value {typeName})");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo(typeName));
    }

    [Test]
    public void ParseDateTimeOffsetTypeTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Value DATETIMEOFFSET)");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo("DATETIMEOFFSET"));
    }

    [Test]
    [TestCase("INTERVAL")]
    [TestCase("TIMESPAN")]
    public void ParseIntervalTypeTest(string typeName)
    {
        var stmt = WitSql.ParseStatement($"CREATE TABLE T (Duration {typeName})");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo(typeName));
    }

    #endregion

    #region GUID Types (SS1.6)

    [Test]
    [TestCase("GUID")]
    [TestCase("UUID")]
    [TestCase("UNIQUEIDENTIFIER")]
    public void ParseGuidTypeTest(string typeName)
    {
        var stmt = WitSql.ParseStatement($"CREATE TABLE T (Id {typeName} PRIMARY KEY)");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo(typeName));
    }

    #endregion

    #region String Types (SS1.7)

    [Test]
    [TestCase("CHAR")]
    [TestCase("NCHAR")]
    public void ParseCharTypeTest(string typeName)
    {
        var stmt = WitSql.ParseStatement($"CREATE TABLE T (Code {typeName}(10))");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo(typeName));
        Assert.That(create.Columns[0].DataType.Length, Is.EqualTo(10));
    }

    [Test]
    [TestCase("VARCHAR")]
    [TestCase("NVARCHAR")]
    public void ParseVarCharTypeTest(string typeName)
    {
        var stmt = WitSql.ParseStatement($"CREATE TABLE T (Name {typeName}(100))");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo(typeName));
        Assert.That(create.Columns[0].DataType.Length, Is.EqualTo(100));
    }

    [Test]
    [TestCase("TEXT")]
    [TestCase("NTEXT")]
    public void ParseTextTypeTest(string typeName)
    {
        var stmt = WitSql.ParseStatement($"CREATE TABLE T (Content {typeName})");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo(typeName));
    }

    [Test]
    public void ParseVarCharMaxTypeTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Data VARCHAR(MAX))");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo("VARCHAR"));
        // MAX is parsed, but representation depends on implementation
    }

    #endregion

    #region Binary Types (SS1.8)

    [Test]
    public void ParseBinaryTypeTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Hash BINARY(64))");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo("BINARY"));
        Assert.That(create.Columns[0].DataType.Length, Is.EqualTo(64));
    }

    [Test]
    public void ParseVarBinaryTypeTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Data VARBINARY(1024))");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo("VARBINARY"));
        Assert.That(create.Columns[0].DataType.Length, Is.EqualTo(1024));
    }

    [Test]
    public void ParseBlobTypeTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Image BLOB)");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo("BLOB"));
    }

    #endregion

    #region ROWVERSION Type (SS15.1)

    [Test]
    public void ParseRowVersionTypeTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (RowVer ROWVERSION NOT NULL)");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo("ROWVERSION"));
    }

    #endregion

    #region JSON Types (SS21.1)

    [Test]
    public void ParseJsonTypeTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Data JSON)");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo("JSON"));
    }

    [Test]
    public void ParseJsonbTypeTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Data JSONB)");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].DataType!.TypeName, Is.EqualTo("JSONB"));
    }

    #endregion

    #region All Types in One Table

    [Test]
    public void ParseTableWithAllTypesTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE AllTypes (
                ColTinyInt TINYINT,
                ColSmallInt SMALLINT,
                ColInt INT,
                ColBigInt BIGINT,
                ColFloat FLOAT,
                ColDouble DOUBLE,
                ColDecimal DECIMAL(18, 4),
                ColBool BOOLEAN,
                ColDate DATE,
                ColTime TIME,
                ColDateTime DATETIME,
                ColDateTimeOffset DATETIMEOFFSET,
                ColInterval INTERVAL,
                ColGuid GUID,
                ColChar CHAR(10),
                ColVarChar VARCHAR(100),
                ColText TEXT,
                ColBinary BINARY(32),
                ColVarBinary VARBINARY(256),
                ColBlob BLOB,
                ColRowVersion ROWVERSION,
                ColJson JSON
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns, Has.Count.EqualTo(22));
    }

    #endregion
}
