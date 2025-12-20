using OutWit.Database.Core.Comparers;

namespace OutWit.Database.Core.LSM
{
    /// <summary>
    /// Merge iterator that produces entries from multiple SSTables in sorted order.
    /// Uses PriorityQueue for O(log n) min selection instead of O(n) scan.
    /// For duplicate keys, yields newest first (highest priority).
    /// </summary>
    internal sealed class MergeIterator
    {
        #region Nested Types

        private readonly record struct HeapEntry(byte[] Key, byte[]? Value, int SourceIndex) : IComparable<HeapEntry>
        {
            public int CompareTo(HeapEntry other)
            {
                var cmp = Key.AsSpan().SequenceCompareTo(other.Key);
                if (cmp != 0) return cmp;
                // For same key, higher source index (newer) should come first (lower priority value)
                return other.SourceIndex.CompareTo(SourceIndex);
            }
        }

        #endregion

        #region Fields

        private readonly IEnumerator<(byte[] Key, byte[]? Value)>[] m_iterators;
        private readonly PriorityQueue<HeapEntry, HeapEntry> m_heap;
        private readonly bool[] m_exhausted;

        #endregion

        #region Constructors

        public MergeIterator(List<SSTableReader> readers, ByteArrayComparer comparer)
        {
            m_iterators = readers.Select(r => r.Scan().GetEnumerator()).ToArray();
            m_exhausted = new bool[readers.Count];
            m_heap = new PriorityQueue<HeapEntry, HeapEntry>();

            // Initialize heap with first entry from each iterator
            for (int i = 0; i < m_iterators.Length; i++)
            {
                if (m_iterators[i].MoveNext())
                {
                    var entry = m_iterators[i].Current;
                    var heapEntry = new HeapEntry(entry.Key, entry.Value, i);
                    m_heap.Enqueue(heapEntry, heapEntry);
                }
                else
                {
                    m_exhausted[i] = true;
                }
            }
        }

        #endregion

        #region Functions

        public IEnumerable<(byte[] Key, byte[]? Value, int Priority)> Iterate()
        {
            byte[]? lastYieldedKey = null;

            while (m_heap.Count > 0)
            {
                var entry = m_heap.Dequeue();

                // Skip duplicates (we already yielded the newest version)
                if (lastYieldedKey != null && 
                    entry.Key.AsSpan().SequenceCompareTo(lastYieldedKey) == 0)
                {
                    // Advance this source's iterator and re-add to heap
                    AdvanceIterator(entry.SourceIndex);
                    continue;
                }

                lastYieldedKey = entry.Key;

                // Yield this entry
                yield return (entry.Key, entry.Value, entry.SourceIndex);

                // Advance the iterator that produced this entry
                AdvanceIterator(entry.SourceIndex);
            }
        }

        private void AdvanceIterator(int sourceIndex)
        {
            if (m_exhausted[sourceIndex])
                return;

            if (m_iterators[sourceIndex].MoveNext())
            {
                var next = m_iterators[sourceIndex].Current;
                var heapEntry = new HeapEntry(next.Key, next.Value, sourceIndex);
                m_heap.Enqueue(heapEntry, heapEntry);
            }
            else
            {
                m_exhausted[sourceIndex] = true;
            }
        }

        #endregion
    }
}
