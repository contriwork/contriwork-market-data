namespace Contriwork.MarketData;

/// <summary>
/// Maps a <c>market</c> string to an ordered adapter chain. CONTRACT.md §4.
/// Constructed by the caller's wiring code and handed to
/// <see cref="MarketDataClient"/>.
/// </summary>
public sealed class AdapterRegistry
{
    private readonly Dictionary<string, IReadOnlyList<IMarketDataAdapter>> chains =
        new(StringComparer.Ordinal);

    /// <summary>Initializes an empty registry.</summary>
    public AdapterRegistry()
    {
    }

    /// <summary>Initializes a registry from an existing market→chain map.</summary>
    /// <param name="chains">Market-to-chain pairs.</param>
    public AdapterRegistry(IReadOnlyDictionary<string, IReadOnlyList<IMarketDataAdapter>> chains)
    {
        ArgumentNullException.ThrowIfNull(chains);
        foreach (var (market, adapters) in chains)
        {
            this.chains[market] = [.. adapters];
        }
    }

    /// <summary>Register or replace the adapter chain for a market.</summary>
    /// <param name="market">Market string.</param>
    /// <param name="adapters">Ordered adapter chain.</param>
    public void Register(string market, IEnumerable<IMarketDataAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        this.chains[market] = [.. adapters];
    }

    /// <summary>Get the ordered adapter chain for a market.</summary>
    /// <param name="market">Market string.</param>
    /// <returns>The chain, or an empty list when the market is unknown.</returns>
    public IReadOnlyList<IMarketDataAdapter> ChainFor(string market) =>
        this.chains.TryGetValue(market, out var chain) ? chain : [];

    /// <summary>All registered market strings.</summary>
    /// <returns>The market keys.</returns>
    public IReadOnlyCollection<string> Markets() => this.chains.Keys;
}
