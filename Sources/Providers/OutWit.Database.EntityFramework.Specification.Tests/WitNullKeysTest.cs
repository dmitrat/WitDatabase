using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using OutWit.Database.EntityFramework.Specification.Tests.TestUtilities;
using OutWit.Database.EntityFramework.Specification.Tests.TestUtilities.Oracle;

namespace OutWit.Database.EntityFramework.Specification.Tests;

/// <summary>
/// Entities whose foreign keys are null, and the navigations that must not materialise.
/// Passes outright - 5/5 on both providers - so it is not tagged Category=Conformance and runs in
/// CI, guarding what it covers. Its oracle stays for the day it stops being green.
/// </summary>
public class WitNullKeysTest(WitNullKeysTest.WitFixture fixture)
    : NullKeysTestBase<WitNullKeysTest.WitFixture>(fixture)
{
    public class WitFixture : NullKeysFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => WitTestStoreFactory.Instance;
    }
}

/// <summary>
/// The same suite on SQLite. See <see cref="SqliteTestStore"/> for why it exists.
/// </summary>
[Trait("Category", "Oracle")]
public class OracleNullKeysTest(OracleNullKeysTest.OracleFixture fixture)
    : NullKeysTestBase<OracleNullKeysTest.OracleFixture>(fixture)
{
    public class OracleFixture : NullKeysFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqliteTestStoreFactory.Instance;
    }
}
