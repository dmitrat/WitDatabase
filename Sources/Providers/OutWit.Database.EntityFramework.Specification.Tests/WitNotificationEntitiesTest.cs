using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using OutWit.Database.EntityFramework.Specification.Tests.TestUtilities;
using OutWit.Database.EntityFramework.Specification.Tests.TestUtilities.Oracle;

namespace OutWit.Database.EntityFramework.Specification.Tests;

/// <summary>
/// Entities that raise change notifications, tracked with ChangingAndChangedNotifications.
///
/// The first EF Core conformance suite WitDatabase passes outright, so it is deliberately NOT tagged
/// Category=Conformance: it runs in CI and guards what it covers. A suite earns that the moment it
/// is green, and loses it again if it ever stops being.
/// </summary>
public class WitNotificationEntitiesTest(WitNotificationEntitiesTest.WitFixture fixture)
    : NotificationEntitiesTestBase<WitNotificationEntitiesTest.WitFixture>(fixture)
{
    public class WitFixture : NotificationEntitiesFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => WitTestStoreFactory.Instance;
    }
}

/// <summary>
/// The same suite on SQLite. See <see cref="SqliteTestStore"/> for why it exists.
/// </summary>
[Trait("Category", "Oracle")]
public class OracleNotificationEntitiesTest(OracleNotificationEntitiesTest.OracleFixture fixture)
    : NotificationEntitiesTestBase<OracleNotificationEntitiesTest.OracleFixture>(fixture)
{
    public class OracleFixture : NotificationEntitiesFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory => SqliteTestStoreFactory.Instance;
    }
}
