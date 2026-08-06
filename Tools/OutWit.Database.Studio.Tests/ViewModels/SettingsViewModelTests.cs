using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The settings window: five sections, applied immediately, no Save (WS-52, WS-53, WS-67).
///
/// <para>
/// The case that matters is the first one, and it is written against the DISK rather than against the
/// ViewModel: "applied immediately" means the change is in the file and in the live object without
/// anything having been pressed. Reading it back through a second service is the only way to tell that
/// apart from a value sitting in a property nobody persisted.
/// </para>
/// </summary>
[TestFixture]
public class SettingsViewModelTests
{
    #region Fields

    private StudioFixture m_studio = null!;
    private SettingsViewModel m_settings = null!;
    private ScriptedDialogService m_dialogs = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task Setup()
    {
        m_studio = await StudioFixture.CreateAsync(connect: false);

        m_dialogs = new ScriptedDialogService();
        m_studio.App.Dialogs = m_dialogs;

        m_settings = new SettingsViewModel(m_studio.App);
    }

    [TearDown]
    public async Task TearDown()
    {
        m_settings.Dispose();

        await m_studio.DisposeAsync();
    }

    #endregion

    #region No Save

    /// <summary>
    /// WS-52, measured where it counts. Nothing is pressed: the value is set on the live object, and it
    /// is then read back out of the file by a service that has never seen this one.
    /// </summary>
    [Test]
    public async Task ASettingIsAppliedAndPersistedWithNothingPressedAsync()
    {
        m_settings.Values.EditorFontSize = 19;
        m_settings.Values.KeywordCase = "Lower";

        // No command was invoked. The change starts its own write; this only waits for it, because a
        // test wants to READ the file and the application does not.
        await m_studio.Settings.FlushAsync();

        var reread = new SettingsService(NullLogger<SettingsService>.Instance, SettingsPath());

        Assert.Multiple(() =>
        {
            Assert.That(reread.Current.EditorFontSize, Is.EqualTo(19), "the change is in the file");
            Assert.That(reread.Current.KeywordCase, Is.EqualTo("Lower"));
        });
    }

    /// <summary>
    /// CONTROL for the case above: the ViewModel holds no copy at all, so there is nothing that could
    /// have been written back later. Reading through the service and reading through the window give
    /// the same object.
    /// </summary>
    [Test]
    public void TheWindowHoldsNoCopyOfAnySettingTest()
    {
        Assert.That(m_settings.Values, Is.SameAs(m_studio.Settings.Current));
    }

    /// <summary>
    /// And the negative control: a change made anywhere else is on screen without the window being told.
    /// This is what a Save button makes impossible.
    /// </summary>
    [Test]
    public void AChangeMadeElsewhereIsVisibleInTheWindowTest()
    {
        m_studio.Settings.Current.GridPageSize = 250;

        Assert.That(m_settings.Values.GridPageSize, Is.EqualTo(250));
    }

    #endregion

    #region The language

    /// <summary>
    /// WS-63: the language is a setting, so changing the setting changes the language - there is no
    /// second way to do it, and nothing to keep in step.
    /// </summary>
    [Test]
    public void ChoosingALanguageSwitchesTheInterfaceTest()
    {
        Assert.That(m_studio.App.Localization.Language, Is.EqualTo("en"));

        m_settings.Values.Language = "ru";

        Assert.Multiple(() =>
        {
            Assert.That(m_studio.App.Localization.Language, Is.EqualTo("ru"));
            Assert.That(m_studio.App.Localization["Common.Cancel"], Is.EqualTo("Отмена"));
        });
    }

    /// <summary>
    /// The offered languages come from the service rather than from a list in the window, so a language
    /// added to the catalogues appears here without anyone remembering to add it.
    /// </summary>
    [Test]
    public void TheLanguagesOfferedAreTheOnesTheServiceHasTest()
    {
        Assert.That(m_settings.Languages.Select(language => language.Code),
            Is.EqualTo(m_studio.App.Localization.Available.Select(language => language.Code)));
    }

    #endregion

    #region Sections

    [Test]
    public void TheFiveSectionsAreTheDesignsFiveTest()
    {
        Assert.That(m_settings.Sections, Is.EqualTo(new[]
        {
            SettingsViewModel.SECTION_GENERAL,
            SettingsViewModel.SECTION_EDITOR,
            SettingsViewModel.SECTION_DATA,
            SettingsViewModel.SECTION_DIAGNOSTICS,
            SettingsViewModel.SECTION_ABOUT
        }));
    }

    [Test]
    public void ShowSectionSelectsItTest()
    {
        m_settings.ShowSection(SettingsViewModel.SECTION_ABOUT);

        Assert.That(m_settings.SelectedSection, Is.EqualTo(SettingsViewModel.SECTION_ABOUT));
    }

    /// <summary>A section that does not exist leaves the window where it was rather than blanking it.</summary>
    [Test]
    public void AnUnknownSectionIsIgnoredTest()
    {
        m_settings.ShowSection("Nonsense");

        Assert.That(m_settings.SelectedSection, Is.EqualTo(SettingsViewModel.SECTION_GENERAL));
    }

