using System.Text.Json;
using OutWit.Database.Core.Interfaces;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.Core.Indexes
{
    /// <summary>
    /// Manages persistence of index metadata to the database store.
    /// Stores index definitions (name, isUnique) so they can be recreated on database reopen.
    /// </summary>
    public sealed class IndexMetadataStore
    {
        #region Constants

        /// <summary>
        /// System key prefix for index metadata.
        /// Uses null bytes to ensure it sorts before user data.
        /// </summary>
        public static readonly byte[] SYSTEM_PREFIX = "\0\0_idx_meta_"u8.ToArray();

        /// <summary>
        /// Key for the index catalog (list of all index names).
        /// </summary>
        private static readonly byte[] CATALOG_KEY = CreateKey("_catalog_");

        #endregion

        #region Fields

        private readonly IKeyValueStore m_store;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new index metadata store.
        /// </summary>
        /// <param name="store">The underlying key-value store.</param>
        public IndexMetadataStore(IKeyValueStore store)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
        }

        #endregion

        #region Save/Load (Sync)

        /// <summary>
        /// Saves metadata for an index.
        /// </summary>
        public void SaveIndex(string name, bool isUnique)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));

            var metadata = new IndexMetadata { Name = name, IsUnique = isUnique };
            var key = CreateKey(name);
            var value = JsonSerializer.SerializeToUtf8Bytes(metadata, IndexMetadataJsonContext.Default.IndexMetadata);
            
            m_store.Put(key, value);
            
            // Update catalog
            var catalog = LoadCatalog();
            if (!catalog.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                catalog.Add(name);
                SaveCatalog(catalog);
            }
        }

        /// <summary>
        /// Loads metadata for an index.
        /// </summary>
        /// <returns>The index metadata, or null if not found.</returns>
        public IndexMetadata? LoadIndex(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            var key = CreateKey(name);
            var value = m_store.Get(key);
            
            if (value == null)
                return null;

            return JsonSerializer.Deserialize(value, IndexMetadataJsonContext.Default.IndexMetadata);
        }

        /// <summary>
        /// Removes metadata for an index.
        /// </summary>
        public bool RemoveIndex(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            var key = CreateKey(name);
            var removed = m_store.Delete(key);
            
            if (removed)
            {
                var catalog = LoadCatalog();
                catalog.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                SaveCatalog(catalog);
            }
            
            return removed;
        }

        /// <summary>
        /// Loads all index metadata.
        /// </summary>
        public IReadOnlyList<IndexMetadata> LoadAllIndexes()
        {
            var catalog = LoadCatalog();
            var result = new List<IndexMetadata>();
            
            foreach (var name in catalog)
            {
                var metadata = LoadIndex(name);
                if (metadata != null)
                {
                    result.Add(metadata);
                }
            }
            
            return result;
        }

        /// <summary>
        /// Gets all index names from the catalog.
        /// </summary>
        public IReadOnlyList<string> GetIndexNames()
        {
            return LoadCatalog().AsReadOnly();
        }

        #endregion

        #region Save/Load (Async)

        /// <summary>
        /// Saves metadata for an index asynchronously.
        /// </summary>
        public async ValueTask SaveIndexAsync(string name, bool isUnique, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));

            var metadata = new IndexMetadata { Name = name, IsUnique = isUnique };
            var key = CreateKey(name);
            var value = JsonSerializer.SerializeToUtf8Bytes(metadata, IndexMetadataJsonContext.Default.IndexMetadata);
            
            await m_store.PutAsync(key, value, cancellationToken).ConfigureAwait(false);
            
            // Update catalog
            var catalog = await LoadCatalogAsync(cancellationToken).ConfigureAwait(false);
            if (!catalog.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                catalog.Add(name);
                await SaveCatalogAsync(catalog, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Loads metadata for an index asynchronously.
        /// </summary>
        public async ValueTask<IndexMetadata?> LoadIndexAsync(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            var key = CreateKey(name);
            var value = await m_store.GetAsync(key, cancellationToken).ConfigureAwait(false);
            
            if (value == null)
                return null;

            return JsonSerializer.Deserialize(value, IndexMetadataJsonContext.Default.IndexMetadata);
        }

        /// <summary>
        /// Removes metadata for an index asynchronously.
        /// </summary>
        public async ValueTask<bool> RemoveIndexAsync(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            var key = CreateKey(name);
            var removed = await m_store.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
            
            if (removed)
            {
                var catalog = await LoadCatalogAsync(cancellationToken).ConfigureAwait(false);
                catalog.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                await SaveCatalogAsync(catalog, cancellationToken).ConfigureAwait(false);
            }
            
            return removed;
        }

        /// <summary>
        /// Loads all index metadata asynchronously.
        /// </summary>
        public async ValueTask<IReadOnlyList<IndexMetadata>> LoadAllIndexesAsync(CancellationToken cancellationToken = default)
        {
            var catalog = await LoadCatalogAsync(cancellationToken).ConfigureAwait(false);
            var result = new List<IndexMetadata>();
            
            foreach (var name in catalog)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var metadata = await LoadIndexAsync(name, cancellationToken).ConfigureAwait(false);
                if (metadata != null)
                {
                    result.Add(metadata);
                }
            }
            
            return result;
        }

        /// <summary>
        /// Gets all index names from the catalog asynchronously.
        /// </summary>
        public async ValueTask<IReadOnlyList<string>> GetIndexNamesAsync(CancellationToken cancellationToken = default)
        {
            var catalog = await LoadCatalogAsync(cancellationToken).ConfigureAwait(false);
            return catalog.AsReadOnly();
        }

        #endregion

        #region Catalog Management (Sync)

        private List<string> LoadCatalog()
        {
            var value = m_store.Get(CATALOG_KEY);
            
            if (value == null || value.Length == 0)
                return [];

            try
            {
                return JsonSerializer.Deserialize(value, IndexMetadataJsonContext.Default.ListString) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private void SaveCatalog(List<string> catalog)
        {
            var value = JsonSerializer.SerializeToUtf8Bytes(catalog, IndexMetadataJsonContext.Default.ListString);
            m_store.Put(CATALOG_KEY, value);
        }

        #endregion

        #region Catalog Management (Async)

        private async ValueTask<List<string>> LoadCatalogAsync(CancellationToken cancellationToken = default)
        {
            var value = await m_store.GetAsync(CATALOG_KEY, cancellationToken).ConfigureAwait(false);
            
            if (value == null || value.Length == 0)
                return [];

            try
            {
                return JsonSerializer.Deserialize(value, IndexMetadataJsonContext.Default.ListString) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private async ValueTask SaveCatalogAsync(List<string> catalog, CancellationToken cancellationToken = default)
        {
            var value = JsonSerializer.SerializeToUtf8Bytes(catalog, IndexMetadataJsonContext.Default.ListString);
            await m_store.PutAsync(CATALOG_KEY, value, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Key Generation

        private static byte[] CreateKey(string name)
        {
            var nameBytes = TextEncoding.UTF8.GetBytes(name.ToLowerInvariant());
            var key = new byte[SYSTEM_PREFIX.Length + nameBytes.Length];
            SYSTEM_PREFIX.CopyTo(key, 0);
            nameBytes.CopyTo(key, SYSTEM_PREFIX.Length);
            return key;
        }

        #endregion
    }

    /// <summary>
    /// Metadata for a secondary index.
    /// </summary>
    public sealed class IndexMetadata
    {
        /// <summary>
        /// The name of the index.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Whether this is a unique index.
        /// </summary>
        public bool IsUnique { get; set; }
    }
}
