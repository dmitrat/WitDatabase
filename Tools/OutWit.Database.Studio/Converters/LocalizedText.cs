using System.Globalization;
using Avalonia.Data.Converters;
using OutWit.Database.Studio.Services.Localization;

namespace OutWit.Database.Studio.Converters;

/// <summary>
/// A sentence with a value in it, translated - for the places markup used to build one out of
/// <c>&lt;Run&gt;</c> fragments.
///
/// <para>
/// <b>Why fragments had to go.</b> "Total:" + count + "rows" reads correctly in English and cannot be
/// translated at all: another language moves the number, inflects the noun after it, or puts the unit
/// first. Every such site is now ONE catalogue entry with a placeholder, so a translator sees the whole
/// sentence and may order it however the language requires.
/// </para>
/// <para>
/// <b>Why the template arrives through a binding rather than being looked up here.</b> A value
/// converter is asked once, when the binding evaluates; changing the language would leave the old
/// sentence on screen until something else moved. Passing the template in as
/// <c>&lt;DynamicResource ResourceKey="S.Some.Key"/&gt;</c> makes the language part of the binding, so
/// swapping the catalogue re-evaluates it - which is what WS-63's "no restart" means for these.
/// </para>
/// <para>
/// The formatting is <see cref="CultureInfo.InvariantCulture"/>, like everything else that can end up
/// in a statement (WS-65): the language of the interface never decides how a number is written.
/// </para>
/// </summary>
public static class LocalizedText
{
    #region Properties

    /// <summary>
    /// The service the plural rules come from. Set once at start-up next to
    /// <see cref="Ui.LocalizedResources.Attach"/>; a converter cannot be given a constructor argument.
    /// </summary>
    public static ILocalizationService? Service { get; set; }

    #endregion

    #region Converters

    /// <summary>
    /// <c>[template, arg0, arg1, ...]</c> -&gt; the template filled in. The template is the first
    /// binding and is meant to be a <c>DynamicResource</c>.
    /// </summary>
    public static readonly IMultiValueConverter Format = new FormatConverter();

    /// <summary>
    /// <c>[language, count]</c> -&gt; the count in the plural form its language asks for, from the
    /// catalogue key given as the converter parameter.
    ///
    /// <para>
    /// The language is a binding rather than a lookup for the same reason the template is: it is what
    /// makes the value change when the catalogue does. Nothing reads it - it is the trigger.
    /// </para>
    /// </summary>
    public static readonly IMultiValueConverter Plural = new PluralConverter();

    /// <summary>
    /// <c>[language, value]</c> -&gt; the catalogue entry for <c>parameter + value</c>.
    ///
    /// <para>
    /// For a list whose items are their own identity: the settings sections are compared by their
    /// value and drawn from it, so translating the value would break the comparison and not
    /// translating it leaves the list in English while its own heading is not. This draws the
    /// translation and leaves the value alone.
    /// </para>
    /// </summary>
    public static readonly IMultiValueConverter Keyed = new KeyedConverter();

    /// <summary>
    /// <c>[language, value]</c> -&gt; the value, or the catalogue entry named by the parameter when
    /// there is no value.
    ///
    /// <para>
    /// This is a translated <c>FallbackValue</c>, which a binding cannot have: writing
    /// <c>FallbackValue={DynamicResource S.X}</c> puts the markup extension OBJECT on screen -
    /// measured, the status bar read
    /// "Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension".
    /// </para>
    /// </summary>
    public static readonly IMultiValueConverter Or = new OrConverter();

    #endregion

    #region Classes

    private sealed class FormatConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count == 0 || values[0] is not string template)
                return null;

            var arguments = values.Skip(1).ToArray();

            // A value that has not arrived yet is not an error: bindings evaluate in whatever order
            // the tree is built, and half a sentence is better than an exception in a template.
            if (arguments.Any(argument => argument is Avalonia.UnsetValueType))
                return null;

            try
            {
                return string.Format(CultureInfo.InvariantCulture, template, arguments);
            }
            catch (FormatException)
            {
                // A catalogue whose placeholders do not match the call site is a defect in the
                // catalogue, caught by LocalizationServiceTests - not a reason to take a window down.
                return template;
            }
        }
    }

    private sealed class KeyedConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (parameter is not string prefix || Service == null)
                return null;

            return values.Count > 1 && values[1] is string value ? Service[prefix + value] : null;
        }
    }

    private sealed class OrConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (parameter is not string key || Service == null)
                return null;

            var value = values.Count > 1 ? values[1] as string : null;

            return string.IsNullOrEmpty(value) ? Service[key] : value;
        }
    }

    private sealed class PluralConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (parameter is not string key || Service == null)
                return null;

            var count = values.Count > 1 ? values[1] : null;

            return count switch
            {
                null => null,
                Avalonia.UnsetValueType => null,
                _ => Service.Plural(key, System.Convert.ToInt64(count, CultureInfo.InvariantCulture))
            };
        }
    }

    #endregion
}
