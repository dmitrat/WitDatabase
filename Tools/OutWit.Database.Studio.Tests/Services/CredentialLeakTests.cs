using Microsoft.Extensions.Logging;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// B1. The password of an encrypted database must never reach the log file.
///
/// This drives the SHIPPING logging path, not a capturing double: a real <see cref="FileLoggerProvider"/>
/// at the level <c>Program.ConfigureServices</c> configures, writing to a real file, under a real
/// <see cref="DatabaseService"/>. The claim being tested is "the password is on disk", so the file is
/// what gets read - a fake logger would answer a different question.
///
/// The main assertion is a NULL result: a substring is absent. A null result proves nothing without a
/// positive control, so two are built in - the log must contain the data source (we are reading the
/// right file, at a level that writes), and a separate control writes the password through the same
/// provider and requires the search to find it. Without those, this fixture would pass just as happily
/// against a provider that writes nothing at all.
/// </summary>
[TestFixture]
public class CredentialLeakTests
{
    #region Constants

    /// <summary>
    /// Distinctive on purpose: it cannot occur by accident in a path, a timestamp or a stack trace.
    /// </summary>
    private const string PASSWORD = "correct-horse-battery-staple-4711";

    #endregion

    #region Fields

    private string m_root = null!;
    private string m_logPath = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_root = Path.Combine(Path.GetTempPath(), "WitStudioSecret", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(m_root);

        m_logPath = Path.Combine(m_root, "logs", "studio.log");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_root))
                Directory.Delete(m_root, recursive: true);
        }
        catch
        {
            // a leaked handle is a finding, not a reason to fail the teardown
        }
    }

    #endregion

    #region Harness

    private ILoggerFactory NewFactory(FileLoggerProvider provider)
    {
        return LoggerFactory.Create(builder =>
        {
            builder.AddProvider(provider);
            builder.SetMinimumLevel(LogLevel.Information);
        });
    }

    private string ReadLog()
    {
        return File.Exists(m_logPath) ? File.ReadAllText(m_logPath) : string.Empty;
    }

    #endregion

    #region Tests

    /// <summary>
    /// The successful path. <c>ConnectAsync</c> logs the connection string it is about to use, and
    /// <c>ConnectionInfo.BuildConnectionString</c> appends <c>;Password={Password}</c> to it.
    /// </summary>
    [Test]
    public async Task ConnectingToAnEncryptedDatabaseKeepsThePasswordOutOfTheLogTest()
    {
        var path = Path.Combine(m_root, "encrypted.witdb");

        using var provider = new FileLoggerProvider(m_logPath);
        using var factory = NewFactory(provider);

        using (var db = new DatabaseService(factory.CreateLogger<DatabaseService>()))
        {
            var connected = await db.ConnectAsync(new ConnectionInfo
            {
                FilePath = path,
                IsEncrypted = true,
                Password = PASSWORD
            });

            Assert.That(connected, Is.True, "the case is only meaningful if the connection was actually made");

            await db.DisconnectAsync();
        }

        var written = ReadLog();

        Assert.Multiple(() =>
        {
            // CONTROL: the log exists, is being read, and records this connection at this level.
            Assert.That(written, Does.Contain("encrypted.witdb"),
                "CONTROL: without the data source in the log there is nothing to search, and the "
                + "absence of the password would mean nothing");

            Assert.That(written, Does.Not.Contain(PASSWORD),
                "the password of an encrypted database was written to the log file on disk");
        });
    }

    /// <summary>
    /// The failure path, which is the one users are asked to attach to an issue. The catch in
    /// <c>ConnectAsync</c> calls <c>BuildConnectionString()</c> a second time, for the error message.
    /// </summary>
    [Test]
    public async Task AFailedConnectionKeepsThePasswordOutOfTheLogTest()
    {
        // A directory where a database file is expected: the open fails inside the engine, so the
        // catch runs with a fully built connection string in hand.
        var path = Path.Combine(m_root, "not-a-database");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "occupant.txt"), "this is not a database");

        using var provider = new FileLoggerProvider(m_logPath);
        using var factory = NewFactory(provider);

        using (var db = new DatabaseService(factory.CreateLogger<DatabaseService>()))
        {
            await db.ConnectAsync(new ConnectionInfo
            {
                FilePath = path,
                IsEncrypted = true,
                Password = PASSWORD
            });
        }

        var written = ReadLog();

        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Contain("not-a-database"),
                "CONTROL: the attempt has to be in the log for its absence of a password to mean anything");

            Assert.That(written, Does.Not.Contain(PASSWORD),
                "the password was written to the log by the failure path - the one attached to issues");
        });
    }

    /// <summary>
    /// CONTROL for the search itself. Both assertions above are "this substring is absent"; if the
    /// provider, the path or the encoding were wrong they would pass for the wrong reason. Here the
    /// password goes through the same provider on purpose and MUST be found.
    /// </summary>
    [Test]
    public void ControlAPasswordWrittenThroughTheSameProviderIsFoundTest()
    {
        using (var provider = new FileLoggerProvider(m_logPath))
        {
            using var factory = NewFactory(provider);
            var logger = factory.CreateLogger<DatabaseService>();

            logger.LogInformation("Attempting to connect with connection string: {ConnectionString}",
                $"Data Source=x.witdb;Encryption=aes-gcm;Password={PASSWORD}");
        }

        Assert.That(ReadLog(), Does.Contain(PASSWORD),
            "CONTROL: the search must be able to find a password when one is written, or the other "
            + "two tests measure nothing");
    }

    /// <summary>
    /// The redacted form still has to be useful: a log that says nothing about the connection is a
    /// different defect from a log that says too much.
    /// </summary>
    [Test]
    public void TheRedactedConnectionStringStillNamesWhatWasAttemptedTest()
    {
        var connection = new ConnectionInfo
        {
            FilePath = @"D:\data\sales.witdb",
            IsEncrypted = true,
            Password = PASSWORD,
            IsReadOnly = true
        };

        var forLog = connection.ToLogString();

        Assert.Multiple(() =>
        {
            Assert.That(forLog, Does.Not.Contain(PASSWORD));
            Assert.That(forLog, Does.Contain(@"D:\data\sales.witdb"));
            Assert.That(forLog, Does.Contain("Encryption=aes-gcm"),
                "whether the attempt was encrypted is the first question asked about a failed open");
            Assert.That(forLog, Does.Contain("Mode=ReadOnly"));

            // And the real one is unchanged - redaction must not reach the engine.
            Assert.That(connection.BuildConnectionString(), Does.Contain($"Password={PASSWORD}"),
                "the connection string handed to the engine still has to carry the password");
        });
    }

    #endregion
}
