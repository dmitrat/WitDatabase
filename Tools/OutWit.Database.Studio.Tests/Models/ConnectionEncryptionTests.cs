using NUnit.Framework;
using OutWit.Database.Core.Builder;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.Models;

/// <summary>
/// The algorithm a connection string names (S6 in the refactoring plan's list).
///
/// <para>
/// <c>BuildConnectionString</c> wrote the literal <c>Encryption=aes-gcm</c> into every string it
/// produced. The engine creates a ChaCha20-Poly1305 database perfectly well, and Studio could not open
/// one at all: the provider was handed the wrong algorithm and answered that the password was wrong,
/// so the failure looked like the user's mistake.
/// </para>
/// </summary>
[TestFixture]
public class ConnectionEncryptionTests
{
    #region Fields

    private string m_root = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), "WitStudioCrypto", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(m_root);

        OutWit.Database.Core.BouncyCastle.BouncyCastleProviderRegistration.EnsureRegistered();
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(m_root, recursive: true);
        }
        catch
        {
            // a file still held open is not a test failure
        }
    }

    #endregion

    #region The string

    [Test]
    public void TheConnectionStringNamesTheChosenAlgorithmTest()
    {
        var connection = new ConnectionInfo
        {
            FilePath = "D:/data/sales.witdb",
            IsEncrypted = true,
            Password = "correct horse",
            EncryptionProvider = ConnectionInfo.CHACHA20
        };

        Assert.That(connection.BuildConnectionString(), Does.Contain("Encryption=chacha20-poly1305"));
    }

    [Test]
    public void AesGcmIsStillTheDefaultTest()
    {
        var connection = new ConnectionInfo
        {
            FilePath = "D:/data/sales.witdb",
            IsEncrypted = true,
            Password = "correct horse"
        };

        Assert.That(connection.BuildConnectionString(), Does.Contain("Encryption=aes-gcm"));
    }

    /// <summary>The password never reaches anything a person or a file will read.</summary>
    [Test]
    public void TheLogStringKeepsTheAlgorithmAndLosesThePasswordTest()
    {
        var connection = new ConnectionInfo
        {
            FilePath = "D:/data/sales.witdb",
            IsEncrypted = true,
            Password = "correct horse",
            EncryptionProvider = ConnectionInfo.CHACHA20
        };

        Assert.Multiple(() =>
        {
            Assert.That(connection.ToLogString(), Does.Contain("chacha20-poly1305"));
            Assert.That(connection.ToLogString(), Does.Not.Contain("correct horse"));
        });
    }

    #endregion

    #region And the string opens the database

    /// <summary>
    /// The measurement, not the claim: a ChaCha20 database is built with the engine, closed, and then
    /// opened by the connection Studio itself would make. Before the fix this failed - which is what
    /// the second case here is for.
    /// </summary>
    [Test]
    public async Task AChaCha20DatabaseOpensThroughStudiosOwnConnectionAsync()
    {
        var path = Path.Combine(m_root, "chacha.witdb");

        using (var database = new WitDatabaseBuilder()
                   .WithFilePath(path)
                   .WithBTree()
                   .WithEncryptionKey(ConnectionInfo.CHACHA20, "correct horse")
                   .Build())
        {
            database.Put("probe"u8.ToArray(), "value"u8.ToArray());
        }

        await using var studio = await StudioFixture.CreateAsync(connect: false);

        var session = await studio.Connections.OpenAsync(new ConnectionInfo
        {
            FilePath = path,
            IsEncrypted = true,
            Password = "correct horse",
            EncryptionProvider = ConnectionInfo.CHACHA20
        });

        Assert.That(session, Is.Not.Null, "Studio could not open a ChaCha20 database");
    }

    /// <summary>
    /// CONTROL, and it is what makes the case above worth having: naming the WRONG algorithm for the
    /// same file and the same password does not open it. So the case is measuring the algorithm and
    /// not merely that some encrypted database opens.
    ///
    /// This is exactly what every Studio user with a ChaCha20 database saw, and it was reported to them
    /// as a bad password.
    /// </summary>
    [Test]
    public async Task NamingTheWrongAlgorithmDoesNotOpenItAsync()
    {
        var path = Path.Combine(m_root, "chacha.witdb");

        using (var database = new WitDatabaseBuilder()
                   .WithFilePath(path)
                   .WithBTree()
                   .WithEncryptionKey(ConnectionInfo.CHACHA20, "correct horse")
                   .Build())
        {
            database.Put("probe"u8.ToArray(), "value"u8.ToArray());
        }

        await using var studio = await StudioFixture.CreateAsync(connect: false);

        var session = await studio.Connections.OpenAsync(new ConnectionInfo
        {
            FilePath = path,
            IsEncrypted = true,
            Password = "correct horse",
            EncryptionProvider = ConnectionInfo.DEFAULT_ENCRYPTION
        });

        Assert.That(session, Is.Null, "AES-GCM must not open a ChaCha20 database");
    }

    #endregion
}
