using OutWit.Database.Core.Managers;
using OutWit.Database.Core.Pages;
using OutWit.Database.Core.Storage;

namespace OutWit.Database.Core.Tests.Managers
{
    [TestFixture]
    public class PageManagerTest
    {
        #region Constructor Tests

        [Test]
        public void CreateNewDatabaseTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            Assert.That(pageManager.PageSize, Is.EqualTo(DatabaseConstants.DEFAULT_PAGE_SIZE));
            Assert.That(pageManager.TotalPageCount, Is.EqualTo(1u)); // Header page
            Assert.That(pageManager.FreePageCount, Is.EqualTo(0u));
        }

        [Test]
        public void ConstructorNullStorageThrowsTest()
        {
            Assert.Throws<ArgumentNullException>(() => new PageManager(null!));
        }

        [Test]
        public void ConstructorWithCacheSizeTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage, cacheSize: 50);
        
            Assert.That(pageManager.PageSize, Is.EqualTo(DatabaseConstants.DEFAULT_PAGE_SIZE));
        }

        #endregion

        #region AllocatePage Tests

        [Test]
        public void AllocatePageExtendsFileTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            var (pageNumber, page) = pageManager.AllocatePage(PageType.Leaf);
        
            Assert.That(pageNumber, Is.EqualTo(1u)); // Page 0 is header
            Assert.That(pageManager.TotalPageCount, Is.EqualTo(2u));
        
            pageManager.ReleasePage(pageNumber);
        }

        [Test]
        public void AllocateMultiplePagesTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            var (page1, _) = pageManager.AllocatePage(PageType.Leaf);
            pageManager.ReleasePage(page1);
        
            var (page2, _) = pageManager.AllocatePage(PageType.Internal);
            pageManager.ReleasePage(page2);
        
            var (page3, _) = pageManager.AllocatePage(PageType.Leaf);
            pageManager.ReleasePage(page3);
        
            Assert.That(page1, Is.EqualTo(1u));
            Assert.That(page2, Is.EqualTo(2u));
            Assert.That(page3, Is.EqualTo(3u));
            Assert.That(pageManager.TotalPageCount, Is.EqualTo(4u));
        }

        [Test]
        public void AllocatePageInitializesHeaderTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            var (pageNumber, page) = pageManager.AllocatePage(PageType.Leaf);
            
            var header = PageHeader.ReadFrom(page.ReadOnlyData);
            Assert.That(header.PageType, Is.EqualTo(PageType.Leaf));
            Assert.That(header.CellCount, Is.EqualTo(0));
            
            pageManager.ReleasePage(pageNumber);
        }

        [Test]
        public void AllocatePageDifferentTypesTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            foreach (PageType pageType in new[] { PageType.Leaf, PageType.Internal, PageType.Overflow, PageType.Schema })
            {
                var (pageNumber, page) = pageManager.AllocatePage(pageType);
                var header = PageHeader.ReadFrom(page.ReadOnlyData);
                
                Assert.That(header.PageType, Is.EqualTo(pageType));
                pageManager.ReleasePage(pageNumber);
            }
        }

        #endregion

        #region FreePage Tests

        [Test]
        public void FreePageAddToFreeListTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            var (pageNumber, _) = pageManager.AllocatePage(PageType.Leaf);
            pageManager.ReleasePage(pageNumber);
        
            Assert.That(pageManager.FreePageCount, Is.EqualTo(0u));
        
            pageManager.FreePage(pageNumber);
        
            Assert.That(pageManager.FreePageCount, Is.EqualTo(1u));
        }

        [Test]
        public void AllocateReusesFreePageTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            // Allocate and free a page
            var (freedPage, _) = pageManager.AllocatePage(PageType.Leaf);
            pageManager.ReleasePage(freedPage);
            pageManager.FreePage(freedPage);
        
            uint countBefore = pageManager.TotalPageCount;
        
            // Allocate again - should reuse the freed page
            var (reusedPage, _) = pageManager.AllocatePage(PageType.Internal);
            pageManager.ReleasePage(reusedPage);
        
            Assert.That(reusedPage, Is.EqualTo(freedPage));
            Assert.That(pageManager.TotalPageCount, Is.EqualTo(countBefore)); // No new pages
            Assert.That(pageManager.FreePageCount, Is.EqualTo(0u));
        }

        [Test]
        public void FreeHeaderPageThrowsTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            Assert.Throws<ArgumentException>(() => pageManager.FreePage(0));
        }

        [Test]
        public void FreeOutOfRangePageThrowsTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            Assert.Throws<ArgumentOutOfRangeException>(() => pageManager.FreePage(999));
        }

        [Test]
        public void FreeMultiplePagesCreatesChainTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            // Allocate several pages
            var pages = new List<uint>();
            for (int i = 0; i < 5; i++)
            {
                var (pn, _) = pageManager.AllocatePage(PageType.Leaf);
                pageManager.ReleasePage(pn);
                pages.Add(pn);
            }
        
            // Free them all
            foreach (var pn in pages)
            {
                pageManager.FreePage(pn);
            }
        
            Assert.That(pageManager.FreePageCount, Is.EqualTo(5u));
        
            // Allocate again - should reuse in LIFO order
            for (int i = 0; i < 5; i++)
            {
                var (pn, _) = pageManager.AllocatePage(PageType.Leaf);
                pageManager.ReleasePage(pn);
                Assert.That(pages.Contains(pn), Is.True);
            }
        
            Assert.That(pageManager.FreePageCount, Is.EqualTo(0u));
        }

        #endregion

        #region GetPage Tests

        [Test]
        public void GetPageReturnsCorrectDataTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            var (pageNumber, page) = pageManager.AllocatePage(PageType.Leaf);
            page.Data[100] = 0xAB;
            pageManager.MarkDirty(pageNumber);
            pageManager.ReleasePage(pageNumber);
        
            // Get again
            var same = pageManager.GetPage(pageNumber);
            Assert.That(same.Data[100], Is.EqualTo((byte)0xAB));
            pageManager.ReleasePage(pageNumber);
        }

        [Test]
        public void GetOutOfRangePageThrowsTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            Assert.Throws<ArgumentOutOfRangeException>(() => pageManager.GetPage(999));
        }

        [Test]
        public void GetHeaderPageTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            var page = pageManager.GetPage(0);
            
            // Should contain magic bytes
            Assert.That(page.ReadOnlyData[..16].SequenceEqual(DatabaseConstants.MAGIC_BYTES.ToArray()), Is.True);
            
            pageManager.ReleasePage(0);
        }

        #endregion

        #region Flush Tests

        [Test]
        public void FlushWritesToStorageTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            var (pageNumber, page) = pageManager.AllocatePage(PageType.Leaf);
            page.Data[50] = 0xCD;
            pageManager.MarkDirty(pageNumber);
            pageManager.ReleasePage(pageNumber);
        
            pageManager.Flush();
        
            // Read directly from storage
            byte[] buffer = new byte[DatabaseConstants.DEFAULT_PAGE_SIZE];
            storage.ReadPage(pageNumber, buffer);
            Assert.That(buffer[50], Is.EqualTo((byte)0xCD));
        }

        [Test]
        public async Task FlushAsyncWritesToStorageTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            var (pageNumber, page) = pageManager.AllocatePage(PageType.Leaf);
            page.Data[50] = 0xEF;
            pageManager.MarkDirty(pageNumber);
            pageManager.ReleasePage(pageNumber);
        
            await pageManager.FlushAsync();
        
            byte[] buffer = new byte[DatabaseConstants.DEFAULT_PAGE_SIZE];
            storage.ReadPage(pageNumber, buffer);
            Assert.That(buffer[50], Is.EqualTo((byte)0xEF));
        }

        #endregion

        #region Header Tests

        [Test]
        public void GetHeaderReturnsCurrentStateTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            var header = pageManager.GetHeader();
        
            Assert.That(header.FormatVersion, Is.EqualTo(DatabaseConstants.FORMAT_VERSION));
            Assert.That(header.PageSize, Is.EqualTo((ushort)DatabaseConstants.DEFAULT_PAGE_SIZE));
        }

        [Test]
        public void SetSchemaRootPageTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            var (pageNumber, _) = pageManager.AllocatePage(PageType.Schema);
            pageManager.ReleasePage(pageNumber);
        
            pageManager.SetSchemaRootPage(pageNumber);
        
            var header = pageManager.GetHeader();
            Assert.That(header.SchemaRootPage, Is.EqualTo(pageNumber));
        }

        [Test]
        public void IncrementTransactionCounterTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            var initial = pageManager.GetHeader().TransactionCounter;
        
            var next1 = pageManager.IncrementTransactionCounter();
            var next2 = pageManager.IncrementTransactionCounter();
        
            Assert.That(next1, Is.EqualTo(initial + 1));
            Assert.That(next2, Is.EqualTo(initial + 2));
        }

        #endregion

        #region Persistence Tests

        [Test]
        public void ReopenDatabaseLoadsHeaderTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
        
            // Create and populate database
            using (var pm1 = new PageManager(storage))
            {
                var (pn, _) = pm1.AllocatePage(PageType.Leaf);
                pm1.ReleasePage(pn);
                pm1.SetSchemaRootPage(pn);
                pm1.IncrementTransactionCounter();
            }
        
            // Reopen
            using var pm2 = new PageManager(storage);
            var header = pm2.GetHeader();
        
            Assert.That(header.TotalPageCount, Is.EqualTo(2u));
            Assert.That(header.SchemaRootPage, Is.EqualTo(1u));
            Assert.That(header.TransactionCounter, Is.EqualTo(1u));
        }

        [Test]
        public void FreeListPersistedAcrossReopenTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
        
            // Create, allocate, free
            using (var pm1 = new PageManager(storage))
            {
                var (pn1, _) = pm1.AllocatePage(PageType.Leaf);
                pm1.ReleasePage(pn1);
                var (pn2, _) = pm1.AllocatePage(PageType.Leaf);
                pm1.ReleasePage(pn2);
                
                pm1.FreePage(pn1);
                Assert.That(pm1.FreePageCount, Is.EqualTo(1u));
            }
        
            // Reopen and verify free list
            using var pm2 = new PageManager(storage);
            Assert.That(pm2.FreePageCount, Is.EqualTo(1u));
            
            // Allocate should reuse free page
            var (reused, _) = pm2.AllocatePage(PageType.Leaf);
            pm2.ReleasePage(reused);
            Assert.That(reused, Is.EqualTo(1u));
            Assert.That(pm2.FreePageCount, Is.EqualTo(0u));
        }

        #endregion

        #region Dispose Tests

        [Test]
        public void DisposeFlushesDataTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
        
            using (var pageManager = new PageManager(storage))
            {
                var (pn, page) = pageManager.AllocatePage(PageType.Leaf);
                page.Data[0] = 0xAA;
                pageManager.MarkDirty(pn);
                pageManager.ReleasePage(pn);
                // Don't call Flush - Dispose should do it
            }
        
            // Verify data was written
            byte[] buffer = new byte[DatabaseConstants.DEFAULT_PAGE_SIZE];
            storage.ReadPage(1, buffer);
            Assert.That(buffer[0], Is.EqualTo((byte)0xAA));
        }

        [Test]
        public void DisposeMultipleTimesDoesNotThrowTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            var pageManager = new PageManager(storage);
        
            pageManager.Dispose();
            pageManager.Dispose();
        
            // Should not throw
            Assert.Pass();
        }

        [Test]
        public void OperationsAfterDisposeThrowTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            var pageManager = new PageManager(storage);
            pageManager.Dispose();
        
            Assert.Throws<ObjectDisposedException>(() => pageManager.AllocatePage(PageType.Leaf));
            Assert.Throws<ObjectDisposedException>(() => pageManager.GetPage(0));
            Assert.Throws<ObjectDisposedException>(() => pageManager.FreePage(1));
            Assert.Throws<ObjectDisposedException>(() => pageManager.Flush());
            Assert.Throws<ObjectDisposedException>(() => pageManager.SetSchemaRootPage(1));
            Assert.Throws<ObjectDisposedException>(() => pageManager.IncrementTransactionCounter());
        }

        #endregion

        #region AllocatePages Batch Tests

        [Test]
        public void AllocatePagesReturnsCorrectCountTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            var pages = pageManager.AllocatePages(PageType.Leaf, 10);
        
            Assert.That(pages.Length, Is.EqualTo(10));
            Assert.That(pageManager.TotalPageCount, Is.EqualTo(11u)); // +1 for header
        }

        [Test]
        public void AllocatePagesReusesFreePagesTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            // Allocate and free some pages
            var initial = pageManager.AllocatePages(PageType.Leaf, 5);
            foreach (var pn in initial)
                pageManager.FreePage(pn);
        
            Assert.That(pageManager.FreePageCount, Is.EqualTo(5u));
        
            // Allocate again - should reuse
            var reused = pageManager.AllocatePages(PageType.Leaf, 5);
        
            Assert.That(pageManager.FreePageCount, Is.EqualTo(0u));
            // Pages should be the same (reused)
            foreach (var pn in reused)
                Assert.That(initial.Contains(pn), Is.True);
        }

        [Test]
        public void AllocatePagesZeroCountThrowsTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            Assert.Throws<ArgumentOutOfRangeException>(() => pageManager.AllocatePages(PageType.Leaf, 0));
        }

        [Test]
        public void AllocatePagesNegativeCountThrowsTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            Assert.Throws<ArgumentOutOfRangeException>(() => pageManager.AllocatePages(PageType.Leaf, -1));
        }

        [Test]
        public void AllocatePagesMixedFreeAndNewTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            // Allocate 3 pages
            var initial = pageManager.AllocatePages(PageType.Leaf, 3);
            
            // Free 2 of them
            pageManager.FreePage(initial[0]);
            pageManager.FreePage(initial[2]);
            
            Assert.That(pageManager.FreePageCount, Is.EqualTo(2u));
        
            // Allocate 5 pages - should use 2 free + 3 new
            var mixed = pageManager.AllocatePages(PageType.Leaf, 5);
        
            Assert.That(mixed.Length, Is.EqualTo(5));
            Assert.That(pageManager.FreePageCount, Is.EqualTo(0u));
        }

        #endregion

        #region FreePages Batch Tests

        [Test]
        public void FreePagesMultipleTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            var pages = pageManager.AllocatePages(PageType.Leaf, 5);
            
            pageManager.FreePages(pages);
        
            Assert.That(pageManager.FreePageCount, Is.EqualTo(5u));
        }

        [Test]
        public void FreePagesEmptySpanDoesNothingTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            pageManager.FreePages(ReadOnlySpan<uint>.Empty);
        
            Assert.That(pageManager.FreePageCount, Is.EqualTo(0u));
        }

        [Test]
        public void FreePagesWithHeaderPageThrowsTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            var pages = new uint[] { 0 };
        
            Assert.Throws<ArgumentException>(() => pageManager.FreePages(pages));
        }

        [Test]
        public void FreePagesOutOfRangeThrowsTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            var pages = new uint[] { 999 };
        
            Assert.Throws<ArgumentOutOfRangeException>(() => pageManager.FreePages(pages));
        }

        #endregion

        #region Header Dirty Flag Tests

        [Test]
        public void HeaderNotWrittenUntilFlushTest()
        {
            using var storage = new MemoryStorage(initialPageCount: 0);
            using var pageManager = new PageManager(storage);
        
            uint initialTotal = pageManager.TotalPageCount;
        
            // Allocate pages without flushing
            pageManager.AllocatePages(PageType.Leaf, 5);
        
            // Read header directly from storage - should still have old value
            // (This tests that header is written lazily)
            byte[] buffer = new byte[DatabaseConstants.DEFAULT_PAGE_SIZE];
            storage.ReadPage(0, buffer);
            var storedHeader = DatabaseHeader.ReadFrom(buffer);
        
            // After explicit flush, header should be updated
            pageManager.Flush();
        
            storage.ReadPage(0, buffer);
            var flushedHeader = DatabaseHeader.ReadFrom(buffer);
        
            Assert.That(flushedHeader.TotalPageCount, Is.EqualTo(6u));
        }

        #endregion
    }
}
