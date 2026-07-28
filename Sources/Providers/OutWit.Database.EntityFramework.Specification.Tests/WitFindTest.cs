using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using OutWit.Database.EntityFramework.Specification.Tests.TestUtilities;

namespace OutWit.Database.EntityFramework.Specification.Tests;

/// <summary>
/// The first conformance suite wired to WitDatabase. Chosen to prove the harness end to end rather
/// than for its subject: it builds a model with int, nullable, string, composite, shadow and
/// inherited keys, creates the schema, writes rows and reads them back through three different
/// entry points - so a green run means the store, the fixture and the SQL path all work, and a red
/// one is about WitDatabase rather than about plumbing.
/// </summary>
public abstract class WitFindTest(WitFindTest.WitFindFixture fixture) : FindTestBase<WitFindTest.WitFindFixture>(fixture)
{
    [Trait("Category", "Conformance")]
    public class WitFindViaSetTest(WitFindFixture fixture) : WitFindTest(fixture)
    {
        protected override TestFinder Finder { get; } = new FindViaSetFinder();
    }

    [Trait("Category", "Conformance")]
    public class WitFindViaContextTest(WitFindFixture fixture) : WitFindTest(fixture)
    {
        protected override TestFinder Finder { get; } = new FindViaContextFinder();
    }

    [Trait("Category", "Conformance")]
    public class WitFindViaNonGenericContextTest(WitFindFixture fixture) : WitFindTest(fixture)
    {
        protected override TestFinder Finder { get; } = new FindViaNonGenericContextFinder();
    }

    public class WitFindFixture : FindFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => WitTestStoreFactory.Instance;
    }
}
