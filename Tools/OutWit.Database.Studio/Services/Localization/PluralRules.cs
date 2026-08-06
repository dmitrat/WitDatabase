namespace OutWit.Database.Studio.Services.Localization;

/// <summary>
/// Which plural form a count takes, per language.
///
/// <para>
/// This exists because Russian has three forms where English has two, and the third cannot be reached
/// by substituting a number into one string: <c>1 строка</c>, <c>2 строки</c>, <c>5 строк</c>. Every
/// count Studio shows - rows returned, statements run, connections open, rows rejected by an import -
/// goes through here.
/// </para>
/// <para>
/// The rules are CLDR's for these two languages, written out rather than taken from
/// <c>CultureInfo</c>, which does not publish them. The awkward cases are the ones worth naming: 11 is
/// <b>many</b> though it ends in 1, and 111 is many for the same reason; 102 is <b>few</b>; 0 is many.
/// </para>
/// </summary>
public static class PluralRules
{
    #region Constants

    /// <summary>The form used when a language has only one plural besides the singular.</summary>
    public const string OTHER = "other";

    public const string ONE = "one";
    public const string FEW = "few";
    public const string MANY = "many";

    #endregion

    #region Functions

    /// <summary>
    /// The form <paramref name="count"/> takes in <paramref name="language"/>. A negative count is read
    /// by its magnitude - "-1 row" is singular in both languages.
    /// </summary>
    public static string FormFor(string language, long count)
    {
        var n = Math.Abs(count);

        return language switch
        {
            "ru" => Russian(n),
            _ => n == 1 ? ONE : OTHER
        };
    }

    private static string Russian(long n)
    {
        var lastTwo = n % 100;

        // 11..14 are many in Russian whatever their last digit is, which is why this is checked first:
        // 11 ends in 1 and is NOT "одна строка", 12 ends in 2 and is NOT "две строки".
        if (lastTwo is >= 11 and <= 14)
            return MANY;

        return (n % 10) switch
        {
            1 => ONE,
            2 or 3 or 4 => FEW,
            _ => MANY
        };
    }

    #endregion
}
