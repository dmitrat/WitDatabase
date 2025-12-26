using OutWit.Database.Types;

namespace OutWit.Database.Values
{
    public readonly partial struct WitSqlValue
    {
        #region Arithmetic Operations

        /// <summary>
        /// Adds two SqlValues.
        /// </summary>
        public WitSqlValue Add(WitSqlValue other)
        {
            if (IsNull || other.IsNull)
                return Null;

            return (m_type, other.m_type) switch
            {
                (WitSqlType.Integer, WitSqlType.Integer) => FromInt(m_intValue + other.m_intValue),
                (WitSqlType.Text, WitSqlType.Text) => FromText((string)m_objectValue! + (string)other.m_objectValue!),
                _ when IsDecimalOperation(other) => FromDecimal(AsDecimal() + other.AsDecimal()),
                _ when IsRealOperation(other) => FromReal(AsDouble() + other.AsDouble()),
                _ => FromReal(AsDouble() + other.AsDouble())
            };
        }

        /// <summary>
        /// Subtracts another SqlValue from this one.
        /// </summary>
        public WitSqlValue Subtract(WitSqlValue other)
        {
            if (IsNull || other.IsNull)
                return Null;

            return (m_type, other.m_type) switch
            {
                (WitSqlType.Integer, WitSqlType.Integer) => FromInt(m_intValue - other.m_intValue),
                _ when IsDecimalOperation(other) => FromDecimal(AsDecimal() - other.AsDecimal()),
                _ when IsRealOperation(other) => FromReal(AsDouble() - other.AsDouble()),
                _ => FromReal(AsDouble() - other.AsDouble())
            };
        }

        /// <summary>
        /// Multiplies two SqlValues.
        /// </summary>
        public WitSqlValue Multiply(WitSqlValue other)
        {
            if (IsNull || other.IsNull)
                return Null;

            return (m_type, other.m_type) switch
            {
                (WitSqlType.Integer, WitSqlType.Integer) => FromInt(m_intValue * other.m_intValue),
                _ when IsDecimalOperation(other) => FromDecimal(AsDecimal() * other.AsDecimal()),
                _ when IsRealOperation(other) => FromReal(AsDouble() * other.AsDouble()),
                _ => FromReal(AsDouble() * other.AsDouble())
            };
        }

        /// <summary>
        /// Divides this SqlValue by another.
        /// </summary>
        public WitSqlValue Divide(WitSqlValue other)
        {
            if (IsNull || other.IsNull)
                return Null;

            // Integer division returns integer only if exact
            if (m_type == WitSqlType.Integer && other.m_type == WitSqlType.Integer)
            {
                if (other.m_intValue == 0)
                    return Null;
                if (m_intValue % other.m_intValue == 0)
                    return FromInt(m_intValue / other.m_intValue);
            }

            var divisor = other.AsDouble();
            return divisor == 0 ? Null : FromReal(AsDouble() / divisor);
        }

        /// <summary>
        /// Computes modulo of this SqlValue by another.
        /// </summary>
        public WitSqlValue Modulo(WitSqlValue other)
        {
            if (IsNull || other.IsNull)
                return Null;

            var divisor = other.AsInt64();
            return divisor == 0 ? Null : FromInt(AsInt64() % divisor);
        }

        /// <summary>
        /// Negates this SqlValue.
        /// </summary>
        public WitSqlValue Negate()
        {
            if (IsNull)
                return Null;

            return m_type switch
            {
                WitSqlType.Integer => FromInt(-m_intValue),
                WitSqlType.Real => FromReal(-m_realValue),
                WitSqlType.Decimal => FromDecimal(-(decimal)m_objectValue!),
                _ => FromReal(-AsDouble())
            };
        }

        #endregion

        #region Logical Operations

        /// <summary>
        /// Logical AND of two SqlValues.
        /// </summary>
        public static WitSqlValue And(WitSqlValue left, WitSqlValue right)
        {
            if (left.IsNull || right.IsNull)
                return Null;
            return FromBool(left.AsBool() && right.AsBool());
        }

        /// <summary>
        /// Logical OR of two SqlValues.
        /// </summary>
        public static WitSqlValue Or(WitSqlValue left, WitSqlValue right)
        {
            if (left.IsNull || right.IsNull)
                return Null;
            return FromBool(left.AsBool() || right.AsBool());
        }

        /// <summary>
        /// Logical NOT of a SqlValue.
        /// </summary>
        public static WitSqlValue Not(WitSqlValue value)
        {
            if (value.IsNull)
                return Null;
            return FromBool(!value.AsBool());
        }

        #endregion

        #region String Operations

        /// <summary>
        /// Concatenates two SqlValues as strings.
        /// </summary>
        public WitSqlValue Concat(WitSqlValue other)
        {
            if (IsNull || other.IsNull)
                return Null;
            return FromText(AsString() + other.AsString());
        }

        #endregion
    }
}
