using OutWit.Database.AdoNet;
using OutWit.Database.AdoNet.Maintenance;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Services.Localization;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The «База» tab (WS-54), against a real database of each kind.
///
/// <para>
/// <b>The criterion this stage was written to, stated first because the plan gives none:</b> the tab
/// says about this database only what something actually read, it names where each fact came from, and
/// every button on it either does what it says or is not there.
/// </para>
/// <para>
/// <b>Every case is a PAIR.</b> A tab that answered "LSM" to everything would pass an LSM-only case,
/// and one that answered "B-Tree" would pass the other; the two arms together are what make either
/// worth running. Same shape as the detector's transaction-model case, which was written the same day
/// and for the same reason.
/// </para>
/// </summary>
[TestFixture]
public class DatabaseTabTests
{
    #region Fields

    private StudioFixture m_fixture = null!;

    #endregion

    #region Setup

    [TearDown]
    public async Task TearDown()
    {
        await m_fixture.DisposeAsync();
    }

    #endregion

    #region The facts

    /// <summary>
    /// A paged database says it is one, and the LSM panel - with its Compact button - is absent from
    /// it entirely (WS-55).
    /// </summary>
    [Test]
    public async Task APagedDatabaseIsDescribedAsOneAsync()
    {
        m_fixture = await StudioFixture.CreateAsync(StudioStorage.BTree);

        var tab = await OpenAsync();

        Assert.Multiple(() =>
        {
            Assert.That(tab.Overview!.StoreProviderKey, Is.EqualTo("btree"));
            Assert.That(tab.Overview.IsDirectory, Is.False);

            Assert.That(tab.IsLsm, Is.False);
            Assert.That(tab.Overview.Lsm, Is.Null);

            // The transaction model comes from the chain this connection ASSEMBLED, which is the live
            // answer and the one that survives a header nobody can read.
            Assert.That(tab.Overview.ChainHasMvcc, Is.True);
        });
    }

    /// <summary>
    /// The configuration block is there for BOTH - a database that was on disk first and one the open
    /// created - and the pair is still the whole case.
    ///
    /// <para>
    /// <b>This case inverted on 2026-08-09 and that is the point of it.</b> It used to assert the
    /// second arm ABSENT. With a connection holding the file, <c>ReadStoredConfiguration</c> answers
    /// null, so the session read the header a moment BEFORE opening - and a database that did not
    /// exist until that open had nothing to have read. The block was absent and said why.
    /// </para>
    /// <para>
    /// The phase-10 remainder's first item removed the workaround: an open database describes itself
    /// now (<c>WitDbConnection.StoredConfiguration</c>, from the header the paged store holds in
    /// memory), so it makes no difference whether the file existed a moment earlier. The two arms are
    /// kept because they are still two different routes into the session, and because the case that
    /// was hardest is now the one that reads the same as the easy one.
    /// </para>
    /// </summary>
    [Test]
    public async Task TheConfigurationIsThereForADatabaseThatExistedFirstAndForOneJustCreatedAsync()
    {
        m_fixture = await StudioFixture.CreateAsync(StudioStorage.BTree);

        var justCreated = await OpenAsync();

        // On disk FIRST and closed again, which is what the Open dialog always meets and what Studio's
        // own Create path produces - it builds the database and disposes it before connecting.
        StudioFixture.CreateDatabaseOnDisk(Path.Combine(m_fixture.Root, "already-there.witdb"));

        var session = await m_fixture.OpenAnotherAsync("already-there", StudioStorage.BTree,
            withSchema: false);
        var reopened = await m_fixture.Workspace.OpenDatabaseTabAsync(session);

        Assert.Multiple(() =>
        {
            Assert.That(justCreated.HasConfiguration, Is.True,
                "a database created by the open describes itself too - it is the open database that "
                + "answers now, not its file");
            Assert.That(justCreated.Overview!.PageSize, Is.Not.Null.And.GreaterThan(0));
            Assert.That(justCreated.HasFormat, Is.True);

            Assert.That(reopened.HasConfiguration, Is.True,
                "and a database that was on disk before the session opened it has all of it");
            Assert.That(reopened.Overview!.PageSize, Is.Not.Null.And.GreaterThan(0));
            Assert.That(reopened.Overview.PageCount, Is.Not.Null.And.GreaterThan(0));
            Assert.That(reopened.Overview.HasFileLocking, Is.True);
            Assert.That(reopened.HasFormat, Is.True);
            Assert.That(reopened.Format, Does.Contain("."));
        });
    }

