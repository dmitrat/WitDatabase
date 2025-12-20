using System.Collections.Concurrent;
using OutWit.Database.Core.Exceptions;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Providers;

/// <summary>
/// Central registry for provider factories.
/// Allows providers to register themselves and be created by key.
/// Thread-safe singleton.
/// </summary>
public sealed class ProviderRegistry
{
    #region Singleton

    private static readonly Lazy<ProviderRegistry> INSTANCE = new(() => new ProviderRegistry());

    /// <summary>
    /// Gets the global provider registry instance.
    /// </summary>
    public static ProviderRegistry Instance => INSTANCE.Value;

    #endregion

    #region Fields

    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, Delegate>> m_factories = new();
    private readonly Lock m_lock = new();

    #endregion

    #region Constructors

    private ProviderRegistry()
    {
        // Register built-in providers
        RegisterBuiltInProviders();
    }

    #endregion

    #region Register

    /// <summary>
    /// Registers a provider factory.
    /// </summary>
    /// <typeparam name="T">The provider interface type.</typeparam>
    /// <param name="key">The provider key.</param>
    /// <param name="factory">Factory function to create provider instances.</param>
    /// <exception cref="ArgumentException">Thrown if key is already registered.</exception>
    public void Register<T>(string key, Func<ProviderParameters, T> factory) where T : IProvider
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        var typeFactories = m_factories.GetOrAdd(typeof(T), _ => new ConcurrentDictionary<string, Delegate>(StringComparer.OrdinalIgnoreCase));
        
        if (!typeFactories.TryAdd(key, factory))
        {
            throw new ArgumentException($"Provider '{key}' is already registered for type {typeof(T).Name}", nameof(key));
        }
    }

    /// <summary>
    /// Registers a provider factory, replacing any existing registration.
    /// </summary>
    public void RegisterOrReplace<T>(string key, Func<ProviderParameters, T> factory) where T : IProvider
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        var typeFactories = m_factories.GetOrAdd(typeof(T), _ => new ConcurrentDictionary<string, Delegate>(StringComparer.OrdinalIgnoreCase));
        typeFactories[key] = factory;
    }

    /// <summary>
    /// Unregisters a provider factory.
    /// </summary>
    /// <returns>True if the factory was removed, false if it wasn't registered.</returns>
    public bool Unregister<T>(string key) where T : IProvider
    {
        if (m_factories.TryGetValue(typeof(T), out var typeFactories))
        {
            return typeFactories.TryRemove(key, out _);
        }
        return false;
    }

    #endregion

    #region Create

    /// <summary>
    /// Creates a provider instance using the registered factory.
    /// </summary>
    /// <typeparam name="T">The provider interface type.</typeparam>
    /// <param name="key">The provider key.</param>
    /// <param name="parameters">Provider-specific parameters.</param>
    /// <returns>A new provider instance.</returns>
    /// <exception cref="ProviderNotFoundException">Thrown when no factory is registered.</exception>
    public T Create<T>(string key, ProviderParameters? parameters = null) where T : IProvider
    {
        parameters ??= new ProviderParameters();

        if (!m_factories.TryGetValue(typeof(T), out var typeFactories) ||
            !typeFactories.TryGetValue(key, out var factory))
        {
            var available = GetRegisteredKeys<T>();
            throw new ProviderNotFoundException(key, typeof(T), available);
        }

        return ((Func<ProviderParameters, T>)factory)(parameters);
    }

    /// <summary>
    /// Tries to create a provider instance.
    /// </summary>
    /// <returns>True if successful, false if provider not found.</returns>
    public bool TryCreate<T>(string key, ProviderParameters? parameters, out T? provider) where T : IProvider
    {
        parameters ??= new ProviderParameters();
        provider = default;

        if (!m_factories.TryGetValue(typeof(T), out var typeFactories) ||
            !typeFactories.TryGetValue(key, out var factory))
        {
            return false;
        }

        try
        {
            provider = ((Func<ProviderParameters, T>)factory)(parameters);
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Query

    /// <summary>
    /// Checks if a provider with the specified key is registered.
    /// </summary>
    public bool IsRegistered<T>(string key) where T : IProvider
    {
        return m_factories.TryGetValue(typeof(T), out var typeFactories) &&
               typeFactories.ContainsKey(key);
    }

    /// <summary>
    /// Gets all registered provider keys for a type.
    /// </summary>
    public IReadOnlyCollection<string> GetRegisteredKeys<T>() where T : IProvider
    {
        if (m_factories.TryGetValue(typeof(T), out var typeFactories))
        {
            return typeFactories.Keys.ToArray();
        }
        return Array.Empty<string>();
    }

    #endregion

    #region Built-in Providers

    private void RegisterBuiltInProviders()
    {
        // Storage providers are registered in their static constructors
        // This method ensures the types are loaded
        
        // Note: Actual registration happens in each provider class
        // via [ModuleInitializer] or explicit calls
    }

    #endregion

    #region Clear

    /// <summary>
    /// Clears all registered factories. For testing only.
    /// </summary>
    internal void ClearAll()
    {
        m_factories.Clear();
    }

    #endregion
}