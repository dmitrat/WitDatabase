using System.Buffers.Binary;

namespace OutWit.Database.Core.Tree;

public ref partial struct BTreeNode
{
    #region Merge/Redistribute

    /// <summary>
    /// Moves entries from this node to the target node (for redistribution).
    /// </summary>
    public void MoveEntriesToRight(ref BTreeNode target, int count)
    {
        if (count <= 0 || count > KeyCount)
            return;

        int sourceKeyCount = KeyCount;
        int startIndex = sourceKeyCount - count;
        
        if (IsLeaf)
        {
            // Move from end of this node to beginning of target
            for (int i = 0; i < count; i++)
            {
                var key = GetKey(startIndex + i).ToArray();
                var value = GetValue(startIndex + i).ToArray();
                
                // Shift target entries right
                int targetCount = target.KeyCount;
                for (int j = targetCount; j > 0; j--)
                {
                    target.SetCellOffset(j, target.GetCellOffset(j - 1));
                }
                
                // Insert at beginning of target
                int cellSize = CalculateLeafEntrySize(key.Length, value.Length);
                int newCellStart = target.CellAreaStart - cellSize;
                target.CellAreaStart = (ushort)newCellStart;
                target.SetCellOffset(0, newCellStart);
                
                int offset = newCellStart;
                offset += Encoding.VarInt.EncodeUnsigned(target.m_data[offset..], (ulong)key.Length);
                offset += Encoding.VarInt.EncodeUnsigned(target.m_data[offset..], (ulong)value.Length);
                key.CopyTo(target.m_data[offset..]);
                offset += key.Length;
                value.CopyTo(target.m_data[offset..]);
                
                target.KeyCount++;
            }
        }
        else
        {
            // Internal node - similar but with children
            for (int i = 0; i < count; i++)
            {
                var key = GetKey(startIndex + i).ToArray();
                uint child = GetChild(startIndex + i);
                
                int targetCount = target.KeyCount;
                for (int j = targetCount; j > 0; j--)
                {
                    target.SetCellOffset(j, target.GetCellOffset(j - 1));
                }
                
                int cellSize = CalculateInternalEntrySize(key.Length);
                int newCellStart = target.CellAreaStart - cellSize;
                target.CellAreaStart = (ushort)newCellStart;
                target.SetCellOffset(0, newCellStart);
                
                int offset = newCellStart;
                offset += Encoding.VarInt.EncodeUnsigned(target.m_data[offset..], (ulong)key.Length);
                key.CopyTo(m_data[offset..]);
                offset += key.Length;
                BinaryPrimitives.WriteUInt32LittleEndian(target.m_data[offset..], child);
                
                target.KeyCount++;
            }
        }
        
        // Remove moved entries from source
        KeyCount = (ushort)(sourceKeyCount - count);
    }

    /// <summary>
    /// Merges all entries from the source node into this node.
    /// </summary>
    public bool MergeFrom(ref BTreeNode source)
    {
        int sourceCount = source.KeyCount;
        
        if (IsLeaf)
        {
            for (int i = 0; i < sourceCount; i++)
            {
                var key = source.GetKey(i).ToArray();
                var value = source.GetValue(i).ToArray();
                
                if (!CanInsertLeaf(key.Length, value.Length))
                    return false;
                
                InsertLeaf(KeyCount, key, value);
            }
            
            // Update leaf links
            NextLeaf = source.NextLeaf;
        }
        else
        {
            for (int i = 0; i < sourceCount; i++)
            {
                var key = source.GetKey(i).ToArray();
                uint child = source.GetChild(i);
                
                if (!CanInsertInternal(key.Length))
                    return false;
                
                InsertInternal(KeyCount, key, child);
            }
            
            RightmostChild = source.RightmostChild;
        }
        
        return true;
    }

    #endregion

    #region Split Helpers

    /// <summary>
    /// Collects all entries for splitting.
    /// </summary>
    public readonly void CollectLeafEntries(out byte[][] keys, out byte[][] values)
    {
        int count = KeyCount;
        keys = new byte[count][];
        values = new byte[count][];
        
        for (int i = 0; i < count; i++)
        {
            keys[i] = GetKey(i).ToArray();
            values[i] = GetValue(i).ToArray();
        }
    }

    /// <summary>
    /// Collects all entries for splitting internal nodes.
    /// </summary>
    public readonly void CollectInternalEntries(out byte[][] keys, out uint[] children)
    {
        int count = KeyCount;
        keys = new byte[count][];
        children = new uint[count + 1];
        
        for (int i = 0; i < count; i++)
        {
            keys[i] = GetKey(i).ToArray();
            children[i] = GetChild(i);
        }
        children[count] = RightmostChild;
    }

    /// <summary>
    /// Clears all entries from the node.
    /// </summary>
    public void Clear()
    {
        KeyCount = 0;
        CellAreaStart = (ushort)m_pageSize;
    }

    #endregion
}
