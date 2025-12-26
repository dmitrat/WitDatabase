using OutWit.Database.Types;

namespace OutWit.Database.Values
{
    public readonly partial struct WitSqlValue
    {
        #region IComparable<WitSqlValue>

        /// <summary>
        /// Compares this SqlValue to another.
        /// </summary>
        /// <remarks>
        /// NULL is considered less than any non-NULL value.
        /// Numeric types are compared numerically.
        /// Other types are compared as strings.
        /// </remarks>
        public int CompareTo(WitSqlValue other)
        {
            // NULL handling
            if (IsNull && other.IsNull) return 0;
            if (IsNull) return -1;
            if (other.IsNull) return 1;

            // Same type - direct comparison
            if (m_type == other.m_type)
            {
                return m_type switch
                {
                    WitSqlType.Integer or WitSqlType.Boolean or WitSqlType.DateTime or WitSqlType.DateOnly or WitSqlType.TimeOnly or WitSqlType.TimeSpan
                        => m_intValue.CompareTo(other.m_intValue),
                    WitSqlType.Real => m_realValue.CompareTo(other.m_realValue),
                    WitSqlType.Text => string.Compare((string)m_objectValue!, (string)other.m_objectValue!, StringComparison.Ordinal),
                    WitSqlType.Decimal => ((decimal)m_objectValue!).CompareTo((decimal)other.m_objectValue!),
                    WitSqlType.Guid => ((Guid)m_objectValue!).CompareTo((Guid)other.m_objectValue!),
                    WitSqlType.DateTimeOffset => ((DateTimeOffset)m_objectValue!).CompareTo((DateTimeOffset)other.m_objectValue!),
                    WitSqlType.Blob => CompareBlobs((byte[])m_objectValue!, (byte[])other.m_objectValue!),
                    WitSqlType.Json => string.Compare(JsonToString(), other.JsonToString(), StringComparison.Ordinal),
                    _ => 0
                };
            }

            // Cross-type: numeric types compare numerically
            if (IsNumeric && other.IsNumeric)
                return AsDouble().CompareTo(other.AsDouble());

            // Otherwise compare as strings
            return string.Compare(AsString(), other.AsString(), StringComparison.Ordinal);
        }

        #endregion

        #region IEquatable<WitSqlValue>

        /// <summary>
        /// Determines whether this SqlValue equals another.
        /// </summary>
        public bool Equals(WitSqlValue other)
        {
            if (IsNull && other.IsNull) return true;
            if (IsNull || other.IsNull) return false;
            return CompareTo(other) == 0;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is WitSqlValue other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            if (IsNull) return 0;

            return m_type switch
            {
                WitSqlType.Integer or WitSqlType.Boolean or WitSqlType.DateTime or WitSqlType.DateOnly or WitSqlType.TimeOnly or WitSqlType.TimeSpan
                    => HashCode.Combine(m_type, m_intValue),
                WitSqlType.Real => HashCode.Combine(m_type, m_realValue),
                WitSqlType.Json => HashCode.Combine(m_type, JsonToString()),
                _ => HashCode.Combine(m_type, m_objectValue)
            };
        }

        #endregion

        #region Comparison Operators

        public static bool operator ==(WitSqlValue left, WitSqlValue right) => left.Equals(right);
        public static bool operator !=(WitSqlValue left, WitSqlValue right) => !left.Equals(right);
        public static bool operator <(WitSqlValue left, WitSqlValue right) => left.CompareTo(right) < 0;
        public static bool operator <=(WitSqlValue left, WitSqlValue right) => left.CompareTo(right) <= 0;
        public static bool operator >(WitSqlValue left, WitSqlValue right) => left.CompareTo(right) > 0;
        public static bool operator >=(WitSqlValue left, WitSqlValue right) => left.CompareTo(right) >= 0;

        #endregion
    }
}
