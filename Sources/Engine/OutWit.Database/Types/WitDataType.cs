namespace OutWit.Database.Types
{
    /// <summary>
    /// Supported data types for storage in WitDB.
    /// Each type has an efficient binary representation.
    /// </summary>
    public enum WitDataType : byte
    {
        /// <summary>
        /// Null value - 0 bytes
        /// </summary>
        Null = 0,

        // ========== Integers (VarInt encoded for variable, fixed for others) ==========

        /// <summary>
        /// Signed 8-bit integer (-128 to 127) - 1 byte fixed
        /// </summary>
        Int8 = 1,

        /// <summary>
        /// Unsigned 8-bit integer (0 to 255) - 1 byte fixed
        /// </summary>
        UInt8 = 2,

        /// <summary>
        /// Signed 16-bit integer - 2 bytes fixed
        /// </summary>
        Int16 = 3,

        /// <summary>
        /// Unsigned 16-bit integer - 2 bytes fixed
        /// </summary>
        UInt16 = 4,

        /// <summary>
        /// Signed 32-bit integer - VarInt encoded (1-5 bytes)
        /// </summary>
        Int32 = 5,

        /// <summary>
        /// Unsigned 32-bit integer - VarInt encoded (1-5 bytes)
        /// </summary>
        UInt32 = 6,

        /// <summary>
        /// Signed 64-bit integer - VarInt encoded (1-10 bytes)
        /// </summary>
        Int64 = 7,

        /// <summary>
        /// Unsigned 64-bit integer - VarInt encoded (1-10 bytes)
        /// </summary>
        UInt64 = 8,

        // ========== Floating Point ==========

        /// <summary>
        /// IEEE 754 half-precision (16-bit) - 2 bytes
        /// </summary>
        Float16 = 10,

        /// <summary>
        /// IEEE 754 single-precision (32-bit) - 4 bytes
        /// </summary>
        Float32 = 11,

        /// <summary>
        /// IEEE 754 double-precision (64-bit) - 8 bytes
        /// </summary>
        Float64 = 12,

        /// <summary>
        /// .NET decimal (128-bit) - 16 bytes
        /// </summary>
        Decimal = 13,

        // ========== Boolean ==========

        /// <summary>
        /// Boolean value - 1 byte (0 = false, 1 = true)
        /// </summary>
        Boolean = 20,

        // ========== Date and Time ==========

        /// <summary>
        /// Date only (no time) - 4 bytes as days since Unix epoch
        /// </summary>
        DateOnly = 30,

        /// <summary>
        /// Time only (no date) - 8 bytes as ticks since midnight
        /// </summary>
        TimeOnly = 31,

        /// <summary>
        /// Date and time (UTC) - 8 bytes as ticks
        /// </summary>
        DateTime = 32,

        /// <summary>
        /// Date, time, and offset - 10 bytes (8 ticks + 2 offset minutes)
        /// </summary>
        DateTimeOffset = 33,

        /// <summary>
        /// Time interval - 8 bytes as ticks
        /// </summary>
        TimeSpan = 34,

        // ========== Unique Identifier ==========

        /// <summary>
        /// GUID/UUID - 16 bytes
        /// </summary>
        Guid = 40,

        // ========== Strings ==========

        /// <summary>
        /// Fixed-length UTF-8 string - length defined in schema
        /// </summary>
        StringFixed = 50,

        /// <summary>
        /// Variable-length UTF-8 string - VarInt length prefix + bytes
        /// </summary>
        StringVariable = 51,

        // ========== Binary ==========

        /// <summary>
        /// Fixed-length binary data - length defined in schema
        /// </summary>
        BinaryFixed = 60,

        /// <summary>
        /// Variable-length binary data - VarInt length prefix + bytes
        /// </summary>
        BinaryVariable = 61,

        // ========== Special Types ==========

        /// <summary>
        /// Row version for optimistic concurrency control - 8 bytes (auto-incrementing)
        /// Used with EF Core concurrency tokens.
        /// </summary>
        RowVersion = 70,

        /// <summary>
        /// JSON document - VarInt length prefix + UTF-8 bytes
        /// Stored as validated JSON text, can be queried with JSON functions.
        /// </summary>
        Json = 80,
    }
}
