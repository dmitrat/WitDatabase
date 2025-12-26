using OutWit.Database.Types;
using System.Runtime.InteropServices;

namespace OutWit.Database.Values
{
    /// <summary>
    /// A variant type for SQL values. Provides efficient storage and type coercion.
    /// </summary>
    /// <remarks>
    /// Uses a union-like structure for efficient storage:
    /// - Primitive types (int, double, bool, DateTime, etc.) stored inline
    /// - Reference types (string, byte[], decimal, JsonDocument) stored in object field
    /// </remarks>
    [StructLayout(LayoutKind.Auto)]
    public readonly partial struct WitSqlValue : IEquatable<WitSqlValue>, IComparable<WitSqlValue>
    {
        #region Constants

        private const string NULL_STRING = "NULL";
        private const string TRUE_STRING = "true";
        private const string FALSE_STRING = "false";

        #endregion

        #region Fields

        private readonly WitSqlType m_type;
        private readonly long m_intValue;      // For Integer, Boolean, DateTime, DateOnly, TimeOnly, TimeSpan
        private readonly double m_realValue;   // For Real
        private readonly object? m_objectValue; // For Text, Blob, Decimal, Guid, DateTimeOffset, Json

        #endregion

        #region Constructors

        private WitSqlValue(WitSqlType type, long intValue = 0, double realValue = 0, object? objectValue = null)
        {
            m_type = type;
            m_intValue = intValue;
            m_realValue = realValue;
            m_objectValue = objectValue;
        }

        #endregion

        #region Static Instances

        /// <summary>
        /// Gets a NULL SqlValue.
        /// </summary>
        public static WitSqlValue Null => new(WitSqlType.Null);

        /// <summary>
        /// Gets a TRUE boolean SqlValue.
        /// </summary>
        public static WitSqlValue True => new(WitSqlType.Boolean, intValue: 1);

        /// <summary>
        /// Gets a FALSE boolean SqlValue.
        /// </summary>
        public static WitSqlValue False => new(WitSqlType.Boolean, intValue: 0);

        #endregion

        #region Tools

        private bool IsNumeric => m_type is WitSqlType.Integer or WitSqlType.Real or WitSqlType.Decimal or WitSqlType.Boolean;

        private bool IsDecimalOperation(WitSqlValue other) =>
            m_type == WitSqlType.Decimal || other.m_type == WitSqlType.Decimal;

        private bool IsRealOperation(WitSqlValue other) =>
            m_type == WitSqlType.Real || other.m_type == WitSqlType.Real;

        private static bool IsTruthyString(string s) =>
            !string.IsNullOrEmpty(s) && s != "0" && !s.Equals("false", StringComparison.OrdinalIgnoreCase);

        private static int CompareBlobs(byte[] a, byte[] b) =>
            a.AsSpan().SequenceCompareTo(b.AsSpan());

        private T ThrowInvalidCast<T>() =>
            throw new InvalidCastException($"Cannot convert {m_type} to {typeof(T).Name}");

        /// <inheritdoc/>
        public override string ToString() =>
            IsNull ? NULL_STRING : $"{m_type}:{AsString()}";

        #endregion

        #region Properties

        /// <summary>
        /// Gets the SQL type of this value.
        /// </summary>
        public WitSqlType Type => m_type;

        /// <summary>
        /// Gets whether this value is NULL.
        /// </summary>
        public bool IsNull => m_type == WitSqlType.Null;

        #endregion
    }
}
