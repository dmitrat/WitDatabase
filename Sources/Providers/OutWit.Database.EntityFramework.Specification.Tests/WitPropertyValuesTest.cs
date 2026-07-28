using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using OutWit.Database.EntityFramework.Specification.Tests.TestUtilities;
using OutWit.Database.EntityFramework.Specification.Tests.TestUtilities.Oracle;

namespace OutWit.Database.EntityFramework.Specification.Tests;

/// <summary>
/// CurrentValues, OriginalValues and GetDatabaseValues across owned types, inheritance and shadow state.
/// Paired with its oracle so that a red run can be attributed before it is reported.
/// </summary>
[Trait("Category", "Conformance")]
public class WitPropertyValuesTest(WitPropertyValuesTest.WitFixture fixture)
    : PropertyValuesTestBase<WitPropertyValuesTest.WitFixture>(fixture)
{
    public class WitFixture : PropertyValuesFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => WitTestStoreFactory.Instance;
    }
}

/// <summary>
/// The same suite on SQLite. See <see cref="SqliteTestStore"/> for why it exists.
/// </summary>
[Trait("Category", "Oracle")]
public class OraclePropertyValuesTest(OraclePropertyValuesTest.OracleFixture fixture)
    : PropertyValuesTestBase<OraclePropertyValuesTest.OracleFixture>(fixture)
{
    public class OracleFixture : PropertyValuesFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqliteTestStoreFactory.Instance;
    }
}
