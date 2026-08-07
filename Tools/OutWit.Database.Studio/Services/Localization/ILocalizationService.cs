namespace OutWit.Database.Studio.Services.Localization;

/// <summary>
/// The interface language (WS-63).
///
/// <para>
/// Two things about this service are decisions rather than mechanics, and both are load-bearing.
/// </para>
/// <para>
/// <b>It never reads <c>CultureInfo.CurrentCulture</c>.</b> The language of the interface is a value
/// held here; the format of a data value is <see cref="System.Globalization.CultureInfo.InvariantCulture"/>
/// unless the user asks otherwise (WS-65). Wiring either of them to the thread culture is how the two
/// get confused, and confusing them means a decimal from the grid that will not paste into SQL.
/// </para>
/// <para>
/// <b>It translates Studio's own text only</b> (WS-64). Engine messages, SQL keywords, plan operators,
/// identifiers and format names are what a person searches for in the documentation, in an issue and in
/// the sources; a translated one cannot be found. There is no key here for any of them, and
/// <c>LocalizationCoverageTests</c> is what keeps it that way.
/// </para>
/// </summary>
public interface ILocalizationService
{
    /// <summary>The languages Studio can be switched to, in the order they are offered.</summary>
    IReadOnlyList<LanguageOption> Available { get; }

    /// <summary>The current language code.</summary>
    string Language { get; }

    /// <summary>
    /// Switches the language. Takes effect immediately and everywhere: no restart, and no dialog saying
    /// that a restart is needed (WS-63).
    /// </summary>
    void SetLanguage(string language);

    /// <summary>
    /// The string for <paramref name="key"/> in the current language.
    /// </summary>
    /// <remarks>
    /// A key with no entry answers with the key itself rather than with an empty string: a missing
    /// translation should be visible on screen and greppable, not a blank button. The build does not
    /// depend on that happening - <c>LocalizationCoverageTests</c> fails when a key is missing from
    /// either language.
    /// </remarks>
    string this[string key] { get; }

    /// <summary>Formats <paramref name="key"/> with <paramref name="arguments"/>, invariantly.</summary>
    string Format(string key, params object?[] arguments);

    /// <summary>
    /// The plural form of <paramref name="key"/> for <paramref name="count"/>, with the count
    /// substituted for <c>{0}</c>.
    /// </summary>
    /// <remarks>
    /// English needs two forms and Russian three, and the third is not reachable by substitution:
    /// 1 строка, 2 строки, 5 строк. This is the one place where a translation is not a lookup.
    /// </remarks>
    string Plural(string key, long count);

    /// <summary>Raised after the language has changed, so that anything holding a rendered string can re-render.</summary>
    event EventHandler? LanguageChanged;
}

/// <summary>A language Studio offers, named in that language.</summary>
/// <param name="Code">
/// The code stored in the settings file, taken from the catalogue's file name -
/// <c>Strings.fr.json</c> is <c>fr</c>. Nothing lists the codes anywhere else.
/// </param>
/// <param name="NativeName">
/// What the item says in the language picker, from the catalogue's own <c>$language</c> header.
/// Deliberately NOT translated: a person looking for their own language looks for its own name, and
/// "Russian" is no help to someone who cannot read the interface they are trying to change.
/// </param>
public sealed record LanguageOption(string Code, string NativeName);
