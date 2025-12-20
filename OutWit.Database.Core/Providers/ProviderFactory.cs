using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Providers;

/// <summary>
/// Typed provider factory that wraps ProviderRegistry.
/// </summary>
/// <typeparam name="T">The provider interface type.</typeparam>
public sealed class ProviderFactory<T> : IProviderFactory<T> where T : IProvider
{
    #region Singleton

    private static readonly Lazy<ProviderFactory<T>> INSTANCE = new(() => new ProviderFactory<T>());

    /// <summary>
    /// Gets the factory instance for this provider type.
    /// </summary>
    public static ProviderFactory<T> Instance => INSTANCE.Value;

    #endregion

    #region IProviderFactory

    /// <inheritdoc/>
    public T Create(string key, ProviderParameters parameters)
    {
        return ProviderRegistry.Instance.Create<T>(key, parameters);
    }

    /// <inheritdoc/>
    public bool IsRegistered(string key)
    {
        return ProviderRegistry.Instance.IsRegistered<T>(key);
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> RegisteredKeys => ProviderRegistry.Instance.GetRegisteredKeys<T>();

    #endregion
}