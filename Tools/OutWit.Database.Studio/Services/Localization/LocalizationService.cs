using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace OutWit.Database.Studio.Services.Localization;

/// <summary>
/// The interface language, read from two catalogues embedded in this assembly.
///
/// <para>
/// <b>Embedded rather than satellite assemblies.</b> Both languages ship inside
/// <c>WitDatabaseStudio.dll</c>, so there is nothing for a packaging step to leave out. Studio is
/// packed for three platforms by Avalonia Parcel, and a satellite assembly that fails to arrive turns
/// the whole interface English with no error anywhere - a failure that looks exactly like nobody having
/// translated anything.
/// </para>
/// <para>
/// <b>Both catalogues are loaded at construction</b>, not on demand, for the same reason a missing key
/// answers with itself: so that switching the language cannot fail halfway through, holding half a
/// window in one language.
/// </para>
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    #region Constants

    public const string ENGLISH = "en";
    public const string RUSSIAN = "ru";

    private const string RESOURCE_PREFIX = "OutWit.Database.Studio.Resources.Strings.";

    #endregion

    #region Fields

    private readonly Dictionary<string, Catalogue> m_catalogues = new(StringComparer.Ordinal);

    #endregion

    #region Constructors

    public LocalizationService()
        : this(ENGLISH)
    {
    }

    public LocalizationService(string language)
    {
        Available =
        [
            new LanguageOption(ENGLISH, "English"),
            new LanguageOption(RUSSIAN, "Русский")
        ];

        foreach (var option in Available)
            m_catalogues[option.Code] = Catalogue.Load(option.Code);

        Language = Known(language);
    }

    #endregion

    #region Functions

    public void SetLanguage(string language)
    {
        var known = Known(language);

        if (known == Language)
            return;

        Language = known;

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// An unknown code falls back to English rather than throwing: the language comes out of a settings
    /// file a person can edit, and a typo there must not stop Studio from starting.
    /// </summary>
    private string Known(string? language)
    {
        return language != null && m_catalogues.ContainsKey(language) ? language : ENGLISH;
    }

    public string this[string key] => Current.Text(key) ?? key;

    /// <summary>
    /// Formatting is invariant on purpose. A count inside a sentence is still a number a person may
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
            // not a reason to take the window down. It is caught by LocalizationCoverageTests.
            return template;
        }
    }

    public string Plural(string key, long count)
    {
        var form = PluralRules.FormFor(Language, count);
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
    /// The catalogue for a language, for the coverage test to walk. Not part of the interface: nothing
    /// in the application needs to read a language it is not showing.
    /// </summary>
    public IReadOnlyDictionary<string, string> Texts(string language)
    {
        return m_catalogues.TryGetValue(language, out var catalogue)
            ? catalogue.Texts
            : new Dictionary<string, string>();
    }

    /// <summary>The plural entries for a language: key -> form -> template.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Plurals(string language)
    {
        return m_catalogues.TryGetValue(language, out var catalogue)
            ? catalogue.Plurals
            : new Dictionary<string, IReadOnlyDictionary<string, string>>();
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
    /// One language's strings. A plain entry is a string; an entry that is an object is a plural, whose
    /// properties are CLDR form names.
    /// </summary>
    private sealed class Catalogue
    {
        private Catalogue(Dictionary<string, string> texts,
            Dictionary<string, IReadOnlyDictionary<string, string>> plurals)
        {
            Texts = texts;
            Plurals = plurals;
        }

        public static Catalogue Load(string language)
        {
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            var plurals = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

            var name = RESOURCE_PREFIX + language + ".json";

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);

            if (stream == null)
                throw new InvalidOperationException(
                    $"The string catalogue '{name}' is not embedded in the assembly. "
                    + "It is an <EmbeddedResource> in the csproj; a renamed file drops it silently.");

            using var document = JsonDocument.Parse(stream);

            foreach (var property in document.RootElement.EnumerateObject())
            {
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

            return new Catalogue(texts, plurals);
        }

        public string? Text(string key) => Texts.GetValueOrDefault(key);

        public string? Plural(string key, string form)
        {
            if (!Plurals.TryGetValue(key, out var forms))
                return null;

            // A language whose rule asks for a form the catalogue does not carry falls back to "other",
            // which every catalogue has. Without this a Russian "few" with no entry would render the key.
            return forms.GetValueOrDefault(form) ?? forms.GetValueOrDefault(PluralRules.OTHER);
        }

        public Dictionary<string, string> Texts { get; }

        public Dictionary<string, IReadOnlyDictionary<string, string>> Plurals { get; }
    }

    #endregion
}
