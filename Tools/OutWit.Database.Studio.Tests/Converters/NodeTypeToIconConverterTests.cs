using NUnit.Framework;
using OutWit.Database.Studio.Converters;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Tests.Converters;

/// <summary>
/// Tests for <see cref="NodeTypeToIconConverter"/>.
/// </summary>
[TestFixture]
public class NodeTypeToIconConverterTests
{
    #region Fields

    private NodeTypeToIconConverter _converter = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        _converter = new NodeTypeToIconConverter();
    }

    #endregion

    #region Convert Tests

    [Test]
    public void Convert_DatabaseNodeType_ReturnsCorrectIcon()
    {
        var result = _converter.Convert(DatabaseNodeType.Database, typeof(string), null, null);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.TypeOf<string>());
        Assert.That(((string)result!).Length, Is.GreaterThan(0)); // Should return SVG path data
    }

    [Test]
    public void Convert_TablesFolderNodeType_ReturnsCorrectIcon()
    {
        var result = _converter.Convert(DatabaseNodeType.TablesFolder, typeof(string), null, null);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.TypeOf<string>());
        Assert.That(((string)result!), Does.Contain("M")); // SVG path starts with M
    }

    [Test]
    public void Convert_TableNodeType_ReturnsCorrectIcon()
    {
        var result = _converter.Convert(DatabaseNodeType.Table, typeof(string), null, null);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.TypeOf<string>());
        Assert.That(((string)result!), Does.Contain("M")); // SVG path
    }

    [Test]
    public void Convert_ViewsFolderNodeType_ReturnsCorrectIcon()
    {
        var result = _converter.Convert(DatabaseNodeType.ViewsFolder, typeof(string), null, null);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.TypeOf<string>());
    }

    [Test]
    public void Convert_ViewNodeType_ReturnsCorrectIcon()
    {
        var result = _converter.Convert(DatabaseNodeType.View, typeof(string), null, null);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.TypeOf<string>());
    }

    [Test]
    public void Convert_IndexesFolderNodeType_ReturnsCorrectIcon()
    {
        var result = _converter.Convert(DatabaseNodeType.IndexesFolder, typeof(string), null, null);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.TypeOf<string>());
    }

    [Test]
    public void Convert_IndexNodeType_ReturnsCorrectIcon()
    {
        var result = _converter.Convert(DatabaseNodeType.Index, typeof(string), null, null);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.TypeOf<string>());
    }

    [Test]
    public void Convert_TriggersFolderNodeType_ReturnsCorrectIcon()
    {
        var result = _converter.Convert(DatabaseNodeType.TriggersFolder, typeof(string), null, null);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.TypeOf<string>());
    }

    [Test]
    public void Convert_NullValue_ReturnsDefaultIcon()
    {
        var result = _converter.Convert(null, typeof(string), null, null);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.TypeOf<string>());
        Assert.That(((string)result!), Does.Contain("M")); // Default SVG path
    }

    [Test]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        Assert.Throws<NotImplementedException>(() => 
            _converter.ConvertBack("M0,0", typeof(DatabaseNodeType), null, null));
    }

    #endregion
}
