using OutWit.Database.Values;

namespace OutWit.Database.Model
{
    /// <summary>
    /// Key for row comparison that handles both hash code and equality.
    /// Stores full row values to avoid hash collisions giving false positives.
    /// </summary>
    public readonly struct RowKey : IEquatable<RowKey>
    {
        private readonly WitSqlValue[] m_values;
        private readonly int m_hashCode;

        public RowKey(WitSqlRow row)
        {
            m_values = new WitSqlValue[row.ColumnCount];
            var hash = new HashCode();
            for (int i = 0; i < row.ColumnCount; i++)
            {
                m_values[i] = row[i];
                hash.Add(row[i]);
            }
            m_hashCode = hash.ToHashCode();
        }

        public bool Equals(RowKey other)
        {
            if (m_values.Length != other.m_values.Length)
                return false;

            for (int i = 0; i < m_values.Length; i++)
            {
                if (!m_values[i].Equals(other.m_values[i]))
                    return false;
            }
            return true;
        }

        public override bool Equals(object? obj) => obj is RowKey other && Equals(other);

        public override int GetHashCode() => m_hashCode;
    }
}