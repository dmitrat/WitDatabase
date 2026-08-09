using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;

namespace OutWit.Database.EntityFramework.Storage;

/// <summary>
/// Temporal literals in the form WitSQL reads, which is the TYPED form.
/// </summary>
/// <remarks>
/// <para>
/// <b>These used to emit a plain quoted string, and that was a silent defect.</b> EF Core's own
/// mappings emit ANSI typed literals and the grammar had no such form, so a query comparing against
/// an inlined constant failed to parse before it reached the engine
/// (<c>no viable alternative at input '&gt;TIMESTAMP'</c>) - <c>Docs/KnownIssues.md</c> 2. Quoting the
/// value instead made it parse, and made it answer wrongly: measured 2026-08-09, <b>a row written
/// with a bare string cannot be found by that very same string</b> - 0 rows for a DATETIME and 0 for
/// a DATETIMEOFFSET, while the typed literal finds the row and the row is demonstrably there. Text
/// compared with a temporal column is not converted, so the loud parse error had been traded for an
/// empty result set.
/// </para>
/// <para>
/// The grammar has typed literals now and the WORD in front decides the type, so these mappings emit
/// the keyword that names the type they carry. <c>DATETIMEOFFSET</c> is its own word rather than an
/// offset smuggled inside a <c>TIMESTAMP</c>: the engine refuses that shape by name, where PostgreSQL
/// would accept it and discard the offset.
/// </para>
/// </remarks>
internal static class WitTemporalLiteral
{
    /// <summary>
    /// Round-trip format, so a literal read back yields the value that was written.
    /// </summary>
    public const string DATETIME_FORMAT = "yyyy-MM-dd HH:mm:ss.fffffff";

    /// <summary>
    /// Writes a formatted temporal value as the typed literal its <paramref name="keyword"/> names.
    /// </summary>
    /// <param name="keyword">The type's name, spelled as it is spelled in DDL.</param>
    /// <param name="text">The formatted value.</param>
    public static string Typed(string keyword, string text) => $"{keyword} '{text}'";
}

/// <inheritdoc />
public sealed class WitDateTimeTypeMapping : DateTimeTypeMapping
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WitDateTimeTypeMapping"/> class.
    /// </summary>
    /// <param name="storeType">The store type name.</param>
    public WitDateTimeTypeMapping(string storeType)
        : base(storeType, System.Data.DbType.DateTime)
    {
    }

    private WitDateTimeTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new WitDateTimeTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
        => WitTemporalLiteral.Typed("TIMESTAMP",
            ((DateTime)value).ToString(WitTemporalLiteral.DATETIME_FORMAT, CultureInfo.InvariantCulture));
}

/// <inheritdoc />
public sealed class WitDateTimeOffsetTypeMapping : DateTimeOffsetTypeMapping
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WitDateTimeOffsetTypeMapping"/> class.
    /// </summary>
    /// <param name="storeType">The store type name.</param>
    public WitDateTimeOffsetTypeMapping(string storeType)
        : base(storeType)
    {
    }

    private WitDateTimeOffsetTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new WitDateTimeOffsetTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
        => WitTemporalLiteral.Typed("DATETIMEOFFSET",
            ((DateTimeOffset)value).ToString(
                WitTemporalLiteral.DATETIME_FORMAT + "zzz", CultureInfo.InvariantCulture));
}

/// <inheritdoc />
public sealed class WitDateOnlyTypeMapping : DateOnlyTypeMapping
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WitDateOnlyTypeMapping"/> class.
    /// </summary>
    /// <param name="storeType">The store type name.</param>
    public WitDateOnlyTypeMapping(string storeType)
        : base(storeType)
    {
    }

    private WitDateOnlyTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new WitDateOnlyTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
        => WitTemporalLiteral.Typed("DATE",
            ((DateOnly)value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
}

/// <inheritdoc />
public sealed class WitTimeOnlyTypeMapping : TimeOnlyTypeMapping
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WitTimeOnlyTypeMapping"/> class.
    /// </summary>
    /// <param name="storeType">The store type name.</param>
    public WitTimeOnlyTypeMapping(string storeType)
        : base(storeType)
    {
    }

    private WitTimeOnlyTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new WitTimeOnlyTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
        => WitTemporalLiteral.Typed("TIME",
            ((TimeOnly)value).ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture));
}
