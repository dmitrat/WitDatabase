using OutWit.Database.Core.Managers;
using OutWit.Database.Core.Pages;
using OutWit.Database.Core.Storage;

namespace OutWit.Database.Core.Tests.Managers;

[TestFixture]
public class PageManagerStressTest
{
    [Test]
    public void AllocateManyPagesTest()
    {
        const int pageCount = 500;
        
        using var storage = new MemoryStorage(initialPageCount: 0);
        using var pageManager = new PageManager(storage);

        var allocatedPages = new List<uint>();

        // Allocate many pages
        for (int i = 0; i < pageCount; i++)
        {
            var (pageNumber, page) = pageManager.AllocatePage(PageType.Leaf);
            page.Data[0] = (byte)(i % 256);
            page.Data[1] = (byte)(i / 256);
            pageManager.MarkDirty(pageNumber);
            pageManager.ReleasePage(pageNumber);
            allocatedPages.Add(pageNumber);
        }

        Assert.That(pageManager.TotalPageCount, Is.EqualTo((uint)(pageCount + 1))); // +1 for header

        // Verify all pages
        foreach (var pageNumber in allocatedPages)
        {
            var page = pageManager.GetPage(pageNumber);
            int expectedValue = (int)(pageNumber - 1); // First allocated is page 1
            Assert.That(page.Data[0], Is.EqualTo((byte)(expectedValue % 256)));
            Assert.That(page.Data[1], Is.EqualTo((byte)(expectedValue / 256)));
            pageManager.ReleasePage(pageNumber);
        }
    }

    [Test]
    public void FreeAndReusePatternTest()
    {
        const int cycles = 100;
        const int pagesPerCycle = 10;
        
        using var storage = new MemoryStorage(initialPageCount: 0);
        using var pageManager = new PageManager(storage);

        for (int cycle = 0; cycle < cycles; cycle++)
        {
            var allocated = new List<uint>();

            // Allocate batch
            for (int i = 0; i < pagesPerCycle; i++)
            {
                var (pageNumber, _) = pageManager.AllocatePage(PageType.Leaf);
                pageManager.ReleasePage(pageNumber);
                allocated.Add(pageNumber);
            }

            // Free every other page
            for (int i = 0; i < allocated.Count; i += 2)
            {
                pageManager.FreePage(allocated[i]);
            }
        }

        // Total pages should be less than cycles * pagesPerCycle due to reuse
        Assert.That(pageManager.TotalPageCount, Is.LessThan((uint)(cycles * pagesPerCycle)));
        Assert.That(pageManager.FreePageCount, Is.GreaterThan(0u));
    }

    [Test]
    public void RandomWriteToManyPagesTest()
    {
        const int pageCount = 200;
        const int writeOperations = 1000;
        
        using var storage = new MemoryStorage(initialPageCount: 0);
        using var pageManager = new PageManager(storage, cacheSize: 50);

        // Allocate pages
        var pages = new List<uint>();
        for (int i = 0; i < pageCount; i++)
        {
            var (pageNumber, _) = pageManager.AllocatePage(PageType.Leaf);
            pageManager.ReleasePage(pageNumber);
            pages.Add(pageNumber);
        }

        // Track latest value for each page
        var pageValues = new Dictionary<uint, int>();

        // Random writes
        for (int op = 0; op < writeOperations; op++)
        {
            uint targetPage = pages[Random.Shared.Next(pages.Count)];
            
            var page = pageManager.GetPage(targetPage);
            int value = op;
            page.Data[0] = (byte)(value & 0xFF);
            page.Data[1] = (byte)((value >> 8) & 0xFF);
            page.Data[2] = (byte)((value >> 16) & 0xFF);
            page.Data[3] = (byte)((value >> 24) & 0xFF);
            pageManager.MarkDirty(targetPage);
            pageManager.ReleasePage(targetPage);
            
            pageValues[targetPage] = value;
        }

        // Verify all pages have correct latest value
        foreach (var (pageNumber, expectedValue) in pageValues)
        {
            var page = pageManager.GetPage(pageNumber);
            int actualValue = page.Data[0] | 
                (page.Data[1] << 8) | 
                (page.Data[2] << 16) | 
                (page.Data[3] << 24);
            Assert.That(actualValue, Is.EqualTo(expectedValue), 
                $"Page {pageNumber} value mismatch");
            pageManager.ReleasePage(pageNumber);
        }
    }