    /// <summary>
    /// WS-53: Help &gt; About opens the settings on the About section, and there is no About window any
    /// more. The dialog service records which ViewModel it was shown, so the section can be asserted.
    /// </summary>
    [Test]
    public async Task HelpAboutOpensTheSettingsOnTheAboutSectionAsync()
    {
        await StudioFixture.PressAsync(m_studio.MainWindow.AboutCommand);

        Assert.Multiple(() =>
        {
            Assert.That(m_dialogs.Shown, Does.Contain(nameof(IDialogService.ShowSettingsAsync)));
            Assert.That(m_dialogs.LastSettings?.SelectedSection, Is.EqualTo(SettingsViewModel.SECTION_ABOUT));
        });
    }

    #endregion

    #region Diagnostics and About

    /// <summary>
    /// "Open the folder" asks for the FOLDER and "Last log" asks for the FILE. They are one character
    /// apart in the code and completely different on screen.
    /// </summary>
    [Test]
    public async Task TheLogButtonsAskForTheFolderAndForTheFileAsync()
    {
        await StudioFixture.PressAsync(m_settings.OpenLogFolderCommand);
        await StudioFixture.PressAsync(m_settings.OpenLastLogCommand);

        Assert.Multiple(() =>
        {
            Assert.That(m_dialogs.Revealed, Has.Count.EqualTo(2));
            Assert.That(m_dialogs.Revealed[0], Is.EqualTo(Path.GetDirectoryName(m_settings.LogFilePath)));
            Assert.That(m_dialogs.Revealed[1], Is.EqualTo(m_settings.LogFilePath));
        });
    }

    /// <summary>
    /// The block for an issue (WS-53). The file format version is the one thing nobody thinks to
    /// include and the one that decides whether a downgrade is possible, so it is asserted by name.
    /// </summary>
    [Test]
    public async Task CopyDetailsPutsTheVersionsOnTheClipboardAsync()
    {
        await StudioFixture.PressAsync(m_settings.CopyDetailsCommand);

        Assert.Multiple(() =>
        {
            Assert.That(m_dialogs.Copied, Does.Contain("WitDatabase Studio"));
            Assert.That(m_dialogs.Copied, Does.Contain(m_settings.EngineVersion));
            Assert.That(m_dialogs.Copied, Does.Contain("File format"));
            Assert.That(m_dialogs.Copied, Does.Contain(m_settings.FileFormatVersion));
        });
    }

    /// <summary>
    /// The format version is read from the engine's own constant, so it cannot drift from what the
    /// build actually writes. Pinned as a value as well, because a change here is a compatibility
    /// event that should never happen quietly.
    /// </summary>
    [Test]
    public void TheFileFormatVersionComesFromTheEngineTest()
    {
        Assert.That(m_settings.FileFormatVersion, Is.EqualTo("1.1"));
    }

    #endregion

    #region Reset and the recent list

    [Test]
    public async Task ClearingTheRecentListEmptiesItAsync()
    {
        await m_studio.Settings.AddRecentFileAsync(m_studio.DatabasePath);

        Assume.That(m_settings.Values.RecentFiles, Is.Not.Empty);

        await StudioFixture.PressAsync(m_settings.ClearRecentCommand);

        Assert.That(m_settings.Values.RecentFiles, Is.Empty);
    }

    /// <summary>
    /// Reset puts the settings back and leaves the recent list alone. The list is not a setting - it is
    /// what the person has been doing - and "reset settings" must not read as "forget my databases".
    /// </summary>
    [Test]
    public async Task ResetRestoresDefaultsAndKeepsTheRecentListAsync()
    {
        await m_studio.Settings.AddRecentFileAsync(m_studio.DatabasePath);

        m_settings.Values.EditorFontSize = 22;
        m_settings.Values.Language = "ru";
        m_settings.Values.AskBeforeUnfilteredWrite = false;

        await StudioFixture.PressAsync(m_settings.ResetSettingsCommand);

        Assert.Multiple(() =>
        {
            Assert.That(m_settings.Values.EditorFontSize, Is.EqualTo(14));
            Assert.That(m_settings.Values.Language, Is.EqualTo("en"));
            Assert.That(m_settings.Values.AskBeforeUnfilteredWrite, Is.True);
            Assert.That(m_settings.Values.RecentFiles, Has.Count.EqualTo(1),
                "the recent list is not a setting and is not what Reset is for");
        });
    }

    /// <summary>
    /// And reset goes through the same live object, so the language follows it back. Without this the
    /// window would say English while the interface stayed Russian.
    /// </summary>
    [Test]
    public async Task ResetTakesTheInterfaceLanguageBackWithItAsync()
    {
        m_settings.Values.Language = "ru";

        Assume.That(m_studio.App.Localization.Language, Is.EqualTo("ru"));

        await StudioFixture.PressAsync(m_settings.ResetSettingsCommand);

        Assert.That(m_studio.App.Localization.Language, Is.EqualTo("en"));
    }

    #endregion

    #region The catalogue of confirmations (WS-67)

    /// <summary>
    /// Every modal question the product asks is a property here and is on by default except the long
    /// script. The list IS the complete set of questions: one that is not here is one nobody can stop.
    /// </summary>
    [Test]
    public void EveryConfirmationIsInTheCatalogueTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(m_settings.Values.AskBeforeClosingEditedTab, Is.True);
            Assert.That(m_settings.Values.AskBeforeDroppingObject, Is.True);
            Assert.That(m_settings.Values.AskBeforeUnfilteredWrite, Is.True);
            Assert.That(m_settings.Values.AskBeforeLongScript, Is.False,
                "the only one off by default - a long script is normal work");
        });
    }

    #endregion

    #region Tools

    private string SettingsPath()
    {
        return Path.Combine(m_studio.Root, "settings", "settings.json");
    }

    #endregion
}
