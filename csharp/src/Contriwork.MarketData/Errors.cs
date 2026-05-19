namespace Contriwork.MarketData;

/// <summary>
/// Base class for every error the public surface raises. CONTRACT v1 §7.
/// The <see cref="Code"/> string is stable within the v1 contract revision.
/// </summary>
public class MarketDataException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MarketDataException"/> class.
    /// </summary>
    /// <param name="code">Stable SCREAMING_SNAKE_CASE error code.</param>
    /// <param name="message">Human-readable message.</param>
    /// <param name="adapterId">Adapter that raised the error, when applicable.</param>
    /// <param name="causes">Per-adapter failure trail, for aggregate errors.</param>
    public MarketDataException(
        string code,
        string message,
        string? adapterId = null,
        IReadOnlyList<MarketDataException>? causes = null)
        : base(message)
    {
        Code = code;
        AdapterId = adapterId;
        Causes = causes ?? [];
    }

    /// <summary>Stable error code from the CONTRACT.md §7 taxonomy.</summary>
    public string Code { get; }

    /// <summary>Adapter that raised the error, when applicable.</summary>
    public string? AdapterId { get; }

    /// <summary>Per-adapter failure trail; non-empty for aggregate errors.</summary>
    public IReadOnlyList<MarketDataException> Causes { get; }
}

/// <summary>Caller-supplied parameters failed validation.</summary>
public sealed class InvalidInputException : MarketDataException
{
    /// <summary>The stable error code for this exception.</summary>
    public const string ErrorCode = "INVALID_INPUT";

    /// <summary>Initializes a new instance of the <see cref="InvalidInputException"/> class.</summary>
    /// <param name="message">Human-readable message.</param>
    /// <param name="adapterId">Adapter that raised the error, when applicable.</param>
    public InvalidInputException(string message, string? adapterId = null)
        : base(ErrorCode, message, adapterId)
    {
    }
}

/// <summary>The requested interval is not supported by the chosen adapter.</summary>
public sealed class InvalidIntervalException : MarketDataException
{
    /// <summary>The stable error code for this exception.</summary>
    public const string ErrorCode = "INVALID_INTERVAL";

    /// <summary>Initializes a new instance of the <see cref="InvalidIntervalException"/> class.</summary>
    /// <param name="message">Human-readable message.</param>
    /// <param name="adapterId">Adapter that raised the error, when applicable.</param>
    public InvalidIntervalException(string message, string? adapterId = null)
        : base(ErrorCode, message, adapterId)
    {
    }
}

/// <summary>The adapter does not support the requested quote currency.</summary>
public sealed class UnsupportedQuoteCurrencyException : MarketDataException
{
    /// <summary>The stable error code for this exception.</summary>
    public const string ErrorCode = "UNSUPPORTED_QUOTE_CURRENCY";

    /// <summary>Initializes a new instance of the <see cref="UnsupportedQuoteCurrencyException"/> class.</summary>
    /// <param name="message">Human-readable message.</param>
    /// <param name="adapterId">Adapter that raised the error, when applicable.</param>
    public UnsupportedQuoteCurrencyException(string message, string? adapterId = null)
        : base(ErrorCode, message, adapterId)
    {
    }
}

/// <summary>The provider does not recognize the requested symbol.</summary>
public sealed class SymbolNotFoundException : MarketDataException
{
    /// <summary>The stable error code for this exception.</summary>
    public const string ErrorCode = "SYMBOL_NOT_FOUND";

    /// <summary>Initializes a new instance of the <see cref="SymbolNotFoundException"/> class.</summary>
    /// <param name="message">Human-readable message.</param>
    /// <param name="adapterId">Adapter that raised the error, when applicable.</param>
    public SymbolNotFoundException(string message, string? adapterId = null)
        : base(ErrorCode, message, adapterId)
    {
    }
}

/// <summary>The adapter's rate-limit budget was exhausted after retries.</summary>
public sealed class RateLimitedException : MarketDataException
{
    /// <summary>The stable error code for this exception.</summary>
    public const string ErrorCode = "RATE_LIMITED";

    /// <summary>Initializes a new instance of the <see cref="RateLimitedException"/> class.</summary>
    /// <param name="message">Human-readable message.</param>
    /// <param name="adapterId">Adapter that raised the error, when applicable.</param>
    public RateLimitedException(string message, string? adapterId = null)
        : base(ErrorCode, message, adapterId)
    {
    }
}

/// <summary>An adapter's network, HTTP, or parse layer failed.</summary>
public sealed class AdapterUnavailableException : MarketDataException
{
    /// <summary>The stable error code for this exception.</summary>
    public const string ErrorCode = "ADAPTER_UNAVAILABLE";

    /// <summary>Initializes a new instance of the <see cref="AdapterUnavailableException"/> class.</summary>
    /// <param name="message">Human-readable message.</param>
    /// <param name="adapterId">Adapter that raised the error, when applicable.</param>
    public AdapterUnavailableException(string message, string? adapterId = null)
        : base(ErrorCode, message, adapterId)
    {
    }
}

/// <summary>The adapter does not support the requested operation.</summary>
public sealed class AdapterFeatureNotSupportedException : MarketDataException
{
    /// <summary>The stable error code for this exception.</summary>
    public const string ErrorCode = "ADAPTER_FEATURE_NOT_SUPPORTED";

