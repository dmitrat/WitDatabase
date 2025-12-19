namespace OutWit.Database.Core.Tree;

public ref partial struct BTreeNode
{
    #region Update

    /// <summary>
    /// Updates the value at the specified index.
    /// Returns true if update was successful, false if not enough space (caller should remove and reinsert).
    /// </summary>
    public bool UpdateValue(int index, ReadOnlySpan<byte> newValue)
    {
        if (!IsLeaf)
            ThrowNotLeaf();
        if ((uint)index >= (uint)KeyCount)
            ThrowIndexOutOfRange(index);

        var oldValue = GetValue(index);
        
        // If same size, update in place
        if (newValue.Length == oldValue.Length)
        {
            int offset = GetCellOffset(index);
            var (keyLength, keyLenBytes) = Encoding.VarInt.DecodeUnsigned(m_data[offset..]);
            offset += keyLenBytes;
            var (_, valueLenBytes) = Encoding.VarInt.DecodeUnsigned(m_data[offset..]);
            offset += valueLenBytes + (int)keyLength;
            
            newValue.CopyTo(m_data[offset..]);
            return true;
        }
        
        // Different size - caller needs to handle remove and reinsert
        return false;
    }

    #endregion
}