    [Test]
    public void CacheEvictionUnderPressureTest()
    {
        const int pageCount = 200;
        const int cacheSize = 20;
        
        using var storage = new MemoryStorage(initialPageCount: 0);
        using var pageManager = new PageManager(storage, cacheSize: cacheSize);

        // Allocate more pages than cache can hold
        var pages = new List<uint>();
        for (int i = 0; i < pageCount; i++)
        {
            var (pageNumber, page) = pageManager.AllocatePage(PageType.Leaf);
            
            // Write unique data
            page.Data[0] = (byte)(i % 256);
            page.Data[1] = (byte)(i / 256);
            pageManager.MarkDirty(pageNumber);
            pageManager.ReleasePage(pageNumber);
            
            pages.Add(pageNumber);
        }

        // Access pages in various patterns - should cause cache evictions
        // Pattern 1: Sequential
        for (int i = 0; i < pageCount; i++)
        {
            var page = pageManager.GetPage(pages[i]);
            pageManager.ReleasePage(pages[i]);
        }

        // Pattern 2: Reverse
        for (int i = pageCount - 1; i >= 0; i--)
        {
            var page = pageManager.GetPage(pages[i]);
            pageManager.ReleasePage(pages[i]);
        }

        // Pattern 3: Random
        for (int i = 0; i < 500; i++)
        {
            int idx = Random.Shared.Next(pageCount);
            var page = pageManager.GetPage(pages[idx]);
            pageManager.ReleasePage(pages[idx]);
        }

        // Verify all data is still correct
        for (int i = 0; i < pageCount; i++)
        {
            var page = pageManager.GetPage(pages[i]);
            Assert.That(page.Data[0], Is.EqualTo((byte)(i % 256)), $"Page {i} byte 0 mismatch");
            Assert.That(page.Data[1], Is.EqualTo((byte)(i / 256)), $"Page {i} byte 1 mismatch");
            pageManager.ReleasePage(pages[i]);
        }
    }

    [Test]
    public void FlushAndReopenTest()
    {
        const int pageCount = 100;
        
        using var storage = new MemoryStorage(initialPageCount: 0);

        // First session - create and write
        using (var pm1 = new PageManager(storage))
        {
            for (int i = 0; i < pageCount; i++)
            {
                var (pageNumber, page) = pm1.AllocatePage(PageType.Leaf);
                page.Data[100] = (byte)i;
                pm1.MarkDirty(pageNumber);
                pm1.ReleasePage(pageNumber);
            }
            pm1.Flush();
        }

        // Second session - verify
        using (var pm2 = new PageManager(storage))
        {
            Assert.That(pm2.TotalPageCount, Is.EqualTo((uint)(pageCount + 1)));

            for (uint i = 1; i <= pageCount; i++)
            {
                var page = pm2.GetPage(i);
                Assert.That(page.Data[100], Is.EqualTo((byte)(i - 1)));
                pm2.ReleasePage(i);
            }
        }
    }

    [Test]
    public void InterleavedAllocFreeTest()
    {
        using var storage = new MemoryStorage(initialPageCount: 0);
        using var pageManager = new PageManager(storage, cacheSize: 30);

        var activePages = new HashSet<uint>();
        const int operations = 500;

        for (int op = 0; op < operations; op++)
        {
            bool shouldAllocate = activePages.Count == 0 || 
                (activePages.Count < 100 && Random.Shared.Next(3) != 0);

            if (shouldAllocate)
            {
                var (pageNumber, page) = pageManager.AllocatePage(PageType.Leaf);
                page.Data[0] = (byte)(pageNumber % 256);
                pageManager.MarkDirty(pageNumber);
                pageManager.ReleasePage(pageNumber);
                activePages.Add(pageNumber);
            }
            else
            {
                // Free a random page
                var pageToFree = activePages.ElementAt(Random.Shared.Next(activePages.Count));
                activePages.Remove(pageToFree);
                pageManager.FreePage(pageToFree);
            }
        }

        // Verify remaining pages
        foreach (uint pageNumber in activePages)
        {
            var page = pageManager.GetPage(pageNumber);
            Assert.That(page.Data[0], Is.EqualTo((byte)(pageNumber % 256)));
            pageManager.ReleasePage(pageNumber);
        }

        Console.WriteLine($"Final: {activePages.Count} active, {pageManager.FreePageCount} free, {pageManager.TotalPageCount} total");
    }
}
