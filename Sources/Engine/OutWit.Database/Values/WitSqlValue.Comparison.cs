using System.Globalization;
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

            // TEXT AGAINST A TYPED VALUE IS READ AS THAT TYPE, which is what PostgreSQL and SQL
            // Server do with `WHERE stamp = '2026-07-01 13:45:30'`.
            //
            // Everything below used to fall through to the ordinal string comparison, and that gave
            // WRONG ANSWERS rather than merely missing ones - it compared two RENDERINGS:
            //
            //   N > '9'    answered NO for N = 42, because "42" sorts before "9";
            //   N < '9'    answered YES for the same row - the two are wrong in opposite directions;
            //   S = '2026-07-01 13:45:30'  found nothing, because a DateTime renders as
            //                              2026-07-01T13:45:30.0000000 and nobody writes that;
            //   S > '2026-07-01 13:45:30'  answered YES for that very instant, because 'T' sorts
            //                              after the space.
            //
            // DATE, TIME, GUID and BOOLEAN happened to work, because their rendering is the way a
            // person writes them - which is why this looked like a temporal-literal problem in
            // `Docs/KnownIssues.md` 2 rather than the general one it is.
            if (m_type == WitSqlType.Text && TryReadAs((string)m_objectValue!, other.m_type, out var asOther))
                return asOther.CompareTo(other);

            if (other.m_type == WitSqlType.Text && TryReadAs((string)other.m_objectValue!, m_type, out var otherAsThis))
                return CompareTo(otherAsThis);

            // Otherwise compare as strings. Text that is not a value of the other type at all -
            // `D = 'not a date'` - lands here, as it always did: a comparison is not the place to
            // refuse, and answering "not equal" is what both reference databases' users see when the
            // engine cannot convert.
            return string.Compare(AsString(), other.AsString(), StringComparison.Ordinal);
        }

        /// <summary>
        /// Reads <paramref name="text"/> as a value of <paramref name="type"/>, or answers false when
        /// it is not one.
        /// </summary>
        /// <remarks>
        /// Invariant culture throughout: a stored value is not read differently on a machine whose
        /// locale writes dates the other way round. <see cref="WitSqlType.Text"/>,
        /// <see cref="WitSqlType.Json"/> and <see cref="WitSqlType.Blob"/> are absent on purpose -
        /// text against those is already a comparison of like with like, or of bytes with a name for
        /// them, and neither is this method's business.
        /// </remarks>
        private static bool TryReadAs(string text, WitSqlType type, out WitSqlValue value)
        {
            value = Null;

            switch (type)
            {
                case WitSqlType.Integer:
                    if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                        value = FromInt(integer);
                    break;

                case WitSqlType.Real:
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var real))
                        value = FromReal(real);
                    break;

                case WitSqlType.Decimal:
                    if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                        value = FromDecimal(number);
                    break;

                case WitSqlType.DateOnly:
                    if (DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        value = FromDateOnly(date);
                    break;

                case WitSqlType.TimeOnly:
                    if (TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                        value = FromTimeOnly(time);
                    break;

                case WitSqlType.DateTime:
                    if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamp))
                        value = FromDateTime(stamp);
                    break;

                case WitSqlType.DateTimeOffset:
                    if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment))
                        value = FromDateTimeOffset(moment);
                    break;

                case WitSqlType.TimeSpan:
                    if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var interval))
                        value = FromTimeSpan(interval);
                    break;

                case WitSqlType.Guid:
                    if (Guid.TryParse(text, out var guid))
                        value = FromGuid(guid);
                    break;

                case WitSqlType.Boolean:
                    if (bool.TryParse(text, out var flag))
                        value = FromBool(flag);
                    break;
            }

            return !value.IsNull;
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
