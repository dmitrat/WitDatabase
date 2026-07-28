using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using OutWit.Database.EntityFramework.Specification.Tests.TestUtilities;
using OutWit.Database.EntityFramework.Specification.Tests.TestUtilities.Oracle;

namespace OutWit.Database.EntityFramework.Specification.Tests;

/// <summary>
/// Properties the store is expected to fill: defaults, computed columns and generated keys.
/// Paired with its oracle so that a red run can be attributed before it is reported.
/// </summary>
[Trait("Category", "Conformance")]
public class WitStoreGeneratedTest(WitStoreGeneratedTest.WitFixture fixture)
    : StoreGeneratedTestBase<WitStoreGeneratedTest.WitFixture>(fixture)
{
    public class WitFixture : StoreGeneratedFixtureBase
    {
        // This fixture base names no store of its own, unlike the others.
        protected override string StoreName => "WitStoreGeneratedTest";

        protected override ITestStoreFactory TestStoreFactory => WitTestStoreFactory.Instance;
    }
}

/// <summary>
/// The same suite on SQLite. See <see cref="SqliteTestStore"/> for why it exists.
/// </summary>
[Trait("Category", "Oracle")]
public class OracleStoreGeneratedTest(OracleStoreGeneratedTest.OracleFixture fixture)
    : StoreGeneratedTestBase<OracleStoreGeneratedTest.OracleFixture>(fixture)
{
    public class OracleFixture : StoreGeneratedFixtureBase
    {
        protected override string StoreName => "OracleStoreGeneratedTest";

        protected override ITestStoreFactory TestStoreFactory => SqliteTestStoreFactory.Instance;
    }
}
