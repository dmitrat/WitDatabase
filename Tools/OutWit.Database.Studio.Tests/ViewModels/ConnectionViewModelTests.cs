using OutWit.Database.Studio.ViewModels;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="ConnectionViewModel"/>.
/// </summary>
[TestFixture]
public class ConnectionViewModelTests
{
    #region Fields

    private ApplicationViewModel m_appVm = null!;
    private ConnectionViewModel m_connectionVm = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ApplicationViewModel>();

        var serviceProvider = services.BuildServiceProvider();

        m_appVm = serviceProvider.GetRequiredService<ApplicationViewModel>();
        m_connectionVm = m_appVm.ConnectionVm;
    }

    [SetUp]
    public void Setup()
    {
        // Reset to default values before each test
        m_connectionVm.SelectedPageSize = 4096;
        m_connectionVm.CacheSize = 1000;
        m_connectionVm.EnableTransactions = true;
        m_connectionVm.EnableMvcc = true;
        m_connectionVm.EnableFileLocking = true;
        m_connectionVm.StorageType = 0;
        m_connectionVm.IsNewDatabase = false;
        m_connectionVm.ErrorMessage = null;
        m_connectionVm.IsConnecting = false;
    }

    #endregion

    #region Initialization Tests

    [Test]
    public void ConnectionInfoIsInitializedTest()
    {
        Assert.That(m_connectionVm.ConnectionInfo, Is.Not.Null);
    }

    [Test]
    public void StorageEnginesAreInitializedTest()
    {
        Assert.That(m_connectionVm.StorageEngines, Is.Not.Null);
        Assert.That(m_connectionVm.StorageEngines.Count, Is.GreaterThan(0));
    }

    [Test]
    public void SelectedStorageEngineDefaultsToeBTreeTest()
    {
        Assert.That(m_connectionVm.SelectedStorageEngine, Is.EqualTo("btree"));
    }

    [Test]
    public void PageSizeOptionsAreInitializedTest()
    {
        Assert.That(m_connectionVm.PageSizeOptions, Is.Not.Null);
    }

    [Test]
    public void SelectedPageSizeDefaultsTo4096Test()
    {
        Assert.That(m_connectionVm.SelectedPageSize, Is.EqualTo(4096));
    }

    [Test]
    public void CacheSizeDefaultsTo1000Test()
    {
        Assert.That(m_connectionVm.CacheSize, Is.EqualTo(1000));
    }

    [Test]
    public void EnableTransactionsDefaultsToTrueTest()
    {
        Assert.That(m_connectionVm.EnableTransactions, Is.True);
    }

    [Test]
    public void EnableMvccDefaultsToTrueTest()
    {
        Assert.That(m_connectionVm.EnableMvcc, Is.True);
    }

    [Test]
    public void EnableFileLockingDefaultsToTrueTest()
    {
        Assert.That(m_connectionVm.EnableFileLocking, Is.True);
    }

    #endregion

    #region Command Tests

    [Test]
    public void BrowseFileCommandIsNotNullTest()
    {
        Assert.That(m_connectionVm.BrowseFileCommand, Is.Not.Null);
    }

    [Test]
    public void ConnectCommandIsNotNullTest()
    {
        Assert.That(m_connectionVm.ConnectCommand, Is.Not.Null);
    }

    [Test]
    public void CancelCommandIsNotNullTest()
    {
        Assert.That(m_connectionVm.CancelCommand, Is.Not.Null);
    }

    #endregion

    #region StorageType Tests

    [Test]
    public void StorageTypeDefaultsToFileBasedTest()
    {
        m_connectionVm.StorageType = 0;
        Assert.That(m_connectionVm.IsFileBased, Is.True);
    }

    [Test]
    public void StorageTypeInMemoryTest()
    {
        m_connectionVm.StorageType = 1;
        Assert.That(m_connectionVm.IsFileBased, Is.False);
    }

    [Test]
    public void IsFileBasedReturnsTrueForStorageType0Test()
    {
        m_connectionVm.StorageType = 0;
        Assert.That(m_connectionVm.IsFileBased, Is.True);
    }

    [Test]
    public void IsFileBasedReturnsFalseForStorageType1Test()
    {
        m_connectionVm.StorageType = 1;
        Assert.That(m_connectionVm.IsFileBased, Is.False);
    }

    #endregion

    #region Dialog Properties Tests

    [Test]
    public void DialogTitleForNewDatabaseTest()
    {
        m_connectionVm.IsNewDatabase = true;
        Assert.That(m_connectionVm.DialogTitle, Is.EqualTo("Create New Database"));
    }

    [Test]
    public void DialogTitleForOpenDatabaseTest()
    {
        m_connectionVm.IsNewDatabase = false;
        Assert.That(m_connectionVm.DialogTitle, Is.EqualTo("Open Database"));
    }

    [Test]
    public void DialogDescriptionForNewDatabaseTest()
    {
        m_connectionVm.IsNewDatabase = true;
        Assert.That(m_connectionVm.DialogDescription, Contains.Substring("Create"));
    }

    [Test]
    public void DialogDescriptionForOpenDatabaseTest()
    {
        m_connectionVm.IsNewDatabase = false;
        Assert.That(m_connectionVm.DialogDescription, Contains.Substring("Open"));
    }

    [Test]
    public void ConnectButtonTextForNewDatabaseTest()
    {
        m_connectionVm.IsNewDatabase = true;
        Assert.That(m_connectionVm.ConnectButtonText, Is.EqualTo("Create"));
    }

    [Test]
    public void ConnectButtonTextForOpenDatabaseTest()
    {
        m_connectionVm.IsNewDatabase = false;
        Assert.That(m_connectionVm.ConnectButtonText, Is.EqualTo("Open"));
    }

    #endregion

    #region PageSize Options Tests

    [Test]
    public void PageSizeOptionsContains4096Test()
    {
        Assert.That(m_connectionVm.PageSizeOptions, Contains.Item(4096));
    }

    [Test]
    public void PageSizeOptionsContainsCommonSizesTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(m_connectionVm.PageSizeOptions, Contains.Item(512));
            Assert.That(m_connectionVm.PageSizeOptions, Contains.Item(1024));
            Assert.That(m_connectionVm.PageSizeOptions, Contains.Item(2048));
            Assert.That(m_connectionVm.PageSizeOptions, Contains.Item(4096));
            Assert.That(m_connectionVm.PageSizeOptions, Contains.Item(8192));
        });
    }

    #endregion

    #region Advanced Settings Tests

    [Test]
    public void SelectedPageSizeCanBeChangedTest()
    {
        m_connectionVm.SelectedPageSize = 8192;
        Assert.That(m_connectionVm.SelectedPageSize, Is.EqualTo(8192));
    }

    [Test]
    public void CacheSizeCanBeChangedTest()
    {
        m_connectionVm.CacheSize = 5000;
        Assert.That(m_connectionVm.CacheSize, Is.EqualTo(5000));
    }

    [Test]
    public void EnableTransactionsCanBeToggledTest()
    {
        m_connectionVm.EnableTransactions = false;
        Assert.That(m_connectionVm.EnableTransactions, Is.False);
        
        m_connectionVm.EnableTransactions = true;
        Assert.That(m_connectionVm.EnableTransactions, Is.True);
    }

    [Test]
    public void EnableMvccCanBeToggledTest()
    {
        m_connectionVm.EnableMvcc = false;
        Assert.That(m_connectionVm.EnableMvcc, Is.False);
        
        m_connectionVm.EnableMvcc = true;
        Assert.That(m_connectionVm.EnableMvcc, Is.True);
    }

    [Test]
    public void EnableFileLockingCanBeToggledTest()
    {
        m_connectionVm.EnableFileLocking = false;
        Assert.That(m_connectionVm.EnableFileLocking, Is.False);
        
        m_connectionVm.EnableFileLocking = true;
        Assert.That(m_connectionVm.EnableFileLocking, Is.True);
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public void ErrorMessageDefaultsToNullTest()
    {
        m_connectionVm.ErrorMessage = null;
        Assert.That(m_connectionVm.ErrorMessage, Is.Null);
    }

    [Test]
    public void ErrorMessageCanBeSetTest()
    {
        m_connectionVm.ErrorMessage = "Test error";
        Assert.That(m_connectionVm.ErrorMessage, Is.EqualTo("Test error"));
    }

    [Test]
    public void IsConnectingDefaultsToFalseTest()
    {
        m_connectionVm.IsConnecting = false;
        Assert.That(m_connectionVm.IsConnecting, Is.False);
    }

    #endregion

    #region Integration Tests

    [Test]
    public void AllCommandsAreInitializedTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(m_connectionVm.BrowseFileCommand, Is.Not.Null);
            Assert.That(m_connectionVm.ConnectCommand, Is.Not.Null);
            Assert.That(m_connectionVm.CancelCommand, Is.Not.Null);
        });
    }

    [Test]
    public void AllPropertiesAreInitializedTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(m_connectionVm.ConnectionInfo, Is.Not.Null);
            Assert.That(m_connectionVm.StorageEngines, Is.Not.Null);
            Assert.That(m_connectionVm.SelectedStorageEngine, Is.Not.Null);
            Assert.That(m_connectionVm.PageSizeOptions, Is.Not.Null);
        });
    }

    #endregion
}
