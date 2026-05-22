/**
 * Error taxonomy — mirror of CONTRACT.md §7. Codes are invariant within v1.
 */

/** Base class for every error the public surface raises. */
export class MarketDataError extends Error {
  /** Stable SCREAMING_SNAKE_CASE error code. */
  public readonly code: string;

  /** Adapter that raised the error, when applicable. */
  public readonly adapterId?: string;

  /** Per-adapter failure trail; non-empty for aggregate errors. */
  public readonly causes: readonly MarketDataError[];

  public constructor(
    code: string,
    message: string,
    adapterId?: string,
    causes?: readonly MarketDataError[],
  ) {
    super(message);
    this.name = new.target.name;
    this.code = code;
    if (adapterId !== undefined) {
      this.adapterId = adapterId;
    }
    this.causes = causes ?? [];
  }
}

/** Caller-supplied parameters failed validation. */
export class InvalidInputError extends MarketDataError {
  public static readonly CODE = "INVALID_INPUT";
  public constructor(message: string, adapterId?: string) {
    super(InvalidInputError.CODE, message, adapterId);
  }
}

/** The requested interval is not supported by the chosen adapter. */
export class InvalidIntervalError extends MarketDataError {
  public static readonly CODE = "INVALID_INTERVAL";
  public constructor(message: string, adapterId?: string) {
    super(InvalidIntervalError.CODE, message, adapterId);
  }
}

/** The adapter does not support the requested quote currency. */
export class UnsupportedQuoteCurrencyError extends MarketDataError {
  public static readonly CODE = "UNSUPPORTED_QUOTE_CURRENCY";
  public constructor(message: string, adapterId?: string) {
    super(UnsupportedQuoteCurrencyError.CODE, message, adapterId);
  }
}

/** The provider does not recognize the requested symbol. */
export class SymbolNotFoundError extends MarketDataError {
  public static readonly CODE = "SYMBOL_NOT_FOUND";
  public constructor(message: string, adapterId?: string) {
    super(SymbolNotFoundError.CODE, message, adapterId);
  }
}

/** The adapter's rate-limit budget was exhausted after retries. */
export class RateLimitedError extends MarketDataError {
  public static readonly CODE = "RATE_LIMITED";
  public constructor(message: string, adapterId?: string) {
    super(RateLimitedError.CODE, message, adapterId);
  }
}

/** An adapter's network, HTTP, or parse layer failed. */
export class AdapterUnavailableError extends MarketDataError {
  public static readonly CODE = "ADAPTER_UNAVAILABLE";
  public constructor(message: string, adapterId?: string) {
    super(AdapterUnavailableError.CODE, message, adapterId);
  }
}

/** The adapter does not support the requested operation. */
export class AdapterFeatureNotSupportedError extends MarketDataError {
  public static readonly CODE = "ADAPTER_FEATURE_NOT_SUPPORTED";
  public constructor(message: string, adapterId?: string) {
    super(AdapterFeatureNotSupportedError.CODE, message, adapterId);
  }
}

/** The adapter requires authentication but no credential resolved. */
export class MissingCredentialsError extends MarketDataError {
  public static readonly CODE = "MISSING_CREDENTIALS";
  public constructor(message: string, adapterId?: string) {
    super(MissingCredentialsError.CODE, message, adapterId);
  }
}

/** No adapter chain is registered for the requested market. */
export class NoAdapterForMarketError extends MarketDataError {
  public static readonly CODE = "NO_ADAPTER_FOR_MARKET";
  public constructor(message: string, adapterId?: string) {
    super(NoAdapterForMarketError.CODE, message, adapterId);
  }
}

/** Every adapter in the chain failed. */
export class AllAdaptersFailedError extends MarketDataError {
  public static readonly CODE = "ALL_ADAPTERS_FAILED";
  public constructor(message: string, causes?: readonly MarketDataError[]) {
    super(AllAdaptersFailedError.CODE, message, undefined, causes);
  }
}

/** Streaming was requested but no adapter could satisfy it. */
export class StreamingNotSupportedError extends MarketDataError {
  public static readonly CODE = "STREAMING_NOT_SUPPORTED";
  public constructor(message: string, adapterId?: string) {
    super(StreamingNotSupportedError.CODE, message, adapterId);
  }
}

/** An active streaming subscription lost its connection. */
export class StreamDisconnectedError extends MarketDataError {
  public static readonly CODE = "STREAM_DISCONNECTED";
  public constructor(message: string, adapterId?: string) {
    super(StreamDisconnectedError.CODE, message, adapterId);
  }
}

type ErrorCtor = new (message: string, adapterId?: string) => MarketDataError;

const CODE_TO_CTOR: ReadonlyMap<string, ErrorCtor> = new Map<string, ErrorCtor>(
  [
    [InvalidInputError.CODE, InvalidInputError],
    [InvalidIntervalError.CODE, InvalidIntervalError],
    [UnsupportedQuoteCurrencyError.CODE, UnsupportedQuoteCurrencyError],
    [SymbolNotFoundError.CODE, SymbolNotFoundError],
    [RateLimitedError.CODE, RateLimitedError],
    [AdapterUnavailableError.CODE, AdapterUnavailableError],
    [AdapterFeatureNotSupportedError.CODE, AdapterFeatureNotSupportedError],
    [MissingCredentialsError.CODE, MissingCredentialsError],
    [NoAdapterForMarketError.CODE, NoAdapterForMarketError],
    [StreamingNotSupportedError.CODE, StreamingNotSupportedError],
    [StreamDisconnectedError.CODE, StreamDisconnectedError],
  ],
);

/**
 * Construct the exception for a stable code. `ALL_ADAPTERS_FAILED` is
 * excluded (it has a different constructor shape) and handled directly.
 *
 * @param code - The SCREAMING_SNAKE_CASE error code.
 * @param message - Error message.
 * @param adapterId - Adapter id, when applicable.
 * @returns A new `MarketDataError` of the mapped subclass.
 * @throws Error when the code is unknown.
 */
export function errorForCode(
  code: string,
  message: string,
  adapterId?: string,
): MarketDataError {
  if (code === AllAdaptersFailedError.CODE) {
    return new AllAdaptersFailedError(message);
  }
  const ctor = CODE_TO_CTOR.get(code);
  if (ctor === undefined) {
    throw new Error(`unknown error code: ${code}`);
  }
  return new ctor(message, adapterId);
}
