using System.Text.Json;
using OutWit.Database.Core.Builder;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// WS-58, second half: replacing a password on a database that already has one is a rewrap, not a
/// migration.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this fixture is about.</b> The engine has done the cheap thing since the format change -
/// the data key is random and the password only wraps it - and Studio kept building a new database
/// and copying everything into it, because nothing above <c>OutWit.Database.Core</c> exposed the
/// rewrap. The capability existed and no path reached it, which is phase 13's defect one layer down.
/// </para>
/// <para>
/// <b>Asserted on the session and on the ViewModel, not on the engine.</b> The engine's half is
/// covered by <c>PasswordRewrapTests</c> and <c>PasswordChangeThroughTheProviderTests</c> - pages
/// untouched, the old password refused, indexes still readable. What only Studio can show is the
/// BRANCH: that a replacement takes the rewrap, that the other two cases still migrate, and that the
/// password this session remembers is updated - because the byte copy and "open the copy beside the
/// original" both reconnect, and a stale one would be refused with the password the user has just
/// replaced.
/// </para>
/// </remarks>
[TestFixture]
public class PasswordRewrapInStudioTests
{
    #region Constants

    private const string PASSWORD = "the original password";

    private const string NEW_PASSWORD = "the replacement password";

    #endregion

    #region The branch

