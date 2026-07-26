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
        Decimal
    }
}
