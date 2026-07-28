using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using OutWit.Database.EntityFramework.Specification.Tests.TestUtilities;
using OutWit.Database.EntityFramework.Specification.Tests.TestUtilities.Oracle;

namespace OutWit.Database.EntityFramework.Specification.Tests;

/// <summary>
/// Entities materialised through constructors rather than property setters, including injected services.
/// Paired with its oracle so that a red run can be attributed before it is reported.
/// </summary>
[Trait("Category", "Conformance")]
public class WitWithConstructorsTest(WitWithConstructorsTest.WitFixture fixture)
    : WithConstructorsTestBase<WitWithConstructorsTest.WitFixture>(fixture)
{
    public class WitFixture : WithConstructorsFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => WitTestStoreFactory.Instance;
    }
}

/// <summary>
/// The same suite on SQLite. See <see cref="SqliteTestStore"/> for why it exists.
/// </summary>
[Trait("Category", "Oracle")]
public class OracleWithConstructorsTest(OracleWithConstructorsTest.OracleFixture fixture)
    : WithConstructorsTestBase<OracleWithConstructorsTest.OracleFixture>(fixture)
{
    public class OracleFixture : WithConstructorsFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqliteTestStoreFactory.Instance;
    }
}
