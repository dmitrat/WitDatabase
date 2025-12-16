using OutWit.Database.Core;

namespace OutWit.Database.Core.Tests;

[TestFixture]
public class DatabaseHeaderTest
{
    #region CreateNew Tests

    [Test]
    public void CreateNewDefaultPageSizeTest()
    {
        var header = DatabaseHeader.CreateNew();
        
        Assert.That(header.FormatVersion, Is.EqualTo(DatabaseConstants.FORMAT_VERSION));
        Assert.That(header.PageSize, Is.EqualTo(DatabaseConstants.DEFAULT_PAGE_SIZE));
        Assert.That(header.TotalPageCount, Is.EqualTo(1u));
        Assert.That(header.FirstFreePage, Is.EqualTo(0u));
        Assert.That(header.FreePageCount, Is.EqualTo(0u));
        Assert.That(header.SchemaRootPage, Is.EqualTo(0u));
        Assert.That(header.TransactionCounter, Is.EqualTo(0u));
        Assert.That(header.Flags, Is.EqualTo(DatabaseFlags.None));
        Assert.That(header.CheckpointCounter, Is.EqualTo(0u));
    }

    [Test]
    public void CreateNewCustomPageSizeTest()
    {
        var header = DatabaseHeader.CreateNew(pageSize: 8192);
        
        Assert.That(header.PageSize, Is.EqualTo(8192));
    }

    [Test]
    [TestCase(512)]
    [TestCase(1024)]
    [TestCase(4096)]
    [TestCase(8192)]
    [TestCase(16384)]
    [TestCase(32768)]
    public void CreateNewValidPageSizesTest(int pageSize)
    {
        var header = DatabaseHeader.CreateNew((ushort)pageSize);
        
        Assert.That(header.PageSize, Is.EqualTo(pageSize));
    }

