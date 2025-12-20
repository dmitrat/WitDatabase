namespace OutWit.Database.Core.Exceptions
{
    /// <summary>
    /// Exception thrown when a provider cannot be found or created.
    /// </summary>
    public sealed class ProviderNotFoundException : Exception
    {
        #region Constructors

        public ProviderNotFoundException(string providerKey, Type providerType, IReadOnlyCollection<string> availableKeys)
            : base(FormatMessage(providerKey, providerType, availableKeys))
        {
            ProviderKey = providerKey;
            ProviderType = providerType;
            AvailableKeys = availableKeys;
        }

        #endregion

        #region Functions

        private static string FormatMessage(string key, Type type, IReadOnlyCollection<string> available)
        {
            var typeName = type.Name;
            if (typeName.StartsWith('I'))
                typeName = typeName[1..];

            var availableStr = available.Count > 0
                ? $"Available: {string.Join(", ", available)}"
                : "No providers registered";

            return $"Provider '{key}' of type {typeName} is not registered. {availableStr}. " +
                   $"Make sure the provider assembly is loaded and the provider is registered.";
        }

        #endregion

        #region Propeties

        /// <summary>
        /// Gets the provider key that was not found.
        /// </summary>
        public string ProviderKey { get; }

        /// <summary>
        /// Gets the provider type that was requested.
        /// </summary>
        public Type ProviderType { get; }

        /// <summary>
        /// Gets the list of available provider keys.
        /// </summary>
        public IReadOnlyCollection<string> AvailableKeys { get; }

        #endregion
    }
}