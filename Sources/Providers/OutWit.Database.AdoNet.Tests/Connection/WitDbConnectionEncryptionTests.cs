using NUnit.Framework;
using System.Data;

namespace OutWit.Database.AdoNet.Tests.Connection;

/// <summary>
/// Tests for WitDbConnection encryption configurations.
/// </summary>
[TestFixture]
public class WitDbConnectionEncryptionTests
{
    #region Fields

    private string? m_testDbPath;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbEncryption_{Guid.NewGuid():N}.witdb");
    }

    [TearDown]
    public void TearDown()
    {
        if (m_testDbPath != null && File.Exists(m_testDbPath))
        {
            try { File.Delete(m_testDbPath); } catch { }
        }
    }

    #endregion

    #region AES-GCM Encryption Tests

    [Test]
    public void AesGcmWithPasswordOpensSuccessfullyTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};Encryption=aes-gcm;Password=TestPassword123");
        
        connection.Open();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }

    [Test]
    public void AesGcmWithUserAndPasswordOpensSuccessfullyTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};Encryption=aes-gcm;User=admin;Password=TestPassword123");
        
        connection.Open();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }

    [Test]
    public void AesGcmCanWriteAndReadDataTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};Encryption=aes-gcm;Password=TestPassword123");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE Test (Id INT PRIMARY KEY, Name VARCHAR(100))";
        cmd.ExecuteNonQuery();
        
        cmd.CommandText = "INSERT INTO Test VALUES (1, 'Test')";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT Name FROM Test WHERE Id = 1";
        var result = cmd.ExecuteScalar();

        Assert.That(result, Is.EqualTo("Test"));
    }

    [Test]
    public void AesGcmDataPersistsAcrossSessionsTest()
    {
        var connectionString = $"Data Source={m_testDbPath};Encryption=aes-gcm;Password=TestPassword123";

        // Create and populate database
        using (var conn1 = new WitDbConnection(connectionString))
        {
            conn1.Open();
            using var cmd = conn1.CreateCommand();
            cmd.CommandText = "CREATE TABLE Test (Id INT PRIMARY KEY, Value TEXT)";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "INSERT INTO Test VALUES (1, 'Encrypted Data')";
            cmd.ExecuteNonQuery();
        }

        // Reopen and verify
        using (var conn2 = new WitDbConnection(connectionString))
        {
            conn2.Open();
            using var cmd = conn2.CreateCommand();
            cmd.CommandText = "SELECT Value FROM Test WHERE Id = 1";
            var result = cmd.ExecuteScalar();

            Assert.That(result, Is.EqualTo("Encrypted Data"));
        }
    }

    [Test]
    public void AesGcmWrongPasswordCannotOpenExistingDatabaseTest()
    {
        // Create with one password
        using (var conn1 = new WitDbConnection($"Data Source={m_testDbPath};Encryption=aes-gcm;Password=CorrectPassword"))
        {
            conn1.Open();
            using var cmd = conn1.CreateCommand();
            cmd.CommandText = "CREATE TABLE Test (Id INT)";
            cmd.ExecuteNonQuery();
        }

        // Try to open with wrong password - should throw CryptographicException
        // because different password produces different encryption key
        using var conn2 = new WitDbConnection($"Data Source={m_testDbPath};Encryption=aes-gcm;Password=WrongPassword");
        
        Assert.That(() => conn2.Open(), Throws.InstanceOf<Exception>());
    }

    [Test]
    public void AesGcmCaseInsensitiveTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};Encryption=AES-GCM;Password=test");
        connection.Open();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }

    #endregion

    #region Fast Encryption Tests

    [Test]
    public void FastEncryptionOpensSuccessfullyTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};Encryption=aes-gcm;Password=TestPassword123;Fast Encryption=true");
        
        connection.Open();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }

    [Test]
    public void FastEncryptionCanWriteAndReadDataTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};Encryption=aes-gcm;Password=TestPassword123;Fast Encryption=true");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE Test (Id INT, Data TEXT)";
        cmd.ExecuteNonQuery();
        
        cmd.CommandText = "INSERT INTO Test VALUES (1, 'Fast encrypted data')";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT Data FROM Test WHERE Id = 1";
        var result = cmd.ExecuteScalar();

        Assert.That(result, Is.EqualTo("Fast encrypted data"));
    }

    #endregion

    #region ChaCha20 Encryption Tests

    [Test]
    public void ChaCha20WhenNotRegisteredThrowsHelpfulErrorTest()
    {
        // ChaCha20 requires BouncyCastle package
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};Encryption=chacha20-poly1305;Password=TestPassword123");

        var ex = Assert.Throws<InvalidOperationException>(() => connection.Open());
        Assert.That(ex!.Message, Does.Contain("chacha20-poly1305").Or.Contain("not registered").Or.Contain("not found"));
    }

    #endregion

    #region Custom Encryption Provider Tests

    [Test]
    public void CustomEncryptionProviderWhenNotRegisteredThrowsHelpfulErrorTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};Encryption=custom-algo;Password=TestPassword123");

        var ex = Assert.Throws<InvalidOperationException>(() => connection.Open());
        Assert.That(ex!.Message, Does.Contain("custom-algo").Or.Contain("not registered").Or.Contain("not found"));
    }

    #endregion

    #region User/Password Combination Tests

    [Test]
    public void UserPasswordProducesDifferentKeyThanPasswordOnlyTest()
    {
        var path1 = Path.Combine(Path.GetTempPath(), $"WitDb_PasswordOnly_{Guid.NewGuid():N}.witdb");
        var path2 = Path.Combine(Path.GetTempPath(), $"WitDb_UserPassword_{Guid.NewGuid():N}.witdb");

        try
        {
            // Create database with password only
            using (var conn1 = new WitDbConnection($"Data Source={path1};Encryption=aes-gcm;Password=SamePassword"))
            {
                conn1.Open();
                using var cmd = conn1.CreateCommand();
                cmd.CommandText = "CREATE TABLE Test (Id INT)";
                cmd.ExecuteNonQuery();
            }

            // Create database with user + password
            using (var conn2 = new WitDbConnection($"Data Source={path2};Encryption=aes-gcm;User=admin;Password=SamePassword"))
            {
                conn2.Open();
                using var cmd = conn2.CreateCommand();
                cmd.CommandText = "CREATE TABLE Test (Id INT)";
                cmd.ExecuteNonQuery();
            }

            // Verify files are different (different encryption keys due to different salt derivation)
            var bytes1 = File.ReadAllBytes(path1);
            var bytes2 = File.ReadAllBytes(path2);

            Assert.That(bytes1, Is.Not.EqualTo(bytes2));
        }
        finally
        {
            if (File.Exists(path1)) File.Delete(path1);
            if (File.Exists(path2)) File.Delete(path2);
        }
    }

    [Test]
    public void DifferentUsersProduceDifferentKeysTest()
    {
        var path1 = Path.Combine(Path.GetTempPath(), $"WitDb_User1_{Guid.NewGuid():N}.witdb");
        var path2 = Path.Combine(Path.GetTempPath(), $"WitDb_User2_{Guid.NewGuid():N}.witdb");

        try
        {
            using (var conn1 = new WitDbConnection($"Data Source={path1};Encryption=aes-gcm;User=user1;Password=SamePassword"))
            {
                conn1.Open();
                using var cmd = conn1.CreateCommand();
                cmd.CommandText = "CREATE TABLE Test (Id INT)";
                cmd.ExecuteNonQuery();
            }

            using (var conn2 = new WitDbConnection($"Data Source={path2};Encryption=aes-gcm;User=user2;Password=SamePassword"))
            {
                conn2.Open();
                using var cmd = conn2.CreateCommand();
                cmd.CommandText = "CREATE TABLE Test (Id INT)";
                cmd.ExecuteNonQuery();
            }

            var bytes1 = File.ReadAllBytes(path1);
            var bytes2 = File.ReadAllBytes(path2);

            Assert.That(bytes1, Is.Not.EqualTo(bytes2));
        }
        finally
        {
            if (File.Exists(path1)) File.Delete(path1);
            if (File.Exists(path2)) File.Delete(path2);
        }
    }

    #endregion

    #region Edge Cases

    [Test]
    public void EncryptionWithSpecialCharactersInPasswordWorksTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};Encryption=aes-gcm;Password=\"p@ss=w;rd\"");
        connection.Open();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }

    [Test]
    public void EncryptionWithLongPasswordWorksTest()
    {
        var longPassword = new string('x', 1000);
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};Encryption=aes-gcm;Password={longPassword}");
        connection.Open();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }

    [Test]
    public void EncryptionWithUnicodePasswordWorksTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};Encryption=aes-gcm;Password=??????123");
        connection.Open();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }

    #endregion
}