    /// <summary>
    /// An LSM database says it is a folder, reports its SSTables and its memtable - and has NO format
    /// version, because it has no header. Absent rather than zero.
    /// </summary>
    [Test]
    public async Task AnLsmDatabaseIsDescribedAsAFolderAsync()
    {
        m_fixture = await StudioFixture.CreateAsync(StudioStorage.Lsm);

        var tab = await OpenAsync();

        Assert.Multiple(() =>
        {
            Assert.That(tab.Overview!.StoreProviderKey, Is.EqualTo("lsm"));
            Assert.That(tab.Overview.IsDirectory, Is.True);
            Assert.That(tab.Overview.PageSize, Is.Null, "a folder of SSTables has no page size");

            Assert.That(tab.HasFormat, Is.False,
                "and no format version either - there is no database header to read one from");

            Assert.That(tab.IsLsm, Is.True);
            Assert.That(tab.Overview.Lsm, Is.Not.Null);
            Assert.That(tab.Overview.Lsm!.MemTableLimitBytes, Is.GreaterThan(0));
        });
    }

    /// <summary>
    /// The store chain is the answer to "which store am I actually talking to", and no consumer could
    /// get it before WS-57. It is also the one fact on the tab that comes from the live connection
    /// rather than from the disk, so the two arms must differ in it.
    /// </summary>
    [TestCase(StudioStorage.BTree, "btree")]
    [TestCase(StudioStorage.Lsm, "lsm")]
    public async Task TheChainEndsInTheStoreThatIsActuallyThereAsync(StudioStorage storage, string expected)
    {
        m_fixture = await StudioFixture.CreateAsync(storage);

        var tab = await OpenAsync();

        Assert.Multiple(() =>
        {
            Assert.That(tab.Overview!.StoreChain, Is.Not.Empty);
            Assert.That(tab.Overview.StoreChain[^1], Is.EqualTo(expected),
                "the innermost layer is the store itself");
            Assert.That(tab.Chain, Does.Contain(expected));
        });
    }

