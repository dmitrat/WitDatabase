using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using OutWit.Database.EntityFramework.Specification.Tests.TestUtilities;
using OutWit.Database.EntityFramework.Specification.Tests.TestUtilities.Oracle;

namespace OutWit.Database.EntityFramework.Specification.Tests;

/// <summary>
/// Explicit loading of references and collections, and the queries it issues.
/// Passes outright - 3,137 tests, all green on both providers - so it is not tagged Category=Conformance and runs in
/// CI, guarding what it covers. Its oracle stays for the day it stops being green.
/// </summary>
public class WitLoadTest(WitLoadTest.WitFixture fixture)
    : LoadTestBase<WitLoadTest.WitFixture>(fixture)
{
    public class WitFixture : LoadFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => WitTestStoreFactory.Instance;
    }
}

/// <summary>
/// The same suite on SQLite. See <see cref="SqliteTestStore"/> for why it exists.
/// </summary>
[Trait("Category", "Oracle")]
public class OracleLoadTest(OracleLoadTest.OracleFixture fixture)
    : LoadTestBase<OracleLoadTest.OracleFixture>(fixture)
{
    public class OracleFixture : LoadFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqliteTestStoreFactory.Instance;
    }
}
