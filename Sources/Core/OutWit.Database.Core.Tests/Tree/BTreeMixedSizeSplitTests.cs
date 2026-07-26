using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tests.Tree
{
    /// <summary>
    /// Regression tests for the B+Tree leaf split with entries of very different sizes.
    /// </summary>
    /// <remarks>
    /// The split point was <c>totalCount / 2</c>, which assumes every entry is the same size, and
    /// both replay loops discarded what <c>InsertLeaf</c> returned. With mixed sizes the heavier half
    /// could exceed the page: the overflowing entries were dropped and <c>Insert</c> still reported
    /// success. Nothing surfaced until the value was read back and was not there.
    ///
    /// These tests write and read back, because a split that loses entries is invisible to anything
    /// that only checks the return value.
    /// </remarks>
    [TestFixture]
    public class BTreeMixedSizeSplitTests
    {
        #region Mixed Size Splits

        [Test]
        public void EveryEntrySurvivesASplitOfMixedSizesTest()
        {
            using var store = new StoreInMemory();

            var expected = new Dictionary<string, byte[]>();

            // Alternate tiny and large values so a count-based midpoint puts far more bytes on one
            // side than the other.
            for (var i = 0; i < 200; i++)
            {
                var key = $"key-{i:D4}";
                var value = i % 3 == 0
                    ? System.Text.Encoding.UTF8.GetBytes(new string('x', 900))
                    : System.Text.Encoding.UTF8.GetBytes("s");

                expected[key] = value;
                store.Put(System.Text.Encoding.UTF8.GetBytes(key), value);
            }

            AssertAllPresent(store, expected);
        }

        [Test]
        public void EveryEntrySurvivesWhenLargeValuesArriveTogetherTest()
        {
            using var store = new StoreInMemory();

            var expected = new Dictionary<string, byte[]>();

            // A run of large values in the middle: the count midpoint lands inside the run.
            for (var i = 0; i < 150; i++)
            {
                var key = $"key-{i:D4}";
                var value = i is >= 60 and < 90
                    ? System.Text.Encoding.UTF8.GetBytes(new string('y', 1200))
                    : System.Text.Encoding.UTF8.GetBytes("t");

                expected[key] = value;
                store.Put(System.Text.Encoding.UTF8.GetBytes(key), value);
            }

            AssertAllPresent(store, expected);
        }

        [Test]
        public void EveryEntrySurvivesWithGrowingValueSizesTest()
        {
            using var store = new StoreInMemory();

            var expected = new Dictionary<string, byte[]>();

            for (var i = 0; i < 120; i++)
            {
                var key = $"key-{i:D4}";
                var value = System.Text.Encoding.UTF8.GetBytes(new string('z', 1 + i * 8));

                expected[key] = value;
                store.Put(System.Text.Encoding.UTF8.GetBytes(key), value);
            }

            AssertAllPresent(store, expected);
        }

        [Test]
        public void KeysRemainInOrderAfterMixedSizeSplitsTest()
        {
            using var store = new StoreInMemory();

            for (var i = 0; i < 200; i++)
            {
                var value = i % 4 == 0
                    ? System.Text.Encoding.UTF8.GetBytes(new string('x', 800))
                    : System.Text.Encoding.UTF8.GetBytes("s");

                store.Put(System.Text.Encoding.UTF8.GetBytes($"key-{i:D4}"), value);
            }

            var keys = store.Scan(null, null)
                .Select(entry => System.Text.Encoding.UTF8.GetString(entry.Key))
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.That(keys, Has.Count.EqualTo(200), "A scan must see every entry");
                Assert.That(keys, Is.Ordered, "Split must preserve key order");
            });
        }

        #endregion

        #region Helper Methods

        private static void AssertAllPresent(StoreInMemory store, Dictionary<string, byte[]> expected)
        {
            var missing = new List<string>();
            var wrong = new List<string>();

            foreach (var (key, value) in expected)
            {
                var actual = store.Get(System.Text.Encoding.UTF8.GetBytes(key));

                if (actual == null)
                    missing.Add(key);
                else if (!actual.AsSpan().SequenceEqual(value))
                    wrong.Add(key);
            }

            Assert.Multiple(() =>
            {
                Assert.That(missing, Is.Empty, "Entries lost by a leaf split");
                Assert.That(wrong, Is.Empty, "Entries corrupted by a leaf split");
            });
        }

        #endregion
    }
}
