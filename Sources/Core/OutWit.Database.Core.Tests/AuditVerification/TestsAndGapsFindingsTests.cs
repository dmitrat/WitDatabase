using NUnit.Framework;

namespace OutWit.Database.Core.Tests.AuditVerification;

/// <summary>
/// Verification of the <c>tests-and-gaps</c> findings of the 2026-07 audit.
/// </summary>
/// <remarks>
/// This dimension is unlike every other one: the claims are about the <b>test suite and the build</b>,
/// not about what the engine does. They are settled by measuring the repository, so the tests here
/// assert repository properties - and, following the same convention as the rest of the harness,
/// they assert the <i>desired</i> state, so a failure confirms the gap and the test turns green the
/// day it is closed.
///
/// See Docs/NEXT-SESSION-PLAN.md workstream B.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class TestsAndGapsFindingsTests
{
    #region Coverage and mutation testing

    [Test]
    public void CiCollectsCodeCoverageTest()
    {
        // FIXED. coverlet.collector was referenced by all seven test projects and never invoked.
        // CI now collects it and summarises the result; the number is reported, not gated, because
        // a threshold on a suite whose assertions are known to miss defects would measure the wrong
        // thing. tests-and-gaps, .github/workflows/ci.yml
        var workflow = ReadRepositoryFile(".github/workflows/ci.yml");

        Assert.That(workflow, Does.Contain("collect").IgnoreCase,
            "coverlet.collector is referenced by all seven test projects but never invoked");
    }

    [Test]
    public void MutationTestingIsWiredUpTest()
    {
        // FIXED, though not where the finding pointed. It named ci.yml, but a Stryker run rebuilds
        // and re-tests once per mutant and is far too slow to block a pull request - so it lives in
        // its own workflow, dispatchable per project and scheduled weekly. The assertion therefore
        // scans the whole workflows directory rather than ci.yml alone, because the claim was "no
        // mutation testing", not "none in ci.yml".
        //
        // This is the gap that matters most in this dimension: nine behaviours changed during the
        // 2026-07 audit without a single test failing, and a surviving mutant is that same signal
        // caught before the defect ships.
        var root = FindRepositoryRoot();
        var workflows = Directory
            .GetFiles(Path.Combine(root, ".github", "workflows"), "*.yml")
            .Select(File.ReadAllText)
            .ToList();

        Assert.That(workflows, Has.Some.Contains("stryker").IgnoreCase,
            "nothing checks whether the suite's assertions would notice a changed behaviour");
    }

    #endregion

    #region EF Core's provider specification suite

    [Test]
    [Ignore("CONFIRMED 2026-07-27: the csproj references Microsoft.EntityFrameworkCore.Sqlite (both TFMs) "
            + "and NOT Specification.Tests - so the unused SQLite dependency that would make a "
            + "differential oracle nearly free is already present, while the conformance suite that "
            + "decides the drop-in claim is absent. This is the highest-value entry in the whole "
            + "backlog. tests-and-gaps, EntityFramework.Tests.csproj:24")]
    public void EfCoreSpecificationSuiteIsReferencedTest()
    {
        // Finding: the canonical proof of "drop-in" is absent. This dimension's most valuable entry:
        // the audit expects it would surface more than the whole of workstream B.
        var project = ReadRepositoryFile(
            "Sources/Providers/OutWit.Database.EntityFramework.Tests/OutWit.Database.EntityFramework.Tests.csproj");

        Assert.That(project, Does.Contain("Microsoft.EntityFrameworkCore.Specification.Tests"),
            "EF Core ships the conformance suite that decides whether a provider is really drop-in");
    }

    #endregion

    #region Corruption and fuzzing

    [Test]
    [Ignore("CONFIRMED 2026-07-27: `bytes[25] ^= 0xFF` sits inside `if (bytes.Length > 30)`, so a shorter "
            + "file skips the mutation and the test passes having verified nothing. "
            + "tests-and-gaps, Core.Tests/Wal/WriteAheadLogTests.cs:284")]
    public void WalCorruptionTestMutatesUnconditionallyTest()
    {
        // Finding: WriteAheadLogTests.cs:284 - the single corruption test flips one hard-coded byte
        // behind an `if`, so a shorter file silently skips the mutation and the test still passes
        // having verified nothing.
        var test = ReadRepositoryFile(
            "Sources/Core/OutWit.Database.Core.Tests/Wal/WriteAheadLogTests.cs");

        Assert.That(test, Does.Not.Contain("bytes[25] ^= 0xFF"),
            "corruption coverage must not depend on one hard-coded offset inside a conditional");
    }

    #endregion

    #region Literal round trip

    [Test]
    [Ignore("CONFIRMED 2026-07-27, and measured: 6 of the 10 LiteralType members are never mentioned by the "
            + "serializer tests - Real, Blob, CurrentTimestamp, CurrentDate, CurrentTime, Decimal. "
            + "The audit said \"2 of 9\"; the enum has since gained Decimal (commit 9556bd2), and 4 "
            + "members are exercised rather than 2. Same gap, different arithmetic. "
            + "tests-and-gaps, Parser.Tests/SerializerTests.cs:236")]
    public void EveryLiteralTypeIsRoundTrippedTest()
    {
        // Finding: SerializerTests.cs:236 - most LiteralType values are never round-tripped.
        // Measured rather than assumed: the enum has 10 members and the serializer tests mention 4.
        var declared = ReadRepositoryFile(
            "Sources/Engine/OutWit.Database.Parser/Schema/Types/LiteralType.cs");
        var tests = ReadRepositoryFile(
            "Sources/Engine/OutWit.Database.Parser.Tests/SerializerTests.cs");

        var members = new[]
        {
            "Null", "Integer", "Real", "String", "Blob",
            "Boolean", "CurrentTimestamp", "CurrentDate", "CurrentTime", "Decimal"
        };
        Assert.That(members.Count(m => declared.Contains($"        {m},") || declared.Contains($"        {m}\r") || declared.Contains($"        {m}\n")),
            Is.GreaterThan(0), "sanity: the enum members should be readable");

        var missing = members.Where(m => !tests.Contains($"LiteralType.{m}")).ToList();

        Assert.That(missing, Is.Empty,
            $"these literal types are never exercised by the serializer tests: {string.Join(", ", missing)}");
    }

    #endregion

    #region Helpers

    private static string ReadRepositoryFile(string relativePath)
    {
        var path = Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.That(File.Exists(path), Is.True, $"expected to find {relativePath}");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
            directory = directory.Parent;

        Assert.That(directory, Is.Not.Null, "could not locate the repository root");
        return directory!.FullName;
    }

    #endregion

    #region Claims settled by measurement, with two corrections

    // CONFIRMED - "StatementExecutor tests mock IDatabase and assert Received(n), so
    // read-your-own-writes defects are structurally invisible"
    // (Engine/OutWit.Database.Tests/Statements/StatementExecutorUpdateTests.cs:418). Seven
    // occurrences of Substitute.For<IDatabase> / Received( in that one file. This is the finding
    // that explains the others: a suite asserting call counts against a mock cannot notice a wrong
    // value, which is exactly the class of defect this whole verification pass kept confirming.
    //
    // PARTLY WRONG - "the sync Database.Migrate() path has ZERO coverage"
    // (MigrateAsyncIntegrationTests.cs:54). The sync path IS covered: `context.Database.Migrate()`
    // appears twice, in MigrationTests/SchemaEvolutionRegressionTests.cs (lines 58 and 80). The
    // second half of the claim holds - nothing round-trips `dotnet ef migrations add`, because no
    // real `Migration` subclass exists anywhere in the test projects.
    //
    // PARTLY WRONG - "the LSM reference-model oracle ... has WAL off"
    // (LsmTreeStressTests.cs:428). WAL is ENABLED in two of the four stress configurations
    // ("WAL+Cache+SyncCompact" and "WAL+Cache+BgCompact"); only "NoWAL+Cache" turns it off. The rest
    // of the claim holds and was measured: the seed is fixed (`new Random(42)`) and verification
    // covers `expected.Take(1000)` keys.

    #endregion
}