    /// <summary>Initializes a new instance of the <see cref="AdapterFeatureNotSupportedException"/> class.</summary>
    /// <param name="message">Human-readable message.</param>
    /// <param name="adapterId">Adapter that raised the error, when applicable.</param>
    public AdapterFeatureNotSupportedException(string message, string? adapterId = null)
        : base(ErrorCode, message, adapterId)
    {
    }
}

/// <summary>The adapter requires authentication but no credential resolved.</summary>
public sealed class MissingCredentialsException : MarketDataException
{
    /// <summary>The stable error code for this exception.</summary>
    public const string ErrorCode = "MISSING_CREDENTIALS";

    /// <summary>Initializes a new instance of the <see cref="MissingCredentialsException"/> class.</summary>
    /// <param name="message">Human-readable message.</param>
    /// <param name="adapterId">Adapter that raised the error, when applicable.</param>
    public MissingCredentialsException(string message, string? adapterId = null)
        : base(ErrorCode, message, adapterId)
    {
    }
}

/// <summary>No adapter chain is registered for the requested market.</summary>
public sealed class NoAdapterForMarketException : MarketDataException
{
    /// <summary>The stable error code for this exception.</summary>
    public const string ErrorCode = "NO_ADAPTER_FOR_MARKET";

    /// <summary>Initializes a new instance of the <see cref="NoAdapterForMarketException"/> class.</summary>
    /// <param name="message">Human-readable message.</param>
    /// <param name="adapterId">Adapter that raised the error, when applicable.</param>
    public NoAdapterForMarketException(string message, string? adapterId = null)
        : base(ErrorCode, message, adapterId)
    {
    }
}

/// <summary>Every adapter in the chain failed.</summary>
public sealed class AllAdaptersFailedException : MarketDataException
{
    /// <summary>The stable error code for this exception.</summary>
    public const string ErrorCode = "ALL_ADAPTERS_FAILED";

    /// <summary>Initializes a new instance of the <see cref="AllAdaptersFailedException"/> class.</summary>
    /// <param name="message">Human-readable message.</param>
    /// <param name="causes">Per-adapter failure trail.</param>
    public AllAdaptersFailedException(string message, IReadOnlyList<MarketDataException>? causes = null)
        : base(ErrorCode, message, adapterId: null, causes: causes)
    {
    }
}

/// <summary>Streaming was requested but no adapter could satisfy it.</summary>
public sealed class StreamingNotSupportedException : MarketDataException
{
    /// <summary>The stable error code for this exception.</summary>
    public const string ErrorCode = "STREAMING_NOT_SUPPORTED";

    /// <summary>Initializes a new instance of the <see cref="StreamingNotSupportedException"/> class.</summary>
    /// <param name="message">Human-readable message.</param>
    /// <param name="adapterId">Adapter that raised the error, when applicable.</param>
    public StreamingNotSupportedException(string message, string? adapterId = null)
        : base(ErrorCode, message, adapterId)
    {
    }
}

/// <summary>An active streaming subscription lost its connection.</summary>
public sealed class StreamDisconnectedException : MarketDataException
{
    /// <summary>The stable error code for this exception.</summary>
    public const string ErrorCode = "STREAM_DISCONNECTED";

    /// <summary>Initializes a new instance of the <see cref="StreamDisconnectedException"/> class.</summary>
    /// <param name="message">Human-readable message.</param>
    /// <param name="adapterId">Adapter that raised the error, when applicable.</param>
    public StreamDisconnectedException(string message, string? adapterId = null)
        : base(ErrorCode, message, adapterId)
    {
    }
}

/// <summary>
/// Maps stable error codes to their exception types — used by the
/// cross-language contract-test runner.
/// </summary>
public static class ErrorCodes
{
    private static readonly Dictionary<string, Type> CodeToType =
        new(StringComparer.Ordinal)
        {
            [InvalidInputException.ErrorCode] = typeof(InvalidInputException),
            [InvalidIntervalException.ErrorCode] = typeof(InvalidIntervalException),
            [UnsupportedQuoteCurrencyException.ErrorCode] = typeof(UnsupportedQuoteCurrencyException),
            [SymbolNotFoundException.ErrorCode] = typeof(SymbolNotFoundException),
            [RateLimitedException.ErrorCode] = typeof(RateLimitedException),
            [AdapterUnavailableException.ErrorCode] = typeof(AdapterUnavailableException),
            [AdapterFeatureNotSupportedException.ErrorCode] = typeof(AdapterFeatureNotSupportedException),
            [MissingCredentialsException.ErrorCode] = typeof(MissingCredentialsException),
            [NoAdapterForMarketException.ErrorCode] = typeof(NoAdapterForMarketException),
            [AllAdaptersFailedException.ErrorCode] = typeof(AllAdaptersFailedException),
            [StreamingNotSupportedException.ErrorCode] = typeof(StreamingNotSupportedException),
            [StreamDisconnectedException.ErrorCode] = typeof(StreamDisconnectedException),
        };

    /// <summary>Look up the exception <see cref="Type"/> for a stable code.</summary>
    /// <param name="code">The SCREAMING_SNAKE_CASE error code.</param>
    /// <returns>The mapped exception type.</returns>
    /// <exception cref="KeyNotFoundException">When the code is unknown.</exception>
    public static Type TypeForCode(string code) => CodeToType[code];
}
