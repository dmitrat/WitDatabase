using OutWit.Database.Types;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace OutWit.Database.Values
{
    public readonly partial struct WitSqlValue
    {
        #region Factory Methods

        /// <summary>
        /// Creates an integer SqlValue.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WitSqlValue FromInt(long value) => new(WitSqlType.Integer, intValue: value);

        /// <summary>
        /// Creates a real (double) SqlValue.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WitSqlValue FromReal(double value) => new(WitSqlType.Real, realValue: value);

        /// <summary>
        /// Creates a text SqlValue.
        /// </summary>
        public static WitSqlValue FromText(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return new(WitSqlType.Text, objectValue: value);
        }

        /// <summary>
        /// Creates a blob SqlValue.
        /// </summary>
        public static WitSqlValue FromBlob(byte[] value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return new(WitSqlType.Blob, objectValue: value);
        }

        /// <summary>
        /// Creates a boolean SqlValue.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WitSqlValue FromBool(bool value) => value ? True : False;

        /// <summary>
        /// Creates a decimal SqlValue.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WitSqlValue FromDecimal(decimal value) => new(WitSqlType.Decimal, objectValue: value);

        /// <summary>
        /// Creates a DateTime SqlValue.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WitSqlValue FromDateTime(DateTime value) => new(WitSqlType.DateTime, intValue: value.Ticks);

        /// <summary>
        /// Creates a DateOnly SqlValue.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WitSqlValue FromDateOnly(DateOnly value) => new(WitSqlType.DateOnly, intValue: value.DayNumber);

        /// <summary>
        /// Creates a TimeOnly SqlValue.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WitSqlValue FromTimeOnly(TimeOnly value) => new(WitSqlType.TimeOnly, intValue: value.Ticks);

        /// <summary>
        /// Creates a TimeSpan SqlValue.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WitSqlValue FromTimeSpan(TimeSpan value) => new(WitSqlType.TimeSpan, intValue: value.Ticks);

        /// <summary>
        /// Creates a Guid SqlValue.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WitSqlValue FromGuid(Guid value) => new(WitSqlType.Guid, objectValue: value);

        /// <summary>
        /// Creates a DateTimeOffset SqlValue.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WitSqlValue FromDateTimeOffset(DateTimeOffset value) => new(WitSqlType.DateTimeOffset, objectValue: value);

        /// <summary>
        /// Creates a Json SqlValue from a JsonDocument.
        /// </summary>
        public static WitSqlValue FromJson(JsonDocument value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return new(WitSqlType.Json, objectValue: value);
        }

        /// <summary>
        /// Creates a Json SqlValue from a JsonElement.
        /// </summary>
        public static WitSqlValue FromJson(JsonElement value)
        {
            // Clone to ensure the element is not tied to a disposed document
            return new(WitSqlType.Json, objectValue: value.Clone());
        }

        /// <summary>
        /// Creates a Json SqlValue from a JSON string.
        /// </summary>
        /// <exception cref="JsonException">If the string is not valid JSON.</exception>
        public static WitSqlValue FromJsonString(string jsonString)
        {
            ArgumentNullException.ThrowIfNull(jsonString);
            var document = JsonDocument.Parse(jsonString);
            return new(WitSqlType.Json, objectValue: document);
        }

        /// <summary>
        /// Tries to create a Json SqlValue from a JSON string.
        /// </summary>
        /// <returns>True if parsing succeeded, false otherwise.</returns>
        public static bool TryFromJsonString(string? jsonString, out WitSqlValue result)
        {
            if (string.IsNullOrEmpty(jsonString))
            {
                result = Null;
                return false;
            }

            try
            {
                var document = JsonDocument.Parse(jsonString);
                result = new(WitSqlType.Json, objectValue: document);
                return true;
            }
            catch (JsonException)
            {
                result = Null;
                return false;
            }
        }

        /// <summary>
        /// Creates SqlValue from a .NET object.
        /// </summary>
        /// <exception cref="ArgumentException">If the type is not supported.</exception>
        public static WitSqlValue FromObject(object? value)
        {
            return value switch
            {
                null or DBNull => Null,
                bool b => FromBool(b),
                sbyte i => FromInt(i),
                byte i => FromInt(i),
                short i => FromInt(i),
                ushort i => FromInt(i),
                int i => FromInt(i),
                uint i => FromInt(i),
                long i => FromInt(i),
                ulong i => FromInt((long)i),
                float f => FromReal(f),
                double d => FromReal(d),
                decimal d => FromDecimal(d),
                string s => FromText(s),
                byte[] b => FromBlob(b),
                DateTime dt => FromDateTime(dt),
                DateOnly d => FromDateOnly(d),
                TimeOnly t => FromTimeOnly(t),
                TimeSpan ts => FromTimeSpan(ts),
                Guid g => FromGuid(g),
                DateTimeOffset dto => FromDateTimeOffset(dto),
                JsonDocument jd => FromJson(jd),
                JsonElement je => FromJson(je),
                WitSqlValue sv => sv,
                _ => throw new ArgumentException($"Cannot convert {value.GetType().Name} to WitSqlValue", nameof(value))
            };
        }

        #endregion
    }
}