    [Test]
    public void CreateNewInvalidPageSizeNotPowerOfTwoThrowsTest()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DatabaseHeader.CreateNew(1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => DatabaseHeader.CreateNew(5000));
    }

    [Test]
    public void CreateNewPageSizeTooSmallThrowsTest()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DatabaseHeader.CreateNew(256));
    }

    [Test]
    public void CreateNewPageSizeTooLargeThrowsTest()
    {
        // 131072 is larger than MAX_PAGE_SIZE (65536)
        // Use unchecked to create an invalid ushort value for testing
        Assert.Throws<ArgumentOutOfRangeException>(() => 
            DatabaseHeader.CreateNew(unchecked((ushort)131072)));
    }

    #endregion

    #region WriteTo/ReadFrom Tests

    [Test]
    public void WriteReadRoundtripTest()
    {
        var original = new DatabaseHeader
        {
            FormatVersion = DatabaseConstants.FORMAT_VERSION,
            PageSize = 4096,
            TotalPageCount = 1000,
            FirstFreePage = 50,
            FreePageCount = 10,
            SchemaRootPage = 1,
            TransactionCounter = 42,
            Flags = DatabaseFlags.WalMode | DatabaseFlags.Utf8Encoding,
            CheckpointCounter = 5
        };

        byte[] buffer = new byte[DatabaseConstants.DATABASE_HEADER_SIZE];
        original.WriteTo(buffer);
        
        var restored = DatabaseHeader.ReadFrom(buffer);
        
        Assert.That(restored.FormatVersion, Is.EqualTo(original.FormatVersion));
        Assert.That(restored.PageSize, Is.EqualTo(original.PageSize));
        Assert.That(restored.TotalPageCount, Is.EqualTo(original.TotalPageCount));
        Assert.That(restored.FirstFreePage, Is.EqualTo(original.FirstFreePage));
        Assert.That(restored.FreePageCount, Is.EqualTo(original.FreePageCount));
        Assert.That(restored.SchemaRootPage, Is.EqualTo(original.SchemaRootPage));
        Assert.That(restored.TransactionCounter, Is.EqualTo(original.TransactionCounter));
        Assert.That(restored.Flags, Is.EqualTo(original.Flags));
        Assert.That(restored.CheckpointCounter, Is.EqualTo(original.CheckpointCounter));
    }

    [Test]
    public void WriteToSmallBufferThrowsTest()
    {
        var header = DatabaseHeader.CreateNew();
        byte[] smallBuffer = new byte[50];
        
        Assert.Throws<ArgumentException>(() => header.WriteTo(smallBuffer));
    }

    [Test]
    public void ReadFromSmallBufferThrowsTest()
    {
        byte[] smallBuffer = new byte[50];
        
        Assert.Throws<ArgumentException>(() => DatabaseHeader.ReadFrom(smallBuffer));
    }

    [Test]
    public void ReadFromInvalidMagicBytesThrowsTest()
    {
        byte[] buffer = new byte[DatabaseConstants.DATABASE_HEADER_SIZE];
        buffer[0] = 0xFF; // Invalid magic bytes
        
        Assert.Throws<InvalidDataException>(() => DatabaseHeader.ReadFrom(buffer));
    }

    [Test]
    public void WriteToWritesMagicBytesTest()
    {
        var header = DatabaseHeader.CreateNew();
        byte[] buffer = new byte[DatabaseConstants.DATABASE_HEADER_SIZE];
        
        header.WriteTo(buffer);
        
        Assert.That(buffer[..16].SequenceEqual(DatabaseConstants.MAGIC_BYTES.ToArray()), Is.True);
    }

    [Test]
    public void WriteToLargerBufferOnlyUsesRequiredSpaceTest()
    {
        var header = DatabaseHeader.CreateNew();
        byte[] buffer = new byte[1000];
        Array.Fill(buffer, (byte)0xFF);
        
        header.WriteTo(buffer);
        
        // Bytes after header should still be 0xFF
        Assert.That(buffer[DatabaseConstants.DATABASE_HEADER_SIZE], Is.EqualTo(0xFF));
    }

    #endregion

    #region Flags Tests

    [Test]
    public void AllFlagsRoundtripTest()
    {
        var allFlags = DatabaseFlags.WalMode | DatabaseFlags.Encrypted | 
                      DatabaseFlags.ReadOnly | DatabaseFlags.Utf8Encoding;
        
        var header = DatabaseHeader.CreateNew();
        header.Flags = allFlags;
        
        byte[] buffer = new byte[DatabaseConstants.DATABASE_HEADER_SIZE];
        header.WriteTo(buffer);
        
        var restored = DatabaseHeader.ReadFrom(buffer);
        
        Assert.That(restored.Flags, Is.EqualTo(allFlags));
        Assert.That(restored.Flags.HasFlag(DatabaseFlags.WalMode), Is.True);
        Assert.That(restored.Flags.HasFlag(DatabaseFlags.Encrypted), Is.True);
        Assert.That(restored.Flags.HasFlag(DatabaseFlags.ReadOnly), Is.True);
        Assert.That(restored.Flags.HasFlag(DatabaseFlags.Utf8Encoding), Is.True);
    }

    #endregion

    #region Edge Cases

    [Test]
    public void MaxValuesRoundtripTest()
    {
        var header = new DatabaseHeader
        {
            FormatVersion = ushort.MaxValue,
            PageSize = 32768, // Largest valid power of 2 that fits in ushort for PageSize
            TotalPageCount = uint.MaxValue,
            FirstFreePage = uint.MaxValue,
            FreePageCount = uint.MaxValue,
            SchemaRootPage = uint.MaxValue,
            TransactionCounter = uint.MaxValue,
            Flags = (DatabaseFlags)uint.MaxValue,
            CheckpointCounter = uint.MaxValue
        };

        byte[] buffer = new byte[DatabaseConstants.DATABASE_HEADER_SIZE];
        header.WriteTo(buffer);
        
        var restored = DatabaseHeader.ReadFrom(buffer);
        
        Assert.That(restored.TotalPageCount, Is.EqualTo(uint.MaxValue));
        Assert.That(restored.TransactionCounter, Is.EqualTo(uint.MaxValue));
    }

    [Test]
    public void HeaderSizeConstantIsCorrectTest()
    {
        Assert.That(DatabaseConstants.DATABASE_HEADER_SIZE, Is.EqualTo(100));
    }

    #endregion
}
