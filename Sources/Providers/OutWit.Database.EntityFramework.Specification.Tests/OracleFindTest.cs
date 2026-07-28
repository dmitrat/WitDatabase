using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using OutWit.Database.EntityFramework.Specification.Tests.TestUtilities.Oracle;

namespace OutWit.Database.EntityFramework.Specification.Tests;

/// <summary>
/// <see cref="WitFindTest"/> run against SQLite instead of WitDatabase.
///
/// This is the control. Whatever it does is what the suite asks of any file-backed provider, so a
/// test red here and red on WitDatabase is not a WitDatabase defect - and that distinction is not
/// available from the WitDatabase run alone.
/// </summary>
public abstract class OracleFindTest(OracleFindTest.OracleFindFixture fixture)
    : FindTestBase<OracleFindTest.OracleFindFixture>(fixture)
{
    [Trait("Category", "Oracle")]
    public class OracleFindViaSetTest(OracleFindFixture fixture) : OracleFindTest(fixture)
    {
        protected override TestFinder Finder { get; } = new FindViaSetFinder();
    }

    public class OracleFindFixture : FindFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqliteTestStoreFactory.Instance;
    }
}
