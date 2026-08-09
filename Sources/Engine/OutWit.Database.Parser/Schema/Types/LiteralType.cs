namespace OutWit.Database.Parser.Schema.Types
{
    public enum LiteralType
    {
        Null,
        Integer,
        Real,
        String,
        Blob,
        Boolean,
        CurrentTimestamp,
        CurrentDate,
        CurrentTime,

        /// <summary>
        /// An exact numeric literal: a decimal point with no exponent, or an integer too large for
        /// <see cref="long"/>. SQL treats these as exact (DECIMAL/NUMERIC); only the exponent form is
        /// approximate (<see cref="Real"/>).
        /// </summary>
        Decimal,

        /// <summary>A typed literal: <c>DATE '2026-07-01'</c>. The value is a <see cref="DateOnly"/>.</summary>
        /// <remarks>
        /// <para>
        /// The four below exist because a bare string is not the same thing. Text compared with a
        /// temporal column falls back to ORDINAL string comparison on this engine, so emitting
        /// <c>'2026-07-01'</c> where a date is meant would trade a loud parse error for silently wrong
        /// rows - which is why the fix for <c>Docs/KnownIssues.md</c> 2 belonged in the grammar rather
        /// than in the provider that emits the literal.
        /// </para>
        /// <para>
        /// <b>The WORD decides the type, never the content.</b> An offset inside a
        /// <see cref="Timestamp"/> is refused by name rather than dropped: PostgreSQL accepts
        /// <c>TIMESTAMP '… +03:00'</c> and silently discards the offset, which is a value changing
        /// meaning between two databases without anything being said.
        /// </para>
        /// </remarks>
        Date,

        /// <summary>A typed literal: <c>TIME '13:45:30'</c>. The value is a <see cref="TimeOnly"/>.</summary>
        Time,

        /// <summary>
        /// A typed literal: <c>TIMESTAMP '2026-07-01 13:45:30'</c>, or <c>DATETIME</c> - this engine's
        /// own name for the type in DDL. The value is a <see cref="System.DateTime"/>, and a text
        /// carrying an offset is refused rather than truncated.
        /// </summary>
        Timestamp,

        /// <summary>
        /// A typed literal: <c>DATETIMEOFFSET '2026-07-01 13:45:30 +03:00'</c>. The value is a
        /// <see cref="System.DateTimeOffset"/>. The word is the engine's own DDL type name and SQL
        /// Server's; PostgreSQL spells the same thing <c>TIMESTAMPTZ</c>.
        /// </summary>
        TimestampOffset
    }
}
