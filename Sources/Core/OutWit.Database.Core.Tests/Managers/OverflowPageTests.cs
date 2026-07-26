using OutWit.Database.Core.Managers;
using OutWit.Database.Core.Pages;
using OutWit.Database.Core.Storage;

namespace OutWit.Database.Core.Tests.Managers
{
    /// <summary>
    /// Tests for OverflowPageManager - large BLOB support.
    /// </summary>
    [TestFixture]
    public class OverflowPageTest : IDisposable
    {
        private string m_testDir = null!;

        [SetUp]
        public void SetUp()
        {
            m_testDir = Path.Combine(Path.GetTempPath(), $"overflow_{Guid.NewGuid():N}");
            Directory.CreateDirectory(m_testDir);
        }

        [TearDown]
        public void TearDown()
        {
            Dispose();
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(m_testDir))
                    Directory.Delete(m_testDir, recursive: true);
            }
            catch { }
        }

        #region Constructor Tests

        [Test]
        public void ConstructorNullPageManagerThrowsTest()
        {
            Assert.Throws<ArgumentNullException>(() => new PageManagerOverflow(null!));
        }

        [Test]
        public void ConstructorDefaultMaxInlineSizeTest()
        {
            using var storage = new StorageMemory(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager);
            
            // Default is PageSize / 4
            Assert.That(overflowManager.MaxInlineSize, Is.EqualTo(pageManager.PageSize / 4));
        }

        [Test]
        public void CustomMaxInlineSizeTest()
        {
            var dbPath = Path.Combine(m_testDir, "overflow_custom.db");
            using var storage = new StorageFile(dbPath);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager, maxInlineSize: 512);
        
            Assert.That(overflowManager.MaxInlineSize, Is.EqualTo(512));
            Assert.That(overflowManager.NeedsOverflow(500), Is.False);
            Assert.That(overflowManager.NeedsOverflow(600), Is.True);
        }

        #endregion

        #region NeedsOverflow Tests

        [Test]
        public void NeedsOverflowTest()
        {
            var dbPath = Path.Combine(m_testDir, "overflow_needs.db");
            using var storage = new StorageFile(dbPath);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager);
        
            // Small values don't need overflow
            Assert.That(overflowManager.NeedsOverflow(100), Is.False);
            Assert.That(overflowManager.NeedsOverflow(overflowManager.MaxInlineSize), Is.False);
        
            // Large values need overflow
            Assert.That(overflowManager.NeedsOverflow(overflowManager.MaxInlineSize + 1), Is.True);
        }

        [Test]
        public void NeedsOverflowZeroLengthTest()
        {
            using var storage = new StorageMemory(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager);
            
            Assert.That(overflowManager.NeedsOverflow(0), Is.False);
        }

        #endregion

        #region StoreOverflow Tests

        [Test]
        public void StoreAndReadSmallOverflowTest()
        {
            var dbPath = Path.Combine(m_testDir, "overflow_small.db");
            using var storage = new StorageFile(dbPath);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager);
        
            // Create data larger than MaxInlineSize but fits in one overflow page
            var data = new byte[overflowManager.MaxInlineSize + 100];
            new Random(42).NextBytes(data);
        
            // Store
            var firstPage = overflowManager.StoreOverflow(data);
            Assert.That(firstPage, Is.GreaterThan(0u));
        
            // Read back
            var result = overflowManager.ReadOverflow(firstPage);
            Assert.That(result, Is.EqualTo(data));
        }

        [Test]
        public void StoreAndReadLargeOverflowTest()
        {
            var dbPath = Path.Combine(m_testDir, "overflow_large.db");
            using var storage = new StorageFile(dbPath);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager);
        
            // Create data that spans multiple overflow pages (100KB)
            var data = new byte[100 * 1024];
            new Random(42).NextBytes(data);
        
            // Store
            var firstPage = overflowManager.StoreOverflow(data);
        
            // Read back
            var result = overflowManager.ReadOverflow(firstPage);
            Assert.That(result, Is.EqualTo(data));
        }

        [Test]
        public void StoreOverflowTooSmallThrowsTest()
        {
            using var storage = new StorageMemory(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager);
            
            var smallData = new byte[100]; // Less than MaxInlineSize
            
            Assert.Throws<ArgumentException>(() => overflowManager.StoreOverflow(smallData));
        }

        [Test]
        public void StoreOverflowExactlyMaxInlineSizeThrowsTest()
        {
            using var storage = new StorageMemory(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager);
            
            var exactData = new byte[overflowManager.MaxInlineSize];
            
            Assert.Throws<ArgumentException>(() => overflowManager.StoreOverflow(exactData));
        }

        [Test]
        public void StoreMultipleOverflowChainsTest()
        {
            using var storage = new StorageMemory(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager);
            
            var data1 = new byte[overflowManager.MaxInlineSize + 100];
            var data2 = new byte[overflowManager.MaxInlineSize + 200];
            var data3 = new byte[overflowManager.MaxInlineSize + 300];
            
            new Random(1).NextBytes(data1);
            new Random(2).NextBytes(data2);
            new Random(3).NextBytes(data3);
            
            var page1 = overflowManager.StoreOverflow(data1);
            var page2 = overflowManager.StoreOverflow(data2);
            var page3 = overflowManager.StoreOverflow(data3);
            
            Assert.That(overflowManager.ReadOverflow(page1), Is.EqualTo(data1));
            Assert.That(overflowManager.ReadOverflow(page2), Is.EqualTo(data2));
            Assert.That(overflowManager.ReadOverflow(page3), Is.EqualTo(data3));
        }

        #endregion

        #region FreeOverflow Pin Release Tests

        /// <summary>
        /// FreeOverflow read the next page number into <c>currentPage</c> and then released
        /// <c>currentPage</c>, so it unpinned the NEXT link instead of the page it had just pinned.
        /// </summary>
        /// <remarks>
        /// The per-link effects mostly cancel - each link was pinned by its own iteration and
        /// released by the previous one - but the head of every chain was pinned and never released,
        /// and the tail was released once too often. Freeing many distinct chains therefore burns one
        /// cache slot per chain permanently. Store all the chains first so their heads are distinct
        /// pages; freeing in a loop would reuse the same head page and hide the leak behind a single
        /// slot.
        /// </remarks>
        [Test]
        public void FreeOverflowDoesNotLeakAPinPerChainTest()
        {
            using var storage = new StorageMemory(initialPageCount: 0);
            using var pageManager = new PageManager(storage, cacheSize: 16);
            using var overflowManager = new PageManagerOverflow(pageManager);

            var data = new byte[pageManager.PageSize * 3];
            Random.Shared.NextBytes(data);

            var chains = new List<uint>();
            for (var i = 0; i < 40; i++)
                chains.Add(overflowManager.StoreOverflow(data));

            Assert.DoesNotThrow(() =>
            {
                foreach (var firstPage in chains)
                    overflowManager.FreeOverflow(firstPage);
            }, "Freeing many chains must not leave their head pages pinned");

            // The cache must still be usable afterwards.
            Assert.DoesNotThrow(
                () => overflowManager.FreeOverflow(overflowManager.StoreOverflow(data)),
                "The page cache must still have evictable slots after freeing many chains");
        }

        [Test]
        public void FreeOverflowReturnsPagesForReuseTest()
        {
            using var storage = new StorageMemory(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager);

            var data = new byte[pageManager.PageSize * 4];
            Random.Shared.NextBytes(data);

            overflowManager.FreeOverflow(overflowManager.StoreOverflow(data));
            var afterFirstCycle = pageManager.TotalPageCount;

            for (var i = 0; i < 10; i++)
                overflowManager.FreeOverflow(overflowManager.StoreOverflow(data));

            Assert.That(pageManager.TotalPageCount, Is.EqualTo(afterFirstCycle),
                "Freed overflow pages must be reused rather than growing the file every cycle");
        }

        [Test]
        public void FreedOverflowChainDataIsStillReadableWhileHeldTest()
        {
            // Guards the fix itself: the page being released must be the one that was pinned, so a
            // concurrent reader of a different chain is unaffected.
            using var storage = new StorageMemory(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager);

            var keep = new byte[pageManager.PageSize * 3];
            var discard = new byte[pageManager.PageSize * 3];
            Random.Shared.NextBytes(keep);
            Random.Shared.NextBytes(discard);

            var keepPage = overflowManager.StoreOverflow(keep);
            var discardPage = overflowManager.StoreOverflow(discard);

            overflowManager.FreeOverflow(discardPage);

            Assert.That(overflowManager.ReadOverflow(keepPage), Is.EqualTo(keep));
        }

        #endregion

        #region ReadOverflow with Span Tests

        [Test]
        public void ReadOverflowIntoSpanTest()
        {
            using var storage = new StorageMemory(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager);
            
            var data = new byte[overflowManager.MaxInlineSize + 100];
            new Random(42).NextBytes(data);
            
            var firstPage = overflowManager.StoreOverflow(data);
            
            // Read into existing buffer
            var buffer = new byte[data.Length + 100]; // Extra space
            int bytesRead = overflowManager.ReadOverflow(firstPage, buffer);
            
            Assert.That(bytesRead, Is.EqualTo(data.Length));
            Assert.That(buffer[..bytesRead], Is.EqualTo(data));
        }

        [Test]
        public void ReadOverflowIntoSpanTooSmallThrowsTest()
        {
            using var storage = new StorageMemory(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager);
            
            var data = new byte[overflowManager.MaxInlineSize + 100];
            var firstPage = overflowManager.StoreOverflow(data);
            
            var smallBuffer = new byte[50];
            
            Assert.Throws<ArgumentException>(() => overflowManager.ReadOverflow(firstPage, smallBuffer));
        }

        [Test]
        public void ReadOverflowLargeIntoSpanTest()
        {
            using var storage = new StorageMemory(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager);
            
            // Multi-page overflow
            var data = new byte[50 * 1024];
            new Random(42).NextBytes(data);
            
            var firstPage = overflowManager.StoreOverflow(data);
            
            var buffer = new byte[data.Length];
            int bytesRead = overflowManager.ReadOverflow(firstPage, buffer);
            
            Assert.That(bytesRead, Is.EqualTo(data.Length));
            Assert.That(buffer, Is.EqualTo(data));
        }

        #endregion

        #region GetOverflowLength Tests

        [Test]
        public void GetOverflowLengthTest()
        {
            using var storage = new StorageMemory(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager);
            
            var data = new byte[overflowManager.MaxInlineSize + 500];
            new Random(42).NextBytes(data);
            
            var firstPage = overflowManager.StoreOverflow(data);
            
            int length = overflowManager.GetOverflowLength(firstPage);
            
            Assert.That(length, Is.EqualTo(data.Length));
        }

        [Test]
        public void GetOverflowLengthInvalidPageThrowsTest()
        {
            using var storage = new StorageMemory(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager);
            
            var (pn, _) = pageManager.AllocatePage(PageType.Leaf);
            pageManager.ReleasePage(pn);
            
            Assert.Throws<InvalidDataException>(() => overflowManager.GetOverflowLength(pn));
        }

        #endregion

        #region GetOverflowInfo Tests

        [Test]
        public void GetOverflowInfoTest()
        {
            var dbPath = Path.Combine(m_testDir, "overflow_info.db");
            using var storage = new StorageFile(dbPath);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager);
        
            // Create data
            var data = new byte[50 * 1024]; // 50KB
            new Random(42).NextBytes(data);
        
            var firstPage = overflowManager.StoreOverflow(data);
        
            // Get info
            var info = overflowManager.GetOverflowInfo(firstPage);
        
            Assert.That(info.FirstPage, Is.EqualTo(firstPage));
            Assert.That(info.TotalLength, Is.EqualTo(data.Length));
            Assert.That(info.PageCount, Is.GreaterThan(1));
        }

        [Test]
        public void GetOverflowInfoSinglePageTest()
        {
            using var storage = new StorageMemory(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager);
            
            // Data that fits in single overflow page
            var data = new byte[overflowManager.MaxInlineSize + 1];
            new Random(42).NextBytes(data);
            
            var firstPage = overflowManager.StoreOverflow(data);
            var info = overflowManager.GetOverflowInfo(firstPage);
            
            Assert.That(info.TotalLength, Is.EqualTo(data.Length));
            Assert.That(info.PageCount, Is.EqualTo(1));
        }

        [Test]
        public void GetOverflowInfoInvalidPageThrowsTest()
        {
            using var storage = new StorageMemory(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager);
            
            // Allocate a non-overflow page
            var (pn, _) = pageManager.AllocatePage(PageType.Leaf);
            pageManager.ReleasePage(pn);
            
            Assert.Throws<InvalidDataException>(() => overflowManager.GetOverflowInfo(pn));
        }

        #endregion

        #region FreeOverflow Tests

        [Test]
        public void FreeOverflowTest()
        {
            var dbPath = Path.Combine(m_testDir, "overflow_free.db");
            using var storage = new StorageFile(dbPath);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager);
        
            var initialFreeCount = pageManager.FreePageCount;
        
            // Store large data
            var data = new byte[20 * 1024];
            new Random(42).NextBytes(data);
        
            var firstPage = overflowManager.StoreOverflow(data);
            var info = overflowManager.GetOverflowInfo(firstPage);
        
            // Free it
            overflowManager.FreeOverflow(firstPage);
        
            // Should have freed all pages in the chain
            Assert.That(pageManager.FreePageCount, Is.EqualTo(initialFreeCount + (uint)info.PageCount));
        }

        [Test]
        public void FreeOverflowSinglePageTest()
        {
            using var storage = new StorageMemory(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager);
            
            var data = new byte[overflowManager.MaxInlineSize + 1];
            var firstPage = overflowManager.StoreOverflow(data);
            
            Assert.That(pageManager.FreePageCount, Is.EqualTo(0u));
            
            overflowManager.FreeOverflow(firstPage);
            
            Assert.That(pageManager.FreePageCount, Is.EqualTo(1u));
        }

        [Test]
        public void FreeAndReallocateOverflowTest()
        {
            using var storage = new StorageMemory(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager);
            
            var data1 = new byte[overflowManager.MaxInlineSize + 100];
            new Random(1).NextBytes(data1);
            
            var firstPage1 = overflowManager.StoreOverflow(data1);
            overflowManager.FreeOverflow(firstPage1);
            
            // Allocate again - should reuse freed pages
            var data2 = new byte[overflowManager.MaxInlineSize + 100];
            new Random(2).NextBytes(data2);
            
            var firstPage2 = overflowManager.StoreOverflow(data2);
            
            Assert.That(firstPage2, Is.EqualTo(firstPage1)); // Reused the freed page
            Assert.That(overflowManager.ReadOverflow(firstPage2), Is.EqualTo(data2));
        }

        #endregion

        #region Persistence Tests

        [Test]
        public void PersistenceAfterFlushTest()
        {
            var dbPath = Path.Combine(m_testDir, "overflow_persist.db");
            var data = new byte[10 * 1024];
            new Random(42).NextBytes(data);
            uint firstPage;
        
            // Write and flush
            using (var storage = new StorageFile(dbPath))
            using (var pageManager = new PageManager(storage))
            using (var overflowManager = new PageManagerOverflow(pageManager))
            {
                firstPage = overflowManager.StoreOverflow(data);
                pageManager.Flush();
            }
        
            // Reopen and read
            using (var storage = new StorageFile(dbPath))
            using (var pageManager = new PageManager(storage))
            using (var overflowManager = new PageManagerOverflow(pageManager))
            {
                var result = overflowManager.ReadOverflow(firstPage);
                Assert.That(result, Is.EqualTo(data));
            }
        }

        [Test]
        public void PersistenceWithMultipleChainsTest()
        {
            var dbPath = Path.Combine(m_testDir, "overflow_multi.db");
            var data1 = new byte[20 * 1024];
            var data2 = new byte[30 * 1024];
            new Random(1).NextBytes(data1);
            new Random(2).NextBytes(data2);
            uint firstPage1, firstPage2;
        
            using (var storage = new StorageFile(dbPath))
            using (var pageManager = new PageManager(storage))
            using (var overflowManager = new PageManagerOverflow(pageManager))
            {
                firstPage1 = overflowManager.StoreOverflow(data1);
                firstPage2 = overflowManager.StoreOverflow(data2);
            }
        
            using (var storage = new StorageFile(dbPath))
            using (var pageManager = new PageManager(storage))
            using (var overflowManager = new PageManagerOverflow(pageManager))
            {
                Assert.That(overflowManager.ReadOverflow(firstPage1), Is.EqualTo(data1));
                Assert.That(overflowManager.ReadOverflow(firstPage2), Is.EqualTo(data2));
            }
        }

        #endregion

        #region Dispose Tests

        [Test]
        public void DisposeMultipleTimesDoesNotThrowTest()
        {
            using var storage = new StorageMemory(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
            var overflowManager = new PageManagerOverflow(pageManager);
            
            overflowManager.Dispose();
            overflowManager.Dispose();
            
            // Should not throw
            Assert.Pass();
        }

        [Test]
        public void OperationsAfterDisposeThrowTest()
        {
            using var storage = new StorageMemory(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
            var overflowManager = new PageManagerOverflow(pageManager);
            overflowManager.Dispose();
            
            var data = new byte[2000];
            
            Assert.Throws<ObjectDisposedException>(() => overflowManager.StoreOverflow(data));
            Assert.Throws<ObjectDisposedException>(() => overflowManager.ReadOverflow(1));
            Assert.Throws<ObjectDisposedException>(() => overflowManager.FreeOverflow(1));
            Assert.Throws<ObjectDisposedException>(() => overflowManager.GetOverflowInfo(1));
        }

        #endregion

        #region Properties Tests

        [Test]
        public void DataSizePerPageTest()
        {
            using var storage = new StorageMemory(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
            using var overflowManager = new PageManagerOverflow(pageManager);
            
            // DataSizePerPage = PageSize - OverflowHeaderSize (16)
            Assert.That(overflowManager.DataSizePerPage, Is.EqualTo(pageManager.PageSize - 16));
        }

        #endregion
    }
}
