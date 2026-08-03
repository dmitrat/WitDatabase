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
    /// <exception cref="ArgumentException">
    /// Thrown when the parameter is present and cannot be read as <typeparamref name="T"/>. A missing
    /// parameter takes the default; a present one that nobody can use is a configuration error, and
    /// returning the default for it is how every numeric connection-string keyword became inert.
    /// </exception>
    public T? Get<T>(string name, T? defaultValue = default)
    {
        if (!m_values.TryGetValue(name, out var value))
            return defaultValue;

        if (!TryCoerce<T>(value, out var typed))
        {
            throw new ArgumentException(
                $"Parameter '{name}' has wrong type. Expected {typeof(T).Name}, got " +
                $"{value?.GetType().Name ?? "null"} ('{value}').", name);
        }

        return typed;
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

        if (!TryCoerce<T>(value, out var typed) || typed == null)
        {
            throw new ArgumentException(
                $"Parameter '{name}' has wrong type. Expected {typeof(T).Name}, got {value?.GetType().Name ?? "null"}",
                name);
        }

        return typed;
    }

    /// <summary>
    /// Reads a stored value as <typeparamref name="T"/>, converting the two representations that reach
    /// this bag by a route that cannot type them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Text.</b> Every value from a connection string arrives as a <c>string</c>: an ADO.NET
    /// connection string has no types. Before 12.0.0 <c>Get&lt;int&gt;("PageSize")</c> on the string
    /// <c>"16384"</c> failed a plain <c>is T</c> test and returned the default without a word, so
    /// <c>PageSize</c> and <c>CacheSize</c> were inert from a connection string and worked from the
    /// fluent builder, which sets the same keys as <c>int</c>. Phase 10 found the same shape in the LSM
    /// options and closed it one parameter set at a time; this closes the class.
    /// </para>
    /// <para>
    /// <b>Deferred values.</b> A <see cref="Lazy{T}"/> is unwrapped on read, so a caller can offer a
    /// resource - a file, a connection - that is only created if some provider actually asks for it.
    /// </para>
    /// </remarks>
    private static bool TryCoerce<T>(object? value, out T? result)
    {
        result = default;

        switch (value)
        {
            case null:
                return !typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null;

            case T typed:
                result = typed;
                return true;

            case Lazy<T> deferred:
                result = deferred.Value;
                return true;
        }

        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        if (value is not string text)
            return false;

        try
        {
            if (target == typeof(string))
            {
                result = (T)(object)text;
                return true;
            }

            if (target.IsEnum)
            {
                result = (T)Enum.Parse(target, text, ignoreCase: true);
                return true;
            }

            if (target == typeof(bool))
            {
                result = (T)(object)ParseBoolean(text);
                return true;
            }

            if (target == typeof(TimeSpan))
            {
                result = (T)(object)TimeSpan.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }

            if (target.IsPrimitive || target == typeof(decimal))
            {
                result = (T)Convert.ChangeType(text, target, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException or InvalidCastException)
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// The spellings a connection string uses for a flag, which are not the ones <c>bool.Parse</c> knows.
    /// </summary>
    private static bool ParseBoolean(string text)
    {
        return text.Trim().ToLowerInvariant() switch
        {
            "true" or "yes" or "1" or "on" => true,
            "false" or "no" or "0" or "off" => false,
            _ => bool.Parse(text)
        };
    }

    /// <summary>
    /// Checks if a parameter exists.
    /// </summary>
    public bool Has(string name) => m_values.ContainsKey(name);

    /// <summary>
    /// Gets all parameters as key-value pairs.
    /// </summary>
    public IEnumerable<KeyValuePair<string, object>> GetAll() => m_values;

    /// <summary>
    /// Clears all parameters.
    /// </summary>
    public void Clear() => m_values.Clear();

    /// <summary>
    /// Removes a parameter.
    /// </summary>
    /// <returns>True if the parameter was removed.</returns>
    public bool Remove(string name) => m_values.Remove(name);

    /// <summary>
    /// Gets the number of parameters.
    /// </summary>
    public int Count => m_values.Count;

    #endregion

    #region Properties

    /// <summary>
    /// Gets all parameter names.
    /// </summary>
    public IReadOnlyCollection<string> Names => m_values.Keys;

    #endregion
}
