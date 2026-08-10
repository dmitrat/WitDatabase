using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OutWit.Database.EntityFramework.Storage;

namespace OutWit.Database.EntityFramework.Tests.Storage;

/// <summary>
/// A store type NAME that carries its size resolves to that size - so a column described by its
/// length and the same column described by its type name are one mapping, not two.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect this guards, reported from WitAnalytics against 12.3.0 and reproduced on 12.5.0.</b>
/// The second migration of any model carried one spurious <c>AlterColumn</c> per sized column, in
/// both directions, with <c>oldType: "TEXT"</c> while the snapshot said <c>VARCHAR(n)</c>:
/// </para>
/// <code>
/// migrationBuilder.AlterColumn&lt;string&gt;(name: "Name", type: "VARCHAR(100)", maxLength: 100,
///     oldType: "TEXT", oldMaxLength: 100);
/// </code>
/// <para>
/// A model snapshot writes <b>both</b> <c>HasMaxLength(100)</c> and
/// <c>HasColumnType("VARCHAR(100)")</c>. The first resolved to <c>VARCHAR(100)</c> and the second to
/// <c>TEXT</c>, because the store-type branch cut the size off to look the name up and then threw it
/// away - so EF's differ, which compares the resolved types, saw every sized column as altered.
/// <b>The `Down` half was the dangerous one</b>: it narrowed each column back to <c>TEXT</c>.
/// </para>
/// <para>
/// <b>The report named `VARCHAR`; measuring it found `DECIMAL` and `VARBINARY` had the identical
/// fault.</b> That is why the cases below are a table of every affected spelling rather than one
/// case for the one that was reported.
/// </para>
/// <para>
/// <b>Why this fixture is at the MAPPING layer and not at the differ.</b> An in-process differ over
/// two models built in one process could not reproduce the report - both sides collapsed to the same
/// answer, so the comparison was quiet whether the defect was present or not. The reproduction that
/// works is the reported one: <c>dotnet ef migrations add</c> twice against a scratch project. What
/// this fixture pins is the mechanism underneath it, exactly and cheaply; the evidence for the
/// symptom is the generated migration, in <c>@Evidence/differ</c>.
/// </para>
/// </remarks>
[TestFixture]
public class StoreTypeNameFacetsTests
{
    #region Fields

    private WitTypeMappingSource m_source = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        var dependencies = new TypeMappingSourceDependencies(
            new ValueConverterSelector(new ValueConverterSelectorDependencies()),
            new JsonValueReaderWriterSource(new JsonValueReaderWriterSourceDependencies()),
            []);

