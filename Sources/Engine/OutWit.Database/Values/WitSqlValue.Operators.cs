namespace OutWit.Database.Values
{
    public readonly partial struct WitSqlValue
    {
        #region Arithmetic Operators

        public static WitSqlValue operator +(WitSqlValue left, WitSqlValue right) => left.Add(right);
        public static WitSqlValue operator -(WitSqlValue left, WitSqlValue right) => left.Subtract(right);
        public static WitSqlValue operator *(WitSqlValue left, WitSqlValue right) => left.Multiply(right);
        public static WitSqlValue operator /(WitSqlValue left, WitSqlValue right) => left.Divide(right);
        public static WitSqlValue operator %(WitSqlValue left, WitSqlValue right) => left.Modulo(right);
        public static WitSqlValue operator -(WitSqlValue value) => value.Negate();

        #endregion

        #region Implicit Conversions

        public static implicit operator WitSqlValue(long value) => FromInt(value);
        public static implicit operator WitSqlValue(int value) => FromInt(value);
        public static implicit operator WitSqlValue(double value) => FromReal(value);
        public static implicit operator WitSqlValue(string value) => FromText(value);
        public static implicit operator WitSqlValue(bool value) => FromBool(value);
        public static implicit operator WitSqlValue(decimal value) => FromDecimal(value);
        public static implicit operator WitSqlValue(DateTime value) => FromDateTime(value);
        public static implicit operator WitSqlValue(DateOnly value) => FromDateOnly(value);
        public static implicit operator WitSqlValue(TimeOnly value) => FromTimeOnly(value);
        public static implicit operator WitSqlValue(TimeSpan value) => FromTimeSpan(value);
        public static implicit operator WitSqlValue(Guid value) => FromGuid(value);

        #endregion
    }
}
