using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Database.AdoNet;
using OutWit.Database.EntityFramework.Diagnostics;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Specification.Tests.TestUtilities;

/// <summary>
/// Tells the specification suite how to build a service provider and options for WitDatabase.
/// Model-level tests use it without ever opening a connection, so the options it hands out point at
/// an in-memory store.
/// </summary>
public class WitTestHelpers : RelationalTestHelpers
{
    protected WitTestHelpers()
    {
    }

    public static WitTestHelpers Instance { get; } = new();

    public override IServiceCollection AddProviderServices(IServiceCollection services)
        => services.AddEntityFrameworkWitDb();

    public override DbContextOptionsBuilder UseProviderOptions(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseWitDb(new WitDbConnection("Data Source=:memory:"));

    public override LoggingDefinitions LoggingDefinitions { get; } = new WitLoggingDefinitions();
}