        m_source = new WitTypeMappingSource(dependencies, new RelationalTypeMappingSourceDependencies([]));
    }

    #endregion

    #region The two spellings must converge

    /// <summary>
    /// The same column, described the way a model declares it and the way a snapshot writes it. The
    /// two must answer with the same store type - that equality IS the defect, and it is asserted
    /// rather than each side being checked against a literal.
    /// </summary>
    [TestCase(typeof(string), 100, null, null, "VARCHAR(100)")]
    [TestCase(typeof(string), 20, null, null, "VARCHAR(20)")]
    [TestCase(typeof(byte[]), 16, null, null, "VARBINARY(16)")]
    public void ALengthAndItsTypeNameResolveToTheSameMappingTest(
        Type clrType, int size, int? precision, int? scale, string expected)
    {
        var byLength = m_source.FindMapping(clrType, storeTypeName: null, size: size);
        var byName = m_source.FindMapping(clrType, expected);

        Assert.Multiple(() =>
        {
            Assert.That(byLength?.StoreType, Is.EqualTo(expected), "declared by its length");
            Assert.That(byName?.StoreType, Is.EqualTo(expected), "declared by its type name");
            Assert.That(byName?.Size, Is.EqualTo(size), "and the size survives the name");
        });
    }

    /// <summary>
    /// Decimal carries precision and scale rather than a size, and had the same fault:
    /// <c>DECIMAL(18,2)</c> resolved to a bare <c>DECIMAL</c>.
    /// </summary>
    [Test]
    public void APrecisionAndItsTypeNameResolveToTheSameMappingTest()
    {
        var byPrecision = m_source.FindMapping(typeof(decimal), storeTypeName: null, precision: 18, scale: 2);
        var byName = m_source.FindMapping(typeof(decimal), "DECIMAL(18,2)");

        Assert.Multiple(() =>
        {
            Assert.That(byPrecision?.StoreType, Is.EqualTo("DECIMAL(18,2)"));
            Assert.That(byName?.StoreType, Is.EqualTo("DECIMAL(18,2)"));
            Assert.That(byName?.Precision, Is.EqualTo(18));
            Assert.That(byName?.Scale, Is.EqualTo(2));
        });
    }

    /// <summary>
    /// The name is answered with verbatim, whichever synonym it uses - the differ compares strings,
    /// so a mapping that "corrected" <c>NVARCHAR(50)</c> to <c>VARCHAR(50)</c> would reintroduce the
    /// defect for anyone who wrote the other spelling.
    /// </summary>
    [TestCase("VARCHAR(50)")]
    [TestCase("NVARCHAR(50)")]
    [TestCase("CHAR(50)")]
    [TestCase("NCHAR(50)")]
    public void TheNameIsAnsweredWithVerbatimTest(string storeTypeName)
    {
        Assert.That(m_source.FindMapping(typeof(string), storeTypeName)?.StoreType,
            Is.EqualTo(storeTypeName));
    }

    #endregion

    #region Controls

    /// <summary>
    /// CONTROL: a name with no facets is unchanged, which is the ordinary case and the one the whole
    /// store-type table exists for. Without this, "sized names work" would be equally true of a
    /// change that broke every unsized one.
    /// </summary>
    [TestCase(typeof(string), "TEXT", "TEXT")]
    [TestCase(typeof(string), "VARCHAR", "TEXT")]
    [TestCase(typeof(byte[]), "BLOB", "BLOB")]
    [TestCase(typeof(byte[]), "VARBINARY", "BLOB")]
    [TestCase(typeof(decimal), "DECIMAL", "DECIMAL")]
    [TestCase(typeof(int), "INT", "INT")]
    [TestCase(typeof(int), "INTEGER", "INT")]
    [TestCase(typeof(Guid), "UUID", "GUID")]
    [TestCase(typeof(DateTime), "TIMESTAMP", "DATETIME")]
    public void ControlAnUnsizedNameKeepsItsOwnMappingTest(Type clrType, string storeTypeName, string expected)
    {
        Assert.That(m_source.FindMapping(clrType, storeTypeName)?.StoreType, Is.EqualTo(expected));
    }

    /// <summary>
    /// CONTROL: a facet this cannot read is not a failure - it falls back to the plain mapping,
    /// which is what happened before any of this existed. <c>VARCHAR(MAX)</c> is SQL Server's
    /// spelling and reaches here from a model written for it.
    /// </summary>
    [TestCase("VARCHAR(MAX)")]
    [TestCase("VARCHAR()")]
    [TestCase("VARCHAR(0)")]
    [TestCase("VARCHAR(-1)")]
    public void ControlAFacetThatIsNotANumberFallsBackTest(string storeTypeName)
    {
        Assert.That(() => m_source.FindMapping(typeof(string), storeTypeName), Throws.Nothing);
        Assert.That(m_source.FindMapping(typeof(string), storeTypeName)?.StoreType, Is.EqualTo("TEXT"));
    }

    /// <summary>
    /// CONTROL: a facet on a type that has no use for one is ignored rather than obeyed. Nothing
    /// writes <c>INT(11)</c> for this engine, but MySQL models do, and inventing a mapping for it
    /// would be worse than falling back.
    /// </summary>
    [Test]
    public void ControlAFacetOnATypeThatHasNoUseForOneIsIgnoredTest()
    {
        Assert.That(m_source.FindMapping(typeof(int), "INT(11)")?.StoreType, Is.EqualTo("INT"));
        Assert.That(m_source.FindMapping(typeof(DateTime), "DATETIME(6)")?.StoreType, Is.EqualTo("DATETIME"));
    }

    #endregion
}
