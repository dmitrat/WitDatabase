using Avalonia.Controls;
using OutWit.Database.Studio.Services.Localization;

namespace OutWit.Database.Studio.Ui;

/// <summary>
/// Puts the current language's strings where markup can reach them, as <c>{DynamicResource S.Some.Key}</c>.
///
/// <para>
/// The catalogue is the source and this is a bridge, not a second copy: markup reads what the service
/// holds, and a ViewModel reads the same service directly. One source of truth is what makes the
/// coverage test meaningful - two would let a window drift into strings nobody checks.
/// </para>
/// <para>
/// The dictionary is <b>replaced</b> on a language change rather than written into key by key. Avalonia
/// re-evaluates a <c>DynamicResource</c> when the dictionary it came from is swapped in the merged
/// list, which is what makes WS-63's "no restart" true of the markup as well as of the ViewModels.
/// </para>
/// </summary>
public static class LocalizedResources
{
    #region Constants

    /// <summary>
    /// Prefix for every string resource. It keeps the catalogue out of the way of the theme's own
    /// resource keys, which share this dictionary.
    /// </summary>
    public const string PREFIX = "S.";

    #endregion

    #region Fields

    private static ResourceDictionary? s_published;

    #endregion

    #region Functions

    /// <summary>
    /// Publishes the current language into <paramref name="resources"/> and keeps doing so whenever the
    /// language changes.
    /// </summary>
    public static void Attach(IResourceDictionary resources, ILocalizationService localization)
    {
        Publish(resources, localization);

        localization.LanguageChanged += (_, _) => Publish(resources, localization);
    }

    private static void Publish(IResourceDictionary resources, ILocalizationService localization)
    {
        if (localization is not LocalizationService service)
            return;

        var dictionary = new ResourceDictionary();

        foreach (var (key, value) in service.Texts(service.Language))
            dictionary.Add(PREFIX + key, value);

        if (s_published != null)
            resources.MergedDictionaries.Remove(s_published);

        resources.MergedDictionaries.Add(dictionary);

        s_published = dictionary;
    }

    #endregion
}
