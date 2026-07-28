using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Specification.Tests.TestUtilities;

/// <summary>
/// The hook the specification fixtures use to obtain a WitDatabase store.
/// </summary>
public class WitTestStoreFactory : RelationalTestStoreFactory
{
    public static WitTestStoreFactory Instance { get; } = new();

    protected WitTestStoreFactory()
    {
    }

    public override TestStore Create(string storeName)
        => WitTestStore.Create(storeName);

    public override TestStore GetOrCreate(string storeName)
        => WitTestStore.GetOrCreate(storeName);

    public override IServiceCollection AddProviderServices(IServiceCollection serviceCollection)
        => serviceCollection.AddEntityFrameworkWitDb();
}
