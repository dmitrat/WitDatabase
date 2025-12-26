namespace OutWit.Database.Types;

/// <summary>
/// SQL value type enumeration for runtime SQL expression evaluation.
/// </summary>
/// <remarks>
/// This is distinct from <see cref="WitDataType"/> which is used for storage.
/// SqlType represents semantic types for SQL operations, while WitDataType
/// represents physical storage types with specific sizes.
/// </remarks>
public enum WitSqlType : byte
{
    Null = 0,
    Integer = 1,
    Real = 2,
    Text = 3,
    Blob = 4,
    Boolean = 5,
    Decimal = 6,
    DateTime = 7,
    DateOnly = 8,
    TimeOnly = 9,
    TimeSpan = 10,
    Guid = 11,
    DateTimeOffset = 12,
    Json = 13
}