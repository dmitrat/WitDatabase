using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Indexes
{
    /// <summary>
    /// Factory for creating secondary indexes using any IKeyValueStore implementation.
    /// Provides storage-agnostic index creation.
    /// </summary>
    public sealed class SecondaryIndexFactoryKeyValueStore : ISecondaryIndexFactory
    {
        #region Constants

        /// <summary>
        /// Provider key for key-value store index factory.
        /// </summary>
        public const string PROVIDER_KEY = "kvstore";

        #endregion

        #region Fields

        private readonly Func<string, IKeyValueStore> m_storeFactory;
        private readonly string m_providerKey;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new key-value store index factory.
        /// </summary>
        /// <param name="storeFactory">
        /// Factory function that creates a new IKeyValueStore for each index.
        /// The function receives the index name and returns a new store instance.
        /// </param>
        /// <param name="providerKey">Optional provider key override. Defaults to "kvstore".</param>
        public SecondaryIndexFactoryKeyValueStore(
            Func<string, IKeyValueStore> storeFactory, 
            string? providerKey = null)
        {
            m_storeFactory = storeFactory ?? throw new ArgumentNullException(nameof(storeFactory));
            m_providerKey = providerKey ?? PROVIDER_KEY;
        }

        #endregion

        #region ISecondaryIndexFactory

        /// <inheritdoc/>
        public ISecondaryIndex CreateIndex(string name, bool isUnique)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));

            var store = m_storeFactory(name);
            return new SecondaryIndexKeyValueStore(name, store, isUnique, ownsStore: true);
        }

        /// <inheritdoc/>
        public string ProviderKey => m_providerKey;

        #endregion
    }
}
