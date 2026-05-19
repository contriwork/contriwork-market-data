using System.Runtime.CompilerServices;

namespace Contriwork.MarketData.Internal;

/// <summary>Shared adapter-side helpers — lazy credentials, stub streams.</summary>
internal static class AdapterHelpers
{
    /// <summary>
    /// Lazily resolve a credential. The provider callback wins over the
    /// static key so callers can rotate credentials at runtime. When
    /// <paramref name="required"/> is set and nothing resolves, throws
    /// <see cref="MissingCredentialsException"/>.
    /// </summary>
    /// <param name="adapterId">Adapter id for error context.</param>
    /// <param name="apiKey">Static key, if any.</param>
    /// <param name="apiKeyProvider">Lazy provider, if any.</param>
    /// <param name="required">Whether a credential is mandatory.</param>
    /// <param name="cancellationToken">Cancellation for the provider call.</param>
    /// <returns>The resolved credential, or <c>null</c> when optional and absent.</returns>
    public static async Task<string?> ResolveApiKeyAsync(
        string adapterId,
        string? apiKey,
        Func<CancellationToken, Task<string?>>? apiKeyProvider,
        bool required,
        CancellationToken cancellationToken)
    {
        var resolved = apiKeyProvider is not null
            ? await apiKeyProvider(cancellationToken).ConfigureAwait(false)
            : apiKey;

        if (required && string.IsNullOrEmpty(resolved))
        {
            throw new MissingCredentialsException(
                $"adapter {adapterId} requires authentication but no api key or "
                + "provider resolved a usable value",
                adapterId);
        }

        return resolved;
    }

    /// <summary>
    /// An async stream that throws <see cref="AdapterFeatureNotSupportedException"/>
    /// on first iteration — used by adapters whose
    /// <see cref="Capability.SupportsNativeStreaming"/> is false.
    /// </summary>
    /// <param name="adapterId">Adapter id for error context.</param>
    /// <param name="cancellationToken">Cancellation token (unused; present for signature parity).</param>
    /// <returns>A stream that fails fast.</returns>
    public static async IAsyncEnumerable<Ticker> StreamingNotSupportedAsync(
        string adapterId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        throw new AdapterFeatureNotSupportedException(
            $"adapter {adapterId} does not implement native streaming; "
            + "use polling fallback via MarketDataClient.SubscribeTickerAsync",
            adapterId);
#pragma warning disable CS0162 // Unreachable yield marks the method as an iterator.
        yield break;
#pragma warning restore CS0162
    }
}