    /// <summary>
    /// The schema block counts what the database holds, and the fixture's schema is what it is
    /// counting - a number pulled from nowhere would pass a "greater than zero" check.
    /// </summary>
    [Test]
    public async Task TheSchemaBlockCountsWhatIsThereAsync()
    {
        m_fixture = await StudioFixture.CreateAsync();

        var tab = await OpenAsync();

        Assert.Multiple(() =>
        {
            Assert.That(tab.Overview!.Schema.Tables, Is.EqualTo(4),
                "Customers, Orders, OrdersAudit and Logs");
            Assert.That(tab.Overview.Schema.Views, Is.EqualTo(1));
            Assert.That(tab.Overview.Schema.Triggers, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// While Studio holds a database, the path is locked against a second opener - which is the whole
    /// reason the block exists: it is the answer to "why will my own application not start".
    /// </summary>
    [Test]
    public async Task TheAccessBlockSaysTheDatabaseIsHeldHereAsync()
    {
        m_fixture = await StudioFixture.CreateAsync();

        var path = Path.Combine(m_fixture.Root, "held.witdb");

        // The CONTROL, and it has to be taken before the session opens it: the same path answers "not
        // in use" while nothing holds it, so "in use" below is a fact about this connection rather
        // than about a probe that says yes to everything.
        StudioFixture.CreateDatabaseOnDisk(path);

        Assert.That(WitDbConnection.IsDatabaseInUse(path), Is.False,
            "CONTROL: nothing is holding the database yet");

        var session = await m_fixture.OpenAnotherAsync("held", StudioStorage.BTree, withSchema: false);
        var tab = await m_fixture.Workspace.OpenDatabaseTabAsync(session);

        var localization = new LocalizationService();

        Assert.Multiple(() =>
        {
            Assert.That(tab.Overview!.HasFileLocking, Is.True,
                "locking is on for this database, which is what makes the sentence below true");
            Assert.That(tab.Overview.IsInUse, Is.True);
            Assert.That(tab.Access, Is.EqualTo(localization["Database.Now.HeldHere"]));
        });
    }

    #endregion

    #region Maintenance

    /// <summary>
    /// Compaction on a paged store is refused BY NAME - <c>NotSupported</c> and not "nothing to do" -
    /// and the tab says so. The distinction is what keeps the button off the screen (WS-55): nothing
    /// else can tell "there was nothing to merge" from "this store does not merge".
    /// </summary>
    [Test]
    public async Task CompactingAPagedStoreSaysTheStoreDoesNotHaveItAsync()
    {
        m_fixture = await StudioFixture.CreateAsync(StudioStorage.BTree);

        var result = await m_fixture.Database.CompactAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(WitDbMaintenanceOutcome.NotSupported));
            Assert.That(result.SstablesBefore, Is.Null);
        });
    }

    /// <summary>
    /// And on an LSM store it is a real operation with a measurable effect: a checkpoint turns the
    /// memtable into a file, so the count on disk goes UP.
    /// </summary>
    /// <remarks>
    /// Asserted on the SSTable count rather than on the outcome alone, because "Completed" is judged by
    /// exactly that count - and a case that only read the outcome would be reading the implementation's
    /// own opinion of itself.
    /// </remarks>
    [Test]
    public async Task ACheckpointOnAnLsmStoreTurnsTheMemTableIntoAFileAsync()
    {
        m_fixture = await StudioFixture.CreateAsync(StudioStorage.Lsm);

        var tab = await OpenAsync();

        var before = tab.Overview!.Lsm!.SstableCount;

        var result = await m_fixture.Database.CheckpointAsync();

        await tab.RefreshAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(WitDbMaintenanceOutcome.Completed));
            Assert.That(result.SstablesAfter, Is.GreaterThan(result.SstablesBefore ?? 0));
            Assert.That(tab.Overview!.Lsm!.SstableCount, Is.GreaterThan(before),
                "and the panel is reading the new number rather than the one it was built with");
        });
    }

    /// <summary>
    /// A second checkpoint with nothing written since says so, which is the answer a <c>void</c> could
    /// never give and the reason the whole surface returns a code.
    /// </summary>
    [Test]
    public async Task ASecondCheckpointSaysThereWasNothingToDoAsync()
    {
        m_fixture = await StudioFixture.CreateAsync(StudioStorage.Lsm);

        await m_fixture.Database.CheckpointAsync();

        var again = await m_fixture.Database.CheckpointAsync();

        Assert.That(again.Outcome, Is.EqualTo(WitDbMaintenanceOutcome.NothingToDo));
    }

    #endregion

    #region The matrix

    /// <summary>
    /// Every row of the provenance matrix has words in every language.
    /// </summary>
    /// <remarks>
    /// The rows carry KEYS, and a key that is not in the catalogue comes back as the key itself - so a
    /// row added without its text would put <c>Database.Cap.Whatever</c> on the screen and fail
    /// nothing. Same guard as the formatter's skip reasons, and for the same reason.
    /// </remarks>
    [Test]
    public void EveryCapabilityRowHasWordsInEveryLanguageTest()
    {
        var localization = new LocalizationService();

        Assert.Multiple(() =>
        {
            foreach (var language in localization.Available)
            {
                var texts = localization.Texts(language.Code);

                foreach (var capability in StorageCapabilities.Matrix)
                {
                    Assert.That(texts.ContainsKey(capability.OperationKey), Is.True,
                        $"{language.Code}: {capability.OperationKey}");
                    Assert.That(texts.ContainsKey(capability.SourceKey), Is.True,
                        $"{language.Code}: {capability.SourceKey}");

                    if (capability.NoteKey != null)
                    {
                        Assert.That(texts.ContainsKey(capability.NoteKey), Is.True,
                            $"{language.Code}: {capability.NoteKey}");
                    }
                }
            }
        });
    }

    /// <summary>
    /// And so does every node type the inspector describes, for the same reason and in the same way.
    /// </summary>
    /// <remarks>
    /// <c>ObjectInspectorViewModel.Describe</c> looks up <c>Node.&lt;type&gt;</c>, and the catalogue
    /// carried nine of the fourteen: selecting a folder in the tree put <c>Node.ViewsFolder</c> on the
    /// screen as its own name. Found by opening the tree while checking something else, which is the
    /// third time this week a key built from a name has failed by printing itself - so it is a rule
    /// now rather than a fix.
    /// </remarks>
    [Test]
    public void EveryNodeTypeHasWordsInEveryLanguageTest()
    {
        var localization = new LocalizationService();

        Assert.Multiple(() =>
        {
            foreach (var language in localization.Available)
            {
                var texts = localization.Texts(language.Code);

                foreach (var type in Enum.GetValues<DatabaseNodeType>())
                {
                    Assert.That(texts.ContainsKey($"Node.{type}"), Is.True,
                        $"{language.Code} has no words for a {type} node");
                }
            }
        });
    }

    /// <summary>
    /// <b>What the page cache is HOLDING, which nothing above the page manager could see until
    /// 2026-08-09.</b> Both caches have kept <c>Count</c> and <c>DirtyCount</c> since they were
    /// written and neither handed them out, so the only thing the tab could say about the cache was
    /// the size it was configured with - the single "needs provider access" row of the matrix.
    ///
    /// <para>
    /// Asserted as NUMBERS rather than as a line of text, and with a control that moves them: a
    /// property answering a constant would satisfy "there is an occupancy" perfectly well.
    /// </para>
    /// <para>
    /// <b>The first version of that control could not fail, and the shape is a familiar one:</b> it
    /// scanned the fixture's three tables and compared, and the whole fixture database is FIVE pages -
    /// they were all in the cache already, so 5 was measured against 5. The workload has to be big
    /// enough to allocate pages that were not there before.
    /// </para>
    /// </summary>
    [Test]
    public async Task ThePageCacheSaysHowManyPagesItIsHoldingAsync()
    {
        m_fixture = await StudioFixture.CreateAsync(StudioStorage.BTree);

        var before = m_fixture.Database.CacheOccupancy;

        Assert.That(before, Is.Not.Null, "a paged database has a page cache to ask");

        await m_fixture.Database.ExecuteNonQueryAsync(
            "CREATE TABLE Bulk (Id INTEGER PRIMARY KEY, Padding VARCHAR(200))");

        for (var i = 1; i <= 300; i++)
        {
            await m_fixture.Database.ExecuteNonQueryAsync(
                $"INSERT INTO Bulk (Id, Padding) VALUES ({i}, '{new string('x', 180)}')");
        }

        var after = m_fixture.Database.CacheOccupancy;

        var tab = await OpenAsync();

        Assert.Multiple(() =>
        {
            Assert.That(after!.Value.ProviderKey, Is.Not.Empty, "and it says which cache answered");
            Assert.That(after.Value.Pages, Is.GreaterThan(before!.Value.Pages),
                "CONTROL: three hundred rows allocate pages, so this is a reading and not a constant");
            Assert.That(after.Value.DirtyPages, Is.LessThanOrEqualTo(after.Value.Pages),
                "dirty pages are a subset of the pages held");

            Assert.That(tab.Overview!.CachePagesHeld, Is.Not.Null.And.GreaterThan(0),
                "and the tab carries it");
        });
    }

    /// <summary>
    /// The other arm, and it is the reason the property is nullable: an LSM database is not paged, so
    /// there is no page cache to ask and the tab says the configured size alone rather than inventing
    /// a zero.
    /// </summary>
    [Test]
    public async Task AnLsmDatabaseHasNoPageCacheToAskAsync()
    {
        m_fixture = await StudioFixture.CreateAsync(StudioStorage.Lsm);

        var tab = await OpenAsync();

        Assert.Multiple(() =>
        {
            Assert.That(m_fixture.Database.CacheOccupancy, Is.Null);
            Assert.That(tab.Overview!.CachePagesHeld, Is.Null);
        });
    }

    /// <summary>
    /// The matrix carries all three categories, and it has to: the third one - what the engine simply
    /// does not have - is the reason the matrix exists rather than a list of buttons.
    /// </summary>
    [Test]
    public void TheMatrixNamesWhatTheEngineDoesNotHaveTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var availability in new[] { StorageAvailability.Available, StorageAvailability.NotInEngine })
            {
                Assert.That(StorageCapabilities.Matrix.Any(row => row.Availability == availability),
                    Is.True, $"nothing in the matrix is {availability}");
            }

            // Measured 2026-08-08 and worth pinning, because it is the row the design left open: the
            // paged caches count no hits and no misses, so a hit rate is absent from the engine rather
            // than merely unreachable through the provider.
            var hitRate = StorageCapabilities.Matrix
                .Single(row => row.OperationKey == "Database.Cap.CacheHitRate");

            Assert.That(hitRate.Availability, Is.EqualTo(StorageAvailability.NotInEngine));

            // And its neighbour, which is the OTHER half of that measurement and moved on 2026-08-09:
            // occupancy WAS in the engine and unpublished, so it was the one row saying "needs provider
            // access". It is Available now, and no row is in that state - asserted rather than left to
            // be noticed, because a state nothing is in reads exactly like a state nobody maintains.
            var occupancy = StorageCapabilities.Matrix
                .Single(row => row.OperationKey == "Database.Cap.CacheOccupancy");

            Assert.That(occupancy.Availability, Is.EqualTo(StorageAvailability.Available));
            Assert.That(StorageCapabilities.Matrix.Where(
                    row => row.Availability == StorageAvailability.NeedsProviderAccess),
                Is.Empty,
                "the last of these was closed on 2026-08-09; a new one has to be a decision, not a drift");
        });
    }

    #endregion

    #region Tools

    private async Task<DatabaseTabViewModel> OpenAsync()
    {
        return await m_fixture.Workspace.OpenDatabaseTabAsync(m_fixture.Database);
    }

    #endregion
}

