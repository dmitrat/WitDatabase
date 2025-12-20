namespace OutWit.Database.Core.Interfaces;

/// <summary>
/// Factory interface for creating provider instances by key.
/// </summary>
/// <typeparam name="T">The provider interface type (e.g., ICryptoProvider, IStorage).</typeparam>
public interface IProviderFactory<T> where T : IProvider
{
    /// <summary>
    /// Creates a provider instance using the specified key and parameters.
    /// </summary>
    /// <param name="key">The provider key (e.g., "aes-gcm", "file").</param>
    /// <param name="parameters">Provider-specific parameters.</param>
    /// <returns>A new provider instance.</returns>
    /// <exception cref="ProviderNotFoundException">Thrown when no factory is registered for the key.</exception>
    T Create(string key, ProviderParameters parameters);

    /// <summary>
    /// Checks if a provider with the specified key is registered.
    /// </summary>
    bool IsRegistered(string key);

    /// <summary>
    /// Gets all registered provider keys.
    /// </summary>
    IReadOnlyCollection<string> RegisteredKeys { get; }
}

/// <summary>
/// Parameters passed to provider factories for instantiation.
/// </summary>
public sealed class ProviderParameters
{
    #region Fields

    private readonly Dictionary<string, object> m_values = new(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Constructors

    /// <summary>
    /// Creates empty parameters.
    /// </summary>
    public ProviderParameters()
    {
    }

    /// <summary>
    /// Creates parameters from a dictionary.
    /// </summary>
    public ProviderParameters(IDictionary<string, object> values)
    {
        foreach (var kvp in values)
        {
            m_values[kvp.Key] = kvp.Value;
        }
    }

    #endregion

    #region Set

    /// <summary>
    /// Sets a parameter value.
    /// </summary>
    public ProviderParameters Set(string name, object value)
    {
        m_values[name] = value;
        return this;
    }

    /// <summary>
    /// Sets a typed parameter value.
    /// </summary>
    public ProviderParameters Set<T>(string name, T value) where T : notnull
    {
        m_values[name] = value;
        return this;
    }

    #endregion

    #region Get

    /// <summary>
    /// Gets a parameter value, or default if not found.
    /// </summary>
    public T? Get<T>(string name, T? defaultValue = default)
    {
        if (m_values.TryGetValue(name, out var value) && value is T typed)
        {
            return typed;
        }
        return defaultValue;
    }

    /// <summary>
    /// Gets a required parameter value.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown if parameter is missing or wrong type.</exception>
    public T GetRequired<T>(string name)
    {
        if (!m_values.TryGetValue(name, out var value))
        {
            throw new ArgumentException($"Required parameter '{name}' is missing", name);
        }

        if (value is not T typed)
        {
            throw new ArgumentException(
                $"Parameter '{name}' has wrong type. Expected {typeof(T).Name}, got {value?.GetType().Name ?? "null"}", 
                name);
        }

        return typed;
    }

    /// <summary>
    /// Checks if a parameter exists.
    /// </summary>
    public bool Has(string name) => m_values.ContainsKey(name);

    #endregion

    #region Properties

    /// <summary>
    /// Gets all parameter names.
    /// </summary>
    public IReadOnlyCollection<string> Names => m_values.Keys;

    #endregion
}
