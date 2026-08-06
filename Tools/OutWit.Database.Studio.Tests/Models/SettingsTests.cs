using NUnit.Framework;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Tests.Models;

/// <summary>
/// Tests for Settings model.
/// </summary>
[TestFixture]
public class SettingsTests
{
    #region Default Values Tests

    [Test]
    public void DefaultThemeIsLightTest()
    {
        // Arrange & Act
        var settings = new Settings();

        // Assert
        Assert.That(settings.Theme, Is.EqualTo("Light"));
    }

    [Test]
    public void DefaultRecentFilesIsEmptyTest()
    {
        // Arrange & Act
        var settings = new Settings();

        // Assert
        Assert.That(settings.RecentFiles, Is.Not.Null);
        Assert.That(settings.RecentFiles, Is.Empty);
    }

    [Test]
    public void DefaultMaxRecentFilesIs10Test()
    {
        // Arrange & Act
        var settings = new Settings();

        // Assert
        Assert.That(settings.MaxRecentFiles, Is.EqualTo(10));
    }

    [Test]
    public void DefaultWindowDimensionsTest()
    {
        // Arrange & Act
        var settings = new Settings();

        // Assert
        Assert.That(settings.WindowWidth, Is.EqualTo(1200));
        Assert.That(settings.WindowHeight, Is.EqualTo(800));
        Assert.That(settings.WindowState, Is.EqualTo("Normal"));
    }

    [Test]
    public void DefaultEditorFontSizeIs14Test()
    {
        // Arrange & Act
        var settings = new Settings();

        // Assert
        Assert.That(settings.EditorFontSize, Is.EqualTo(14));
    }

    [Test]
    public void DefaultRestoreUnsavedTabsIsTrueTest()
    {
        // Arrange & Act
        var settings = new Settings();

        // Assert
        Assert.That(settings.RestoreUnsavedTabs, Is.True);
    }

    #endregion

    #region Clone Tests

    [Test]
    public void CloneCreatesIndependentCopyTest()
    {
        // Arrange
        var original = new Settings
        {
            Theme = "Dark",
            EditorFontSize = 16,
            RecentFiles = ["file1.db", "file2.db"]
        };

        // Act
        var clone = original.Clone();

        // Assert
        Assert.That(clone, Is.Not.SameAs(original));
        Assert.That(clone.Theme, Is.EqualTo(original.Theme));
        Assert.That(clone.EditorFontSize, Is.EqualTo(original.EditorFontSize));
        Assert.That(clone.RecentFiles, Is.Not.SameAs(original.RecentFiles));
        Assert.That(clone.RecentFiles, Is.EqualTo(original.RecentFiles));
    }

    [Test]
    public void ClonePreservesAllPropertiesTest()
    {
        // Arrange
        var original = new Settings
        {
            Theme = "Dark",
            EditorFontSize = 18,
            RestoreUnsavedTabs = false,
            MaxRecentFiles = 20,
            WindowWidth = 1400,
            WindowHeight = 900,
            WindowState = "Maximized",
            RecentFiles = ["a.db", "b.db", "c.db"]
        };

        // Act
        var clone = original.Clone();

        // Assert
        Assert.That(clone.Theme, Is.EqualTo("Dark"));
        Assert.That(clone.EditorFontSize, Is.EqualTo(18));
        Assert.That(clone.RestoreUnsavedTabs, Is.False);
        Assert.That(clone.MaxRecentFiles, Is.EqualTo(20));
        Assert.That(clone.WindowWidth, Is.EqualTo(1400));
        Assert.That(clone.WindowHeight, Is.EqualTo(900));
        Assert.That(clone.WindowState, Is.EqualTo("Maximized"));
        Assert.That(clone.RecentFiles.Count, Is.EqualTo(3));
    }

    #endregion

    #region Is (Equality) Tests

    [Test]
    public void IsReturnsTrueForEqualSettingsTest()
    {
        // Arrange
        var settings1 = new Settings { Theme = "Dark", EditorFontSize = 14 };
        var settings2 = new Settings { Theme = "Dark", EditorFontSize = 14 };

        // Act & Assert
        Assert.That(settings1.Is(settings2), Is.True);
    }

    [Test]
    public void IsReturnsFalseForDifferentThemeTest()
    {
        // Arrange
        var settings1 = new Settings { Theme = "Dark" };
        var settings2 = new Settings { Theme = "Light" };

        // Act & Assert
        Assert.That(settings1.Is(settings2), Is.False);
    }

    [Test]
    public void IsReturnsFalseForDifferentFontSizeTest()
    {
        // Arrange
        var settings1 = new Settings { EditorFontSize = 14 };
        var settings2 = new Settings { EditorFontSize = 16 };

        // Act & Assert
        Assert.That(settings1.Is(settings2), Is.False);
    }

    [Test]
    public void IsReturnsFalseForNonSettingsObjectTest()
    {
        // Arrange
        var settings = new Settings();
        var other = new ConnectionInfo();

        // Act & Assert
        Assert.That(settings.Is(other), Is.False);
    }

    #endregion
}
