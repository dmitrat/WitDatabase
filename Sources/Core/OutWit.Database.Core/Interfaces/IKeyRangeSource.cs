namespace OutWit.Database.Core.Interfaces
{
    /// <summary>
    /// Interface for stores that can name their smallest and largest key without reading everything in
    /// between.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two questions the engine asks often and could only answer by walking the whole store: what is the
    /// first key, and what is the last. <c>ISecondaryIndex.GetLastEntry</c> was
    /// <c>Scan(null, null).LastOrDefault()</c> - a full pass over an index to read one key, in a public
    /// API - and the query optimizer could not ask at all, which is why its estimate for every range
    /// predicate was a flat 20% of the table.
    /// </para>
    /// <para>
    /// A separate interface rather than a member of <see cref="IKeyValueStore"/>, for the same reason
    /// <see cref="IProviderMetadataSource"/> is one: a store that cannot answer cheaply should not be
    /// made to pretend it can. A caller that meets a store without it keeps whatever it did before.
    /// </para>
    /// </remarks>
    public interface IKeyRangeSource
    {
        /// <summary>
        /// The smallest key in the store, or <c>null</c> when it holds nothing.
        /// </summary>
        byte[]? GetFirstKey();

        /// <summary>
        /// The largest key in the store, or <c>null</c> when it holds nothing.
        /// </summary>
        byte[]? GetLastKey();
    }
}
