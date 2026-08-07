using Avalonia.Data.Converters;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Converters;

/// <summary>
/// Which pane of the settings window is showing.
///
/// One converter per section rather than one taking a parameter: with compiled bindings a
/// <c>ConverterParameter</c> is an untyped string that nothing checks, so a renamed section would leave
/// a pane that is simply never visible and no build error anywhere. These go through
/// <see cref="SettingsViewModel"/>'s constants, so the compiler is the thing that notices.
/// </summary>
public static class SettingsSection
{
    public static readonly IValueConverter IsGeneral =
        new FuncValueConverter<string, bool>(section => section == SettingsViewModel.SECTION_GENERAL);

    public static readonly IValueConverter IsEditor =
        new FuncValueConverter<string, bool>(section => section == SettingsViewModel.SECTION_EDITOR);

    public static readonly IValueConverter IsData =
        new FuncValueConverter<string, bool>(section => section == SettingsViewModel.SECTION_DATA);

    public static readonly IValueConverter IsDiagnostics =
        new FuncValueConverter<string, bool>(section => section == SettingsViewModel.SECTION_DIAGNOSTICS);

    public static readonly IValueConverter IsAbout =
        new FuncValueConverter<string, bool>(section => section == SettingsViewModel.SECTION_ABOUT);
}
