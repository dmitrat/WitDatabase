using System.Runtime.CompilerServices;

namespace OutWit.Database.Core.Tree;

public ref partial struct BTreeNode
{
    #region Search

    /// <summary>
    /// Binary search for a key. Returns index if found, or ~insertPoint if not found.
    /// </summary>
    public readonly int SearchKey(ReadOnlySpan<byte> key)
    {
        int low = 0;
        int high = KeyCount - 1;
        
        while (low <= high)
        {
            int mid = (low + high) >> 1;
            var midKey = GetKey(mid);
            
            int cmp = midKey.SequenceCompareTo(key);
            
            if (cmp == 0)
                return mid;
            else if (cmp < 0)
                low = mid + 1;
            else
                high = mid - 1;
        }
        
        return ~low;
    }

    /// <summary>
    /// Finds the child index for navigation in internal nodes.
    /// Returns the index of the child pointer to follow.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int FindChildIndex(ReadOnlySpan<byte> key)
    {
        // Linear search - simple and correct
        // For small number of keys per node this is often faster than binary search
        // due to better cache locality
        int keyCount = KeyCount;
        for (int i = 0; i < keyCount; i++)
        {
            if (key.SequenceCompareTo(GetKey(i)) < 0)
                return i;
        }
        return keyCount;
    }

    #endregion
}
