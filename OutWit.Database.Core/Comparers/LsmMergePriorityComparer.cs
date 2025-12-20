namespace OutWit.Database.Core.Comparers;

/// <summary>
/// Comparer for priority queue entries in LSM-Tree merge operations.
/// Compares by key first (lexicographically), then by priority (lower priority value = higher priority).
/// </summary>
internal sealed class LsmMergePriorityComparer : IComparer<(byte[] Key, int InversePriority)>
{
    public static readonly LsmMergePriorityComparer Instance = new();

    private LsmMergePriorityComparer() { }

    public int Compare((byte[] Key, int InversePriority) x, (byte[] Key, int InversePriority) y)
    {
        var keyCompare = ByteArrayComparer.Default.Compare(x.Key, y.Key);
        if (keyCompare != 0) return keyCompare;
        return x.InversePriority.CompareTo(y.InversePriority);
    }
}
