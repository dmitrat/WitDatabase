using System.Runtime.CompilerServices;
using Microsoft.JSInterop;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Providers;

namespace OutWit.Database.Core.IndexedDb;

/// <summary>
/// Registers IndexedDB providers with the ProviderRegistry.
/// Called automatically via ModuleInitializer when the assembly is loaded.
/// </summary>
public static class IndexedDbProviderRegistration
{
    #region Fields

    private static bool s_initialized;
    private static readonly Lock s_lock = new();

    #endregion

    #region Initialize

    /// <summary>
    /// Registers IndexedDB providers. Safe to call multiple times.
    /// Called automatically via ModuleInitializer, but can be called explicitly
    /// to ensure registration before using providers.
    /// </summary>
    [ModuleInitializer]
    public static void Initialize()
    {
        if (s_initialized) return;

        lock (s_lock)
        {
            if (s_initialized) return;

            RegisterStorageProviders();

            s_initialized = true;
        }
    }

    /// <summary>
    /// Ensures IndexedDB providers are registered.
    /// Alias for Initialize() for more explicit API.
    /// </summary>
    public static void EnsureRegistered() => Initialize();

    #endregion

    #region Registration

    private static void RegisterStorageProviders()
    {
        // Register IndexedDB storage provider
        ProviderRegistry.Instance.RegisterOrReplace<IStorage>(StorageIndexedDb.PROVIDER_KEY, p =>
        {
            var databaseName = p.GetRequired<string>("databaseName");
            var jsRuntime = p.GetRequired<IJSRuntime>("jsRuntime");
            var pageSize = p.Get("pageSize", DatabaseConstants.DEFAULT_PAGE_SIZE);
            
            return new StorageIndexedDb(databaseName, jsRuntime, pageSize);
        });
    }

    #endregion
}
