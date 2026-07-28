using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using OutWit.Database.EntityFramework.Specification.Tests.TestUtilities;
using OutWit.Database.EntityFramework.Specification.Tests.TestUtilities.Oracle;

namespace OutWit.Database.EntityFramework.Specification.Tests;

/// <summary>
/// Entities mapped to backing fields instead of properties, in every access mode EF offers.
/// Paired with its oracle so that a red run can be attributed before it is reported.
/// </summary>
[Trait("Category", "Conformance")]
public class WitFieldMappingTest(WitFieldMappingTest.WitFixture fixture)
    : FieldMappingTestBase<WitFieldMappingTest.WitFixture>(fixture)
{
    public class WitFixture : FieldMappingFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => WitTestStoreFactory.Instance;
    }
}

/// <summary>
/// The same suite on SQLite. See <see cref="SqliteTestStore"/> for why it exists.
/// </summary>
[Trait("Category", "Oracle")]
public class OracleFieldMappingTest(OracleFieldMappingTest.OracleFixture fixture)
    : FieldMappingTestBase<OracleFieldMappingTest.OracleFixture>(fixture)
{
    public class OracleFixture : FieldMappingFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqliteTestStoreFactory.Instance;
    }
}
