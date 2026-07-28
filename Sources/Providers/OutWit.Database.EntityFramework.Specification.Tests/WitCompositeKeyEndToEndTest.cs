using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using OutWit.Database.EntityFramework.Specification.Tests.TestUtilities;
using OutWit.Database.EntityFramework.Specification.Tests.TestUtilities.Oracle;

namespace OutWit.Database.EntityFramework.Specification.Tests;

/// <summary>
/// Round trips entities keyed on two and three columns, including generated and binary keys.
/// Paired with its oracle so that a red run can be attributed before it is reported.
/// </summary>
[Trait("Category", "Conformance")]
public class WitCompositeKeyEndToEndTest(WitCompositeKeyEndToEndTest.WitFixture fixture)
    : CompositeKeyEndToEndTestBase<WitCompositeKeyEndToEndTest.WitFixture>(fixture)
{
    public class WitFixture : CompositeKeyEndToEndFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => WitTestStoreFactory.Instance;
    }
}

/// <summary>
/// The same suite on SQLite. See <see cref="SqliteTestStore"/> for why it exists.
/// </summary>
[Trait("Category", "Oracle")]
public class OracleCompositeKeyEndToEndTest(OracleCompositeKeyEndToEndTest.OracleFixture fixture)
    : CompositeKeyEndToEndTestBase<OracleCompositeKeyEndToEndTest.OracleFixture>(fixture)
{
    public class OracleFixture : CompositeKeyEndToEndFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqliteTestStoreFactory.Instance;
    }
}
