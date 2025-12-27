using OutWit.Database.Types;
using System.Globalization;
using System.Text.Json;

namespace OutWit.Database.Values
{
    public readonly partial struct WitSqlValue
    {
        #region Value Getters

        /// <summary>
        /// Gets the value as Int64.
        /// </summary>
        /// <exception cref="InvalidCastException">If conversion is not possible.</exception>
        public long AsInt64() => m_type switch
        {
            WitSqlType.Integer or WitSqlType.Boolean => m_intValue,
            WitSqlType.Real => (long)m_realValue,
            WitSqlType.Text => long.TryParse((string)m_objectValue!, out var v) ? v : 0,
            WitSqlType.Decimal => (long)(decimal)m_objectValue!,
            WitSqlType.Null => 0,
            _ => ThrowInvalidCast<long>()
        };

        /// <summary>
        /// Gets the value as long (alias for AsInt64).
        /// </summary>
        /// <exception cref="InvalidCastException">If conversion is not possible.</exception>
        public long AsLong() => AsInt64();

        /// <summary>
        /// Gets the value as UInt64.
        /// </summary>
        /// <exception cref="InvalidCastException">If conversion is not possible.</exception>
        public ulong AsUInt64() => m_type switch
        {
            WitSqlType.Integer or WitSqlType.Boolean => (ulong)m_intValue,
            WitSqlType.Real => (ulong)m_realValue,
            WitSqlType.Text => ulong.TryParse((string)m_objectValue!, out var v) ? v : 0,
            WitSqlType.Decimal => (ulong)(decimal)m_objectValue!,
            WitSqlType.Null => 0,
            _ => ThrowInvalidCast<ulong>()
        };

        /// <summary>
        /// Gets the value as ulong (alias for AsUInt64).
        /// </summary>
        /// <exception cref="InvalidCastException">If conversion is not possible.</exception>
        public ulong AsULong() => AsUInt64();

        /// <summary>
        /// Gets the value as Double.
        /// </summary>
        /// <exception cref="InvalidCastException">If conversion is not possible.</exception>
        public double AsDouble() => m_type switch
        {
            WitSqlType.Real => m_realValue,
            WitSqlType.Integer or WitSqlType.Boolean => m_intValue,
            WitSqlType.Text => double.TryParse((string)m_objectValue!, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0,
            WitSqlType.Decimal => (double)(decimal)m_objectValue!,
            WitSqlType.Null => 0,
            _ => ThrowInvalidCast<double>()
        };

        /// <summary>
        /// Gets the value as String.
        /// </summary>
        public string AsString() => m_type switch
        {
            WitSqlType.Text => (string)m_objectValue!,
            WitSqlType.Integer => m_intValue.ToString(CultureInfo.InvariantCulture),
            WitSqlType.Real => m_realValue.ToString(CultureInfo.InvariantCulture),
            WitSqlType.Boolean => m_intValue != 0 ? TRUE_STRING : FALSE_STRING,
            WitSqlType.Decimal => ((decimal)m_objectValue!).ToString(CultureInfo.InvariantCulture),
            WitSqlType.DateTime => new DateTime(m_intValue).ToString("o", CultureInfo.InvariantCulture),
            WitSqlType.DateOnly => DateOnly.FromDayNumber((int)m_intValue).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            WitSqlType.TimeOnly => new TimeOnly(m_intValue).ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
            WitSqlType.TimeSpan => new TimeSpan(m_intValue).ToString("c", CultureInfo.InvariantCulture),
            WitSqlType.Guid => ((Guid)m_objectValue!).ToString(),
            WitSqlType.DateTimeOffset => ((DateTimeOffset)m_objectValue!).ToString("o", CultureInfo.InvariantCulture),
            WitSqlType.Blob => Convert.ToBase64String((byte[])m_objectValue!),
            WitSqlType.Json => JsonToString(),
            WitSqlType.Null => string.Empty,
            _ => ThrowInvalidCast<string>()
        };

        /// <summary>
        /// Gets the value as byte array.
        /// </summary>
        /// <exception cref="InvalidCastException">If conversion is not possible.</exception>
        public byte[] AsBlob() => m_type switch
        {
            WitSqlType.Blob => (byte[])m_objectValue!,
            WitSqlType.Text => Convert.FromBase64String((string)m_objectValue!),
            WitSqlType.Null => [],
            _ => ThrowInvalidCast<byte[]>()
        };

        /// <summary>
        /// Gets the value as Boolean.
        /// </summary>
        public bool AsBool() => m_type switch
        {
            WitSqlType.Boolean or WitSqlType.Integer => m_intValue != 0,
            WitSqlType.Real => m_realValue != 0,
            WitSqlType.Text => IsTruthyString((string)m_objectValue!),
            WitSqlType.Null => false,
            _ => ThrowInvalidCast<bool>()
        };

        /// <summary>
        /// Gets the value as Decimal.
        /// </summary>
        /// <exception cref="InvalidCastException">If conversion is not possible.</exception>
        public decimal AsDecimal() => m_type switch
        {
            WitSqlType.Decimal => (decimal)m_objectValue!,
            WitSqlType.Integer => m_intValue,
            WitSqlType.Real => (decimal)m_realValue,
            WitSqlType.Text => decimal.TryParse((string)m_objectValue!, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0,
            WitSqlType.Null => 0,
            _ => ThrowInvalidCast<decimal>()
        };

        /// <summary>
        /// Gets the value as DateTime.
        /// </summary>
        /// <exception cref="InvalidCastException">If conversion is not possible.</exception>
        public DateTime AsDateTime() => m_type switch
        {
            WitSqlType.DateTime => new DateTime(m_intValue),
            WitSqlType.Text => DateTime.TryParse((string)m_objectValue!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var v) ? v : DateTime.MinValue,
            WitSqlType.Integer => new DateTime(m_intValue),
            WitSqlType.DateTimeOffset => ((DateTimeOffset)m_objectValue!).DateTime,
            WitSqlType.Null => DateTime.MinValue,
            _ => ThrowInvalidCast<DateTime>()
        };

        /// <summary>
        /// Gets the value as DateOnly.
        /// </summary>
        /// <exception cref="InvalidCastException">If conversion is not possible.</exception>
        public DateOnly AsDateOnly() => m_type switch
        {
            WitSqlType.DateOnly => DateOnly.FromDayNumber((int)m_intValue),
            WitSqlType.DateTime => DateOnly.FromDateTime(new DateTime(m_intValue)),
            WitSqlType.Text => DateOnly.TryParse((string)m_objectValue!, CultureInfo.InvariantCulture, DateTimeStyles.None, out var v) ? v : DateOnly.MinValue,
            WitSqlType.Null => DateOnly.MinValue,
            _ => ThrowInvalidCast<DateOnly>()
        };

        /// <summary>
        /// Gets the value as TimeOnly.
        /// </summary>
        /// <exception cref="InvalidCastException">If conversion is not possible.</exception>
        public TimeOnly AsTimeOnly() => m_type switch
        {
            WitSqlType.TimeOnly => new TimeOnly(m_intValue),
            WitSqlType.DateTime => TimeOnly.FromDateTime(new DateTime(m_intValue)),
            WitSqlType.TimeSpan => TimeOnly.FromTimeSpan(new TimeSpan(m_intValue)),
            WitSqlType.Text => TimeOnly.TryParse((string)m_objectValue!, CultureInfo.InvariantCulture, DateTimeStyles.None, out var v) ? v : TimeOnly.MinValue,
            WitSqlType.Null => TimeOnly.MinValue,
            _ => ThrowInvalidCast<TimeOnly>()
        };

        /// <summary>
        /// Gets the value as TimeSpan.
        /// </summary>
        /// <exception cref="InvalidCastException">If conversion is not possible.</exception>
        public TimeSpan AsTimeSpan() => m_type switch
        {
            WitSqlType.TimeSpan or WitSqlType.TimeOnly => new TimeSpan(m_intValue),
            WitSqlType.Integer => new TimeSpan(m_intValue),
            WitSqlType.Text => TimeSpan.TryParse((string)m_objectValue!, CultureInfo.InvariantCulture, out var v) ? v : TimeSpan.Zero,
            WitSqlType.Null => TimeSpan.Zero,
            _ => ThrowInvalidCast<TimeSpan>()
        };

        /// <summary>
        /// Gets the value as Guid.
        /// </summary>
        /// <exception cref="InvalidCastException">If conversion is not possible.</exception>
        public Guid AsGuid() => m_type switch
        {
            WitSqlType.Guid => (Guid)m_objectValue!,
            WitSqlType.Text => Guid.TryParse((string)m_objectValue!, out var v) ? v : Guid.Empty,
            WitSqlType.Blob when ((byte[])m_objectValue!).Length == 16 => new Guid((byte[])m_objectValue!),
            WitSqlType.Null => Guid.Empty,
            _ => ThrowInvalidCast<Guid>()
        };

        /// <summary>
        /// Gets the value as DateTimeOffset.
        /// </summary>
        /// <exception cref="InvalidCastException">If conversion is not possible.</exception>
        public DateTimeOffset AsDateTimeOffset() => m_type switch
        {
            WitSqlType.DateTimeOffset => (DateTimeOffset)m_objectValue!,
            WitSqlType.DateTime => new DateTimeOffset(new DateTime(m_intValue)),
            WitSqlType.Text => DateTimeOffset.TryParse((string)m_objectValue!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var v) ? v : DateTimeOffset.MinValue,
            WitSqlType.Null => DateTimeOffset.MinValue,
            _ => ThrowInvalidCast<DateTimeOffset>()
        };

        /// <summary>
        /// Gets the value as JsonElement.
        /// </summary>
        /// <exception cref="InvalidCastException">If conversion is not possible.</exception>
        public JsonElement AsJsonElement() => m_type switch
        {
            WitSqlType.Json when m_objectValue is JsonDocument doc => doc.RootElement.Clone(),
            WitSqlType.Json when m_objectValue is JsonElement elem => elem,
            WitSqlType.Text => JsonDocument.Parse((string)m_objectValue!).RootElement,
            WitSqlType.Null => default,
            _ => ThrowInvalidCast<JsonElement>()
        };

        /// <summary>
        /// Gets the value as JsonDocument.
        /// </summary>
        /// <exception cref="InvalidCastException">If conversion is not possible.</exception>
        public JsonDocument? AsJsonDocument() => m_type switch
        {
            WitSqlType.Json when m_objectValue is JsonDocument doc => doc,
            WitSqlType.Json when m_objectValue is JsonElement elem => JsonDocument.Parse(elem.GetRawText()),
            WitSqlType.Text => JsonDocument.Parse((string)m_objectValue!),
            WitSqlType.Null => null,
            _ => ThrowInvalidCast<JsonDocument>()
        };

        /// <summary>
        /// Converts to .NET object.
        /// </summary>
        public object? ToObject() => m_type switch
        {
            WitSqlType.Null => null,
            WitSqlType.Integer => m_intValue,
            WitSqlType.Real => m_realValue,
            WitSqlType.Text or WitSqlType.Blob or WitSqlType.Decimal or WitSqlType.Guid or WitSqlType.DateTimeOffset or WitSqlType.Json => m_objectValue,
            WitSqlType.Boolean => m_intValue != 0,
            WitSqlType.DateTime => new DateTime(m_intValue),
            WitSqlType.DateOnly => DateOnly.FromDayNumber((int)m_intValue),
            WitSqlType.TimeOnly => new TimeOnly(m_intValue),
            WitSqlType.TimeSpan => new TimeSpan(m_intValue),
            _ => throw new InvalidOperationException($"Unknown type: {m_type}")
        };

        #endregion
    }
}
