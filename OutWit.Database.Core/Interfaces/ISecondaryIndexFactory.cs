namespace OutWit.Database.Core.Interfaces
{
    /// <summary>
    /// Factory for creating secondary indexes.
    /// Allows different storage backends to provide appropriate index implementations.
    /// </summary>
    public interface ISecondaryIndexFactory
    {
        /// <summary>
        /// Creates a new secondary index with the specified name.
        /// </summary>
        /// <param name="name">The unique name of the index.</param>
        /// <param name="isUnique">Whether the index should enforce uniqueness.</param>
        /// <returns>The created secondary index.</returns>
        ISecondaryIndex CreateIndex(string name, bool isUnique);

        /// <summary>
        /// Gets the provider key for this factory.
        /// Used to identify the type of indexes this factory creates.
        /// </summary>
        string ProviderKey { get; }
    }
}
