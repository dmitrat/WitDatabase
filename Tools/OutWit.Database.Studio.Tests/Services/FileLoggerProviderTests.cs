using Microsoft.Extensions.Logging;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// S7. Studio is a WinExe, which has no console, so the console provider it used to configure alone
/// wrote every message - including DatabaseService's own catch - nowhere at all. These check that the
/// file provider records what the application says, and that it cannot take the application down while
/// trying to describe it.
/// </summary>
[TestFixture]
public class FileLoggerProviderTests
{
    #region Fields

    private string m_root = null!;
    private string m_logPath = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_root = Path.Combine(Path.GetTempPath(), "WitStudioLog", Guid.NewGuid().ToString("N"));
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
            // ignore cleanup failures
        }
    }

    #endregion

    #region Tests

    [Test]
    public void AnErrorWithItsExceptionReachesTheFileTest()
    {
        using (var provider = new FileLoggerProvider(m_logPath))
        {
            var logger = provider.CreateLogger("Probe");

            logger.LogError(new InvalidOperationException("the-inner-message"), "connect failed for {Path}", "db.witdb");
        }

        var written = File.ReadAllText(m_logPath);

        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Contain("[ERR]"));
            Assert.That(written, Does.Contain("Probe"));
            Assert.That(written, Does.Contain("connect failed for db.witdb"));
            Assert.That(written, Does.Contain("the-inner-message"),
                "the exception is the whole reason the log exists - it must be written, not just the message");
        });
    }

    /// <summary>
    /// CONTROL. Without this, "the log contains what we asked for" would pass for a provider that
    /// writes everything regardless of level.
    /// </summary>
    [Test]
    public void ControlAMessageBelowTheMinimumLevelIsNotWrittenTest()
    {
        using (var provider = new FileLoggerProvider(m_logPath, LogLevel.Warning))
        {
            var logger = provider.CreateLogger("Probe");

            logger.LogInformation("this-must-not-appear");
            logger.LogWarning("this-must-appear");
        }

        var written = File.ReadAllText(m_logPath);

        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Not.Contain("this-must-not-appear"));
            Assert.That(written, Does.Contain("this-must-appear"));
        });
    }

    /// <summary>
    /// A logger that throws turns a recoverable failure into a crash, which is the opposite of what
    /// this was added for. Measured against a path that cannot be created.
    /// </summary>
    [Test]
    public void AnUnwritableLogDoesNotThrowIntoTheApplicationTest()
    {
        var impossible = Path.Combine(m_root, "a-file-not-a-directory");
        Directory.CreateDirectory(m_root);
        File.WriteAllText(impossible, "this is a file");

        // Asking for a log INSIDE a path that is a file: neither the directory nor the write can work.
        using var provider = new FileLoggerProvider(Path.Combine(impossible, "studio.log"));
        var logger = provider.CreateLogger("Probe");

        Assert.DoesNotThrow(() => logger.LogError("this cannot be written anywhere"));
    }

    #endregion
}