/// <summary>
/// The `Page cache` line, over the four arrangements a header can produce.
/// </summary>
/// <remarks>
/// <para>
/// Its own fixture, and over the composing function rather than over a connection, because the arm
/// that matters cannot be built from a database: <b>every file this repository can write records the
/// cache kind</b>, so a case driving a real connection sees only the arm that was already right. The
/// blank came from <c>demo.witdb</c>, which predates the field.
/// </para>
/// <para>
/// This is the "drop a layer and assert the guarantee itself" move: the running application is where
/// the defect was seen, and the function is the only place where both answers exist.
/// </para>
/// </remarks>
[TestFixture]
public class DatabaseCacheLineTests
{
    [Test]
    public void AnAbsentCacheKindTakesItsSeparatorWithItTest()
    {
        var localization = new LocalizationService("en");

        // The four arrangements: kind or no kind, times an occupancy reading or none. The numbers are
        // the ones actually driven on 2026-08-15 - "clock · 1000 pages · holding 2 pages, 0 dirty" on a
        // database written that day, and 170 pages held on demo.witdb, whose header carries no kind.
        var known = DatabaseTabViewModel.CacheLine(localization, "clock", 1000, 2, 0);
        var unknown = DatabaseTabViewModel.CacheLine(localization, "", 0, 170, 0);
        var sizedKnown = DatabaseTabViewModel.CacheLine(localization, "clock", 1000, null, null);
        var sizedUnknown = DatabaseTabViewModel.CacheLine(localization, "", 0, null, null);

        localization.SetLanguage("ru");

        var russian = DatabaseTabViewModel.CacheLine(localization, "", 0, 170, 0);

        TestContext.Out.WriteLine(string.Join("\n", known, unknown, sizedKnown, sizedUnknown, russian));

        Assert.Multiple(() =>
        {
            // CONTROL: the arm that was already correct still prints the kind. Without it, "return the
            // size and nothing else" would pass every assertion below.
            Assert.That(known, Is.EqualTo("clock · 1000 pages · holding 2 pages, 0 dirty"));
            Assert.That(sizedKnown, Is.EqualTo("clock · 1000 pages"));

            Assert.That(unknown, Is.EqualTo("0 pages · holding 170 pages, 0 dirty"));
            Assert.That(sizedUnknown, Is.EqualTo("0 pages"));

            Assert.That(russian, Is.EqualTo("0 страниц · занято 170 страниц, из них грязных 0"),
                "and the same in the other language, where the separator is in the same place");

            // The rule over all of them rather than over the one that was reported: no line may open
            // with a separator, whatever is missing from the header.
            foreach (var line in new[] { known, unknown, sizedKnown, sizedUnknown, russian })
            {
                Assert.That(line.TrimStart(), Does.Not.StartWith("·"),
                    "a field that is absent is dropped, not printed as an empty slot");
                Assert.That(line, Does.Not.Contain("· ·"),
                    "and two separators in a row is the same defect one field along");
            }
        });
    }
}
