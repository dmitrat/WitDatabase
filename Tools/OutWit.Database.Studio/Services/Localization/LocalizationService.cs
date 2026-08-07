using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace OutWit.Database.Studio.Services.Localization;

/// <summary>
/// The interface language, read from the catalogues embedded in this assembly.
///
/// <para>
/// <b>A language is a FILE, not a code change.</b> The catalogues are discovered from the assembly
/// manifest rather than listed here: drop <c>Resources\Strings.fr.json</c> into the project and French
/// appears in the settings, in the coverage tests and in the language picker without anything else
/// being edited. The first version had the two languages written out in the constructor and a
/// <c>switch</c> on the code in the plural rules, which made "extensible" a description of the
/// interface rather than of the mechanism.
/// </para>
/// <para>
/// A catalogue carries its own header - the name of the language in that language, and the plural
/// family it belongs to - because those are properties of the language and belong with its words.
/// </para>
/// <para>
/// <b>Embedded rather than satellite assemblies.</b> Studio is packed for three platforms, and a
/// satellite that fails to arrive turns the interface English with no error anywhere - a failure
/// indistinguishable from nobody having translated it.
/// </para>
/// <para>
/// It never reads <see cref="CultureInfo.CurrentCulture"/>. The language of the interface is a value
/// held here; the format of a data value is invariant unless the user asks otherwise (WS-65). Wiring
/// either to the thread culture is how the two get confused, and confusing them means a decimal from
/// the grid that will not paste into SQL.
/// </para>
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    #region Constants

    /// <summary>
    /// The language every catalogue is compared against and the one a missing or unknown code falls
    /// back to. It is the language the sources are written in, so it is the one that is always
    /// complete.
    /// </summary>
    public const string BASE_LANGUAGE = "en";

    private const string RESOURCE_PREFIX = "OutWit.Database.Studio.Resources.Strings.";
    private const string RESOURCE_SUFFIX = ".json";

    /// <summary>The catalogue's own header keys. They are metadata, not strings to show.</summary>
    public const string KEY_LANGUAGE_NAME = "$language";
    public const string KEY_PLURAL_FAMILY = "$plural";

    #endregion

    #region Fields

    private readonly Dictionary<string, Catalogue> m_catalogues = new(StringComparer.Ordinal);

    #endregion

    #region Constructors

    public LocalizationService()
        : this(BASE_LANGUAGE)
    {
    }

    public LocalizationService(string language)
    {
        foreach (var code in Discover())
            m_catalogues[code] = Catalogue.Load(code);

        if (!m_catalogues.ContainsKey(BASE_LANGUAGE))
            throw new InvalidOperationException(
                $"The base catalogue '{RESOURCE_PREFIX}{BASE_LANGUAGE}{RESOURCE_SUFFIX}' is not embedded in "
                + "the assembly. Every other language is compared against it and falls back to it.");

        // The base language first, the rest alphabetically by their own name - so a picker does not
        // reorder itself when a language is added.
        Available = m_catalogues
            .OrderByDescending(entry => entry.Key == BASE_LANGUAGE)
            .ThenBy(entry => entry.Value.NativeName, StringComparer.CurrentCulture)
            .Select(entry => new LanguageOption(entry.Key, entry.Value.NativeName))
            .ToList();

        Language = Known(language);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Every language that shipped, taken from the manifest. This is the whole of "a language is a
    /// file": nothing else enumerates them.
    /// </summary>
    private static IEnumerable<string> Discover()
    {
        return Assembly.GetExecutingAssembly()
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(RESOURCE_PREFIX, StringComparison.Ordinal)
                && name.EndsWith(RESOURCE_SUFFIX, StringComparison.Ordinal))
            .Select(name => name[RESOURCE_PREFIX.Length..^RESOURCE_SUFFIX.Length])
            .Where(code => code.Length > 0)
            .OrderBy(code => code, StringComparer.Ordinal);
    }

    public void SetLanguage(string language)
    {
        var known = Known(language);

        if (known == Language)
            return;

        Language = known;

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// An unknown code falls back to the base language rather than throwing: the language comes out of
    /// a settings file a person can edit, and a typo there must not stop Studio from starting.
    /// </summary>
    private string Known(string? language)
    {
        return language != null && m_catalogues.ContainsKey(language) ? language : BASE_LANGUAGE;
    }

    public string this[string key] => Current.Text(key) ?? key;

    /// <summary>
    /// Formatting is invariant on purpose. A number inside a sentence is still a number a person may
    /// copy, and the thread culture has no business deciding how it is written - the same rule the
    /// value formatter follows for WS-65.
    /// </summary>
    public string Format(string key, params object?[] arguments)
    {
        var template = this[key];

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, arguments);
        }
        catch (FormatException)
        {
            // A translation whose placeholders do not match the call site is a defect in the catalogue,
            // not a reason to take the window down. It is caught by LocalizationServiceTests.
            return template;
        }
    }

    public string Plural(string key, long count)
    {
        var form = PluralRules.FormFor(Current.PluralFamily, count);
        var template = Current.Plural(key, form) ?? Current.Text(key) ?? key;

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, count);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    private Catalogue Current => m_catalogues[Language];

    /// <summary>
    /// The strings of a language, for the coverage tests to walk. Not part of the interface: nothing in
    /// the application needs to read a language it is not showing.
    /// </summary>
    public IReadOnlyDictionary<string, string> Texts(string language)
    {
        return m_catalogues.TryGetValue(language, out var catalogue)
            ? catalogue.Texts
            : new Dictionary<string, string>();
    }

    /// <summary>The plural entries of a language: key -> form -> template.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Plurals(string language)
    {
        return m_catalogues.TryGetValue(language, out var catalogue)
            ? catalogue.Plurals
            : new Dictionary<string, IReadOnlyDictionary<string, string>>();
    }

    /// <summary>The plural family a catalogue declared, so a test can check its entries against it.</summary>
    public string PluralFamilyOf(string language)
    {
        return m_catalogues.TryGetValue(language, out var catalogue)
            ? catalogue.PluralFamily
            : PluralRules.FAMILY_DEFAULT;
    }

    #endregion

    #region Properties

    public IReadOnlyList<LanguageOption> Available { get; }

    public string Language { get; private set; }

    #endregion

    #region Events

    public event EventHandler? LanguageChanged;

    #endregion

    #region Classes

    /// <summary>
    /// One language's strings and its header. A plain entry is a string; an entry that is an object is
    /// a plural, whose properties are CLDR form names; an entry whose key starts with <c>$</c> is
    /// metadata about the language rather than something to show.
    /// </summary>
    private sealed class Catalogue
    {
        private Catalogue(Dictionary<string, string> texts,
            Dictionary<string, IReadOnlyDictionary<string, string>> plurals,
            string nativeName,
            string pluralFamily)
        {
            Texts = texts;
            Plurals = plurals;
            NativeName = nativeName;
            PluralFamily = pluralFamily;
        }

        public static Catalogue Load(string language)
        {
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            var plurals = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

            var name = RESOURCE_PREFIX + language + RESOURCE_SUFFIX;

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);

            if (stream == null)
                throw new InvalidOperationException($"The string catalogue '{name}' disappeared between "
                    + "being listed in the manifest and being read.");

            using var document = JsonDocument.Parse(stream);

            var nativeName = language;
            var family = PluralRules.FAMILY_DEFAULT;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name == KEY_LANGUAGE_NAME)
                {
                    nativeName = property.Value.GetString() ?? language;
                    continue;
                }

                if (property.Name == KEY_PLURAL_FAMILY)
                {
                    family = property.Value.GetString() ?? PluralRules.FAMILY_DEFAULT;
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    var forms = new Dictionary<string, string>(StringComparer.Ordinal);

                    foreach (var form in property.Value.EnumerateObject())
                        forms[form.Name] = form.Value.GetString() ?? string.Empty;

                    plurals[property.Name] = forms;
                }
                else
                {
                    texts[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }

            return new Catalogue(texts, plurals, nativeName, family);
        }

        public string? Text(string key) => Texts.GetValueOrDefault(key);

        public string? Plural(string key, string form)
        {
            if (!Plurals.TryGetValue(key, out var forms))
                return null;

            // A family that asks for a form the catalogue does not carry falls back to "other", which
            // every catalogue has. Without this a missing Slavic "few" would render the key.
            return forms.GetValueOrDefault(form) ?? forms.GetValueOrDefault(PluralRules.OTHER);
        }

        public Dictionary<string, string> Texts { get; }

        public Dictionary<string, IReadOnlyDictionary<string, string>> Plurals { get; }

        /// <summary>The language's name in its own language - what the picker shows.</summary>
        public string NativeName { get; }

        /// <summary>The plural family it declared, or the default if it declared none.</summary>
        public string PluralFamily { get; }
    }

    #endregion
}
