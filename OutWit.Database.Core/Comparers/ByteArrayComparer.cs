namespace OutWit.Database.Core.Comparers;

/// <summary>
/// Byte array comparer using lexicographic ordering.
/// Implements both IComparer and IEqualityComparer for use in sorted and hash-based collections.
/// </summary>
public sealed class ByteArrayComparer : IComparer<byte[]>, IEqualityComparer<byte[]>
{
    /// <summary>
    /// Default singleton instance.
    /// </summary>
    public static readonly ByteArrayComparer Default = new();

    private ByteArrayComparer() { }

    /// <summary>
    /// Compares two byte arrays lexicographically.
    /// </summary>
    public int Compare(byte[]? x, byte[]? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        return x.AsSpan().SequenceCompareTo(y.AsSpan());
    }

    /// <summary>
    /// Compares a byte array with a ReadOnlySpan lexicographically.
    /// </summary>
    public int Compare(byte[]? x, ReadOnlySpan<byte> y)
    {
        if (x is null) return -1;
        return x.AsSpan().SequenceCompareTo(y);
    }

    /// <summary>
    /// Compares a ReadOnlySpan with a byte array lexicographically.
    /// </summary>
    public int Compare(ReadOnlySpan<byte> x, byte[]? y)
    {
        if (y is null) return 1;
        return x.SequenceCompareTo(y.AsSpan());
    }

    /// <summary>
    /// Determines whether two byte arrays are equal.
    /// </summary>
    public bool Equals(byte[]? x, byte[]? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;

        return x.AsSpan().SequenceEqual(y.AsSpan());
    }

    /// <summary>
    /// Returns a hash code for the byte array.
    /// Uses FNV-1a algorithm for good distribution.
    /// </summary>
    public int GetHashCode(byte[] obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return GetHashCode(obj.AsSpan());
    }

    /// <summary>
    /// Returns a hash code for the byte span.
    /// Uses FNV-1a algorithm for good distribution.
    /// </summary>
    public static int GetHashCode(ReadOnlySpan<byte> data)
    {
        // FNV-1a hash algorithm
        unchecked
        {
            const int fnvPrime = 16777619;
            int hash = unchecked((int)2166136261);
            
            foreach (byte b in data)
            {
                hash ^= b;
                hash *= fnvPrime;
            }
            
            return hash;
        }
    }
}