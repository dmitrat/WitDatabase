using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Stores;

/// <summary>
/// Finding what a store can do when the store you are holding is a wrapper.
/// </summary>
public static class KeyValueStoreCapabilities
{
    /// <summary>
    /// How far down the chain to look before deciding something has gone wrong.
    /// </summary>
    /// <remarks>
    /// The real chain is four deep. This is not a limit on legitimate nesting, it is a guard against a
    /// wrapper that returns itself or a cycle two wrappers make between them - which would otherwise
    /// hang the caller instead of reporting a missing capability.
    /// </remarks>
    private const int MAXIMUM_DEPTH = 32;

    /// <summary>
    /// The nearest store in the chain that implements <typeparamref name="T"/>, or null.
    /// </summary>
    /// <remarks>
    /// <b>Nearest wins</b>, which is what a caller wants: a wrapper that implements a capability has
    /// done so in order to say something the layer below cannot - a transactional store's count is not
    /// the raw store's count.
    /// </remarks>
    public static T? FindCapability<T>(this IKeyValueStore store) where T : class
    {
        ArgumentNullException.ThrowIfNull(store);

        var current = store;

        for (var depth = 0; current != null && depth < MAXIMUM_DEPTH; depth++)
        {
            if (current is T capability)
                return capability;

            current = (current as IStoreWrapper)?.Inner;
        }

        return null;
    }

    /// <summary>
    /// The chain from the store you are holding down to the one that does the storing, outermost
    /// first.
    /// </summary>
    /// <remarks>
    /// For a report that says what a database is actually made of, and for a test that wants to state
    /// the chain rather than assume it.
    /// </remarks>
    public static IReadOnlyList<IKeyValueStore> Chain(this IKeyValueStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var chain = new List<IKeyValueStore>();
        var current = store;

        for (var depth = 0; current != null && depth < MAXIMUM_DEPTH; depth++)
        {
            chain.Add(current);
            current = (current as IStoreWrapper)?.Inner;
        }

        return chain;
    }
}