    [Test]
    public async Task ReplacingAPasswordOnAnEncryptedDatabaseIsARewrapAsync()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        var session = await OpenEncryptedAsync(fixture);
        var viewModel = new ChangePasswordViewModel(fixture.App, session);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Change, Is.EqualTo(PasswordChange.Replace),
                "an encrypted database opens this window on the replacement case");

            Assert.That(viewModel.IsRewrap, Is.True,
                "and a replacement on an encrypted database must take the rewrap - if this is false "
                + "the capability has stopped below Studio again, which is the whole finding");
        });
    }

    /// <summary>
    /// Control: encrypting a database that has no password is NOT a rewrap and must still migrate.
    /// Without this, <c>IsRewrap</c> could be a property that is simply always true.
    /// </summary>
    [Test]
    public async Task ControlEncryptingAnUnencryptedDatabaseStillMigratesAsync()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        var viewModel = new ChangePasswordViewModel(fixture.App, fixture.Database);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Change, Is.EqualTo(PasswordChange.Encrypt),
                "CONTROL: an unencrypted database opens this window on the encrypt case");

            Assert.That(viewModel.IsRewrap, Is.False,
                "CONTROL: there is no wrapped key to rewrap, so this stays a migration - and it "
                + "always will, because a database with no preamble has nowhere to put one");
        });
    }

    /// <summary>
    /// Control: removing encryption is not a rewrap either. The pages are ciphertext, and no rewrite
    /// of 60 bytes turns them back.
    /// </summary>
    [Test]
    public async Task ControlRemovingEncryptionStillMigratesAsync()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        var session = await OpenEncryptedAsync(fixture);
        var viewModel = new ChangePasswordViewModel(fixture.App, session)
        {
            Change = PasswordChange.Remove
        };

        Assert.That(viewModel.IsRewrap, Is.False,
            "CONTROL: taking encryption away has to rewrite every page, so it is a migration");
    }

    /// <summary>
    /// Both computed properties the window binds are ANNOUNCED when the choice moves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Asserted on the announcement and not on the value</b>, which is the whole of phase 17 in one
    /// case: <c>IsFiltering</c> returned the right answer to everyone who asked and told nobody, two
    /// green tests read it and found it true, and the panel it was bound to never moved. A value that
    /// is never wrong is exactly the thing a value assertion cannot catch.
    /// </para>
    /// <para>
    /// <c>NeedsPassword</c> is here because it was already silent before this work: choosing «remove
    /// the encryption» left the password fields on screen. Found while wiring <c>IsRewrap</c> next
    /// to it.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TheChoiceAnnouncesBothPropertiesTheWindowBindsAsync()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        var session = await OpenEncryptedAsync(fixture);
        var viewModel = new ChangePasswordViewModel(fixture.App, session);

        var announced = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != null)
                announced.Add(args.PropertyName);
        };

        viewModel.Change = PasswordChange.Remove;

        Assert.Multiple(() =>
        {
            Assert.That(announced, Does.Contain(nameof(ChangePasswordViewModel.IsRewrap)),
                "the window hides the destination box and swaps its explanation on IsRewrap, so a "
                + "change of choice that does not announce it leaves both wrong");

            Assert.That(announced, Does.Contain(nameof(ChangePasswordViewModel.NeedsPassword)),
                "and the password fields are bound to NeedsPassword - unannounced, they stay on "
                + "screen while the encryption is being removed");

            Assert.That(viewModel.IsRewrap, Is.False,
                "CONTROL: and the value really did move, or the announcement is about nothing");
        });
    }

    #endregion

    #region What it does

    [Test]
    public async Task TheRewrapReplacesThePasswordAndTellsTheSessionAsync()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        var session = await OpenEncryptedAsync(fixture);
        var path = session.Connection.FilePath!;

        var viewModel = new ChangePasswordViewModel(fixture.App, session)
        {
            NewPassword = NEW_PASSWORD,
            NewPasswordAgain = NEW_PASSWORD
        };

        Assert.That(viewModel.CanMigrate, Is.True,
            "a rewrap needs no destination file, so the button must be live without one");

        viewModel.MigrateCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsDone, Is.True, $"the rewrap must finish: {viewModel.Message}");

            Assert.That(session.Connection.Password, Is.EqualTo(NEW_PASSWORD),
                "and the session must remember the NEW password - the byte copy and the migration "
                + "both reconnect with it, and a stale one is refused");

            Assert.That(session.IsConnected, Is.True,
                "the connection stays open: this is a page write, not a reopen");
        });

        // The file itself, asked from outside Studio, is the assertion that cannot be satisfied by a
        // ViewModel flag.
        await fixture.Connections.CloseAsync(session);

        Assert.That(OpensWith(path, NEW_PASSWORD), Is.True, "the new password must open the file");
        Assert.That(OpensWith(path, PASSWORD), Is.False, "and the old one must not");
    }

    #endregion

    #region What the window says

    /// <summary>
    /// The rewrap carries a warning of its own, and the migration does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a warning at all, when nothing can be lost.</b> The data key does not move, so every
    /// page stays exactly as readable as it was. What changes is that the migration's safety net is
    /// gone: it wrote a NEW database and left the original alone, so a mistake cost nothing. A rewrap
    /// changes the only copy, and a new password that is forgotten locks the database as surely as
    /// any other - with no old one kept anywhere to fall back to.
    /// </para>
    /// <para>
    /// Asserted on the markup as well as on the catalogue, because a string that exists and is drawn
    /// nowhere is the exact shape this application keeps finding - and a warning nobody sees is worse
    /// than none, since it reads in the source like the case is covered.
    /// </para>
    /// </remarks>
    [Test]
    public void TheRewrapCarriesItsOwnWarningAndTheMigrationDoesNotTest()
    {
        var markup = Markup("Views/Dialogs/ChangePasswordDialog.axaml");

        Assert.Multiple(() =>
        {
            Assert.That(markup, Does.Contain("S.Password.RewrapWarning"),
                "the warning has to be in the window, not only in the catalogue");

            Assert.That(markup, Does.Contain("IsVisible=\"{Binding IsRewrap}\""),
                "and it has to be gated on IsRewrap - shown for a migration it would be wrong, "
                + "because a migration leaves the original where it is");

            Assert.That(markup, Does.Contain("Wit.Warn.Surface"),
                "and drawn as a warning rather than as another grey line of prose");
        });
    }

    /// <summary>
    /// The button that OPENS the dialog does not promise what the dialog will decide.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Its accessible name said «Change the password by migrating into a new database», and had
    /// said so since before the rewrap existed. Since 13.1.0 replacing a password rewrites 60
    /// bytes and copies nothing; only adding encryption or taking it away builds a new database.
    /// The dialog itself has been right about this since 2026-08-15 - it swaps its explanation on
    /// <c>IsRewrap</c> - so the one wrong sentence was the one a person read BEFORE opening it.
    /// </para>
    /// <para>
    /// Reported from outside on 2026-08-16: the button and the capability matrix of the same tab
    /// disagreed, and «rewrites 60 bytes» against «copies the database» are different promises on
    /// a 40 GB file.
    /// </para>
    /// <para>
    /// The control is the other half - the migration explanation still has to say that a new
    /// database is built, or the rule would pass over a catalogue that had simply lost the words.
    /// </para>
    /// </remarks>
    [TestCase("en", "migrating", "new database")]
    [TestCase("ru", "переносом", "новую базу")]
    public void TheButtonThatOpensThePasswordDialogPromisesNoMigrationTest(
        string language, string verb, string noun)
    {
        using var catalogue = JsonDocument.Parse(Markup($"Resources/Strings.{language}.json"));

        var button = catalogue.RootElement.GetProperty("Password.Open.Name").GetString() ?? string.Empty;
        var migration = catalogue.RootElement.GetProperty("Password.Explanation").GetString() ?? string.Empty;

        Assert.Multiple(() =>
        {
            Assert.That(button, Does.Not.Contain(verb).IgnoreCase,
                "the button opens a dialog that decides between a rewrap and a migration, so it "
                + "cannot announce one of them");

            Assert.That(button, Does.Not.Contain(noun).IgnoreCase,
                "and a replacement builds no new database at all");

            // CONTROL: the migration arm still describes a migration.
            Assert.That(migration, Does.Contain(noun).IgnoreCase,
                "CONTROL: the migration explanation must still say a new database is built");
        });
    }

    /// <summary>
    /// Nothing that describes the MIGRATION is left on screen during a rewrap.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three pieces of chrome had to be gated, and all three were found by looking at the window
    /// rather than by reading it: a destination box for a file that is never created, a checkbox
    /// offering to connect to a new database that does not exist, and a sentence explaining that the
    /// transfer is a script counted on both sides - when there is only one side. The button said
    /// «Transfer».
    /// </para>
    /// <para>
    /// This is the application's most familiar defect, and the reason the rule is written over all of
    /// them at once: a branch added under chrome that still describes the other operation. The same
    /// shape as a status bar reading «Editing» over a table whose every editing control is disabled.
    /// </para>
    /// </remarks>
    [Test]
    public void NothingAboutTheMigrationIsShownDuringARewrapTest()
    {
        var markup = Markup("Views/Dialogs/ChangePasswordDialog.axaml");

        string[] migrationOnly =
        [
            "S.Password.NewFile",           // the destination box
            "S.Password.ConnectAfterwards", // connect to the new database
            "S.Password.ItIsAScript",       // counted on both sides
            "S.Password.Explanation"        // "this needs a new database"
        ];

        Assert.Multiple(() =>
        {
            foreach (var key in migrationOnly)
            {
                var block = Around(markup, key);

                Assert.That(block, Does.Contain("!IsRewrap"),
                    $"{key} belongs to the migration and must be hidden for a rewrap - it describes "
                    + "a file, a connection or a count that a rewrap does not produce");
            }

            // CONTROL: the rule can tell the two apart, or every assertion above passes on a window
            // where nothing is gated at all.
            Assert.That(Around(markup, "S.Password.RewrapWarning"), Does.Not.Contain("!IsRewrap"),
                "CONTROL: the rewrap's own warning must NOT be hidden for a rewrap");
        });
    }

    /// <summary>
    /// The markup around a resource key, which is where its <c>IsVisible</c> lives. Crude and
    /// deliberate: a window is a few hundred lines and the elements are small, so a fixed span either
    /// side of the key is enough and needs no parser.
    /// </summary>
    private static string Around(string markup, string key)
    {
        var index = markup.IndexOf(key, StringComparison.Ordinal);

        Assert.That(index, Is.GreaterThanOrEqualTo(0), $"{key} is not in this window at all");

        var start = Math.Max(0, index - 500);
        var end = Math.Min(markup.Length, index + 300);

        return markup[start..end];
    }

    /// <summary>
    /// The `WS-55` provenance row no longer says the engine cannot do this. It said
    /// «not in the engine» for a whole release while the rewrap sat in <c>Core</c> unreachable, which
    /// is the matrix failing at its one job.
    /// </summary>
    [Test]
    public void TheProvenanceMatrixNoLongerDeniesTheRewrapTest()
    {
        var row = StorageCapabilities.Matrix
            .SingleOrDefault(item => item.OperationKey == "Database.Cap.ChangePassword");

        Assert.That(row, Is.Not.Null, "the row must still be in the matrix");

        Assert.Multiple(() =>
        {
            Assert.That(row!.Availability, Is.EqualTo(StorageAvailability.Available),
                "replacing a password is in the engine and reachable from Studio now. It was in the "
                + "engine and unreachable for a whole release, and NeedsProviderAccess is the state "
                + "that says exactly that - this row claimed NotInEngine instead");

            Assert.That(row.SourceKey, Is.EqualTo("Database.Cap.Source.Rewrap"),
                "and its source is the preamble, not «nothing - the key is derived at creation», "
                + "which stopped being true at the format change");
        });
    }

    /// <summary>
    /// The Database tab says which of the two encryptions this file has.
    /// </summary>
    /// <remarks>
    /// «Key derived from the password» was on the card for every encrypted database, and it is the
    /// fourth place that carried the pre-format-change explanation - after the dialog, the migration
    /// branch and the `WS-55` row. It stayed true of files written before the change, which is why it
    /// is kept rather than replaced: the card now answers the question the file actually asks.
    /// </remarks>
    [Test]
    public async Task TheDatabaseTabSaysTheKeyIsWrappedNotDerivedAsync()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        var session = await OpenEncryptedAsync(fixture);
        var tab = await fixture.Workspace.OpenDatabaseTabAsync(session);

        Assert.Multiple(() =>
        {
            Assert.That(tab.EncryptionDetail, Does.Contain("unwraps"),
                "a database whose password can be replaced has a WRAPPED key, and the card has to "
                + "say so - it said «derived» for every encrypted database, which is what made the "
                + "rewrap look impossible");

            Assert.That(tab.EncryptionDetail, Does.Not.Contain("derived"),
                "and it must not still say the old thing");
        });
    }

    /// <summary>
    /// Control: an UNENCRYPTED database says nothing about a key at all - so the assertion above is
    /// about which sentence was chosen and not about a card that always says the same thing.
    /// </summary>
    [Test]
    public async Task ControlAnUnencryptedDatabaseSaysNothingAboutAKeyAsync()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        var tab = await fixture.Workspace.OpenDatabaseTabAsync(fixture.Database);

        Assert.That(tab.EncryptionDetail, Is.Empty,
            "CONTROL: there is no key to describe when nothing is encrypted");
    }

    #endregion

    #region Tools

    private static string Markup(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Tools", "OutWit.Database.Studio");

            if (Directory.Exists(Path.Combine(candidate, "Views")))
            {
                var path = Path.Combine(candidate, relative.Replace('/', Path.DirectorySeparatorChar));

                Assert.That(File.Exists(path), Is.True, $"{relative} must be where this fixture says");

                return File.ReadAllText(path);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("the Studio project was not found from " + AppContext.BaseDirectory);
    }

    private static async Task<IDatabaseSession> OpenEncryptedAsync(StudioFixture fixture)
    {
        var path = Path.Combine(fixture.Root, "encrypted.witdb");

        StudioFixture.CreateDatabaseOnDisk(path, PASSWORD);

        var session = await fixture.Connections.OpenAsync(new ConnectionInfo
        {
            FilePath = path,
            StorageEngine = "btree",
            IsEncrypted = true,
            Password = PASSWORD
        });

        Assert.That(session, Is.Not.Null, "the encrypted connection must open");

        return session!;
    }

    private static bool OpensWith(string path, string password)
    {
        try
        {
            using var database = new WitDatabaseBuilder()
                .WithFilePath(path).WithBTree().WithEncryption(password).Build();

            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion
}
