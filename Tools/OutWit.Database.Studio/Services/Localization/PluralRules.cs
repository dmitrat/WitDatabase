namespace OutWit.Database.Studio.Services.Localization;

/// <summary>
/// Which plural form a count takes.
///
/// <para>
/// <b>A language names a FAMILY, it does not get a branch here.</b> The first version switched on the
/// language code with a case for Russian and an English-shaped default, which meant a new language
/// could not be added without editing this file - and a language whose rules nobody had thought about
/// would silently get English's two forms. A catalogue now declares its family in its own header, so
/// adding Ukrainian or Polish is a file, and only a genuinely new SHAPE of rule is code.
/// </para>
/// <para>
/// The families are CLDR's. The awkward cases are the ones worth naming: in the Slavic family 11 is
/// <b>many</b> though it ends in 1, and 111 is many for the same reason; 102 is <b>few</b>; 0 is many.
/// </para>
/// </summary>
public static class PluralRules
{
    #region Constants

    public const string ONE = "one";
    public const string FEW = "few";
    public const string MANY = "many";

    /// <summary>The form used when a family has only one plural besides the singular.</summary>
    public const string OTHER = "other";

    /// <summary>
    /// English, German, Dutch, the Scandinavian languages: one and other.
    /// </summary>
    public const string FAMILY_ONE_OTHER = "one-other";

    /// <summary>
    /// Russian, Ukrainian, Belarusian, Croatian, Serbian: one, few and many.
    /// </summary>
    public const string FAMILY_SLAVIC = "slavic";

    /// <summary>
    /// Chinese, Japanese, Korean, Turkish, Vietnamese: a count does not change the noun at all.
    /// </summary>
    public const string FAMILY_ONE_FORM = "one-form";

    /// <summary>What a catalogue gets if it names no family. Named so the fallback is visible.</summary>
    public const string FAMILY_DEFAULT = FAMILY_ONE_OTHER;

    #endregion

    #region Functions

    /// <summary>Every family this build understands, which is what a catalogue may name.</summary>
    public static IReadOnlyList<string> Families => [FAMILY_ONE_OTHER, FAMILY_SLAVIC, FAMILY_ONE_FORM];

    /// <summary>
    /// The forms a family uses, in the order a translator would write them. This is what the coverage
    /// test checks a catalogue against, so a Russian entry with two forms is caught rather than
    /// silently falling back.
    /// </summary>
    public static IReadOnlyList<string> FormsOf(string family) => family switch
    {
        FAMILY_SLAVIC => [ONE, FEW, MANY],
        FAMILY_ONE_FORM => [OTHER],
        _ => [ONE, OTHER]
    };

    /// <summary>Whether this build knows the family a catalogue named.</summary>
    public static bool IsKnown(string family) => Families.Contains(family);

    /// <summary>
    /// The form <paramref name="count"/> takes in <paramref name="family"/>. A negative count is read
    /// by its magnitude - "-1 row" is singular wherever singular exists.
    /// </summary>
    public static string FormFor(string family, long count)
    {
        var n = Math.Abs(count);

        return family switch
        {
            FAMILY_SLAVIC => Slavic(n),
            FAMILY_ONE_FORM => OTHER,
            _ => n == 1 ? ONE : OTHER
        };
    }

    private static string Slavic(long n)
    {
        var lastTwo = n % 100;

        // 11..14 are many whatever their last digit is, which is why this is checked first: 11 ends in
        // 1 and is NOT "одна строка", 12 ends in 2 and is NOT "две строки".
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
