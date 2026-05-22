/**
 * InMemoryAdapter — test fixture + reference implementation. Drives the
 * cross-language contract fixtures and serves as the canonical example of
 * the adapter contract.
 */
import type { ApiKeyProvider, MarketDataAdapter } from "../adapter.js";
import { type Clock, SystemClock } from "../clock.js";
import { resolveApiKey } from "../internal/credentials.js";
import {
  AdapterFeatureNotSupportedError,
  InvalidIntervalError,
  type MarketDataError,
  SymbolNotFoundError,
  UnsupportedQuoteCurrencyError,
  errorForCode,
} from "../errors.js";
import {
  type Candle,
  type Capability,
  EMPTY_EXTRA,
  Interval,
  type OrderBook,
  type SpotPrice,
  type Ticker,
  quoteCurrencySupported,
} from "../types.js";

/** Forced error programming for a symbol. */
export interface InMemoryFailModeSpec {
  readonly symbol: string;
  readonly code: string;
  readonly failFirstN?: number;
}

/** A fail mode with its remaining-call counter. */
export class InMemoryFailMode {
  public readonly symbol: string;
  public readonly code: string;
  private remaining: number;

  public constructor(spec: InMemoryFailModeSpec) {
    // Validate the code eagerly (throws on a typo).
    errorForCode(spec.code, "probe");
    this.symbol = spec.symbol;
    this.code = spec.code;
    this.remaining = spec.failFirstN ?? -1;
  }

  /** Consume one invocation; returns whether this call should fail. */
  public consume(): boolean {
    if (this.remaining === 0) {
      return false;
    }
    if (this.remaining > 0) {
      this.remaining -= 1;
    }
    return true;
  }
}

/** Pre-seeded data for a single symbol. */
export interface InMemorySymbolData {
  readonly spot?: SpotPrice;
  readonly ohlcv?: ReadonlyMap<Interval, readonly Candle[]>;
  readonly orderBook?: OrderBook;
  readonly tickerStream?: readonly Ticker[];
}

/** Options for constructing an {@link InMemoryAdapter}. */
export interface InMemoryAdapterOptions {
  readonly adapterId: string;
  readonly data?: ReadonlyMap<string, InMemorySymbolData>;
  readonly capability?: Capability;
  readonly failModes?: readonly InMemoryFailModeSpec[];
  readonly apiKey?: string;
  readonly apiKeyProvider?: ApiKeyProvider;
  readonly clock?: Clock;
}

function defaultCapability(): Capability {
  return {
    supportedMarkets: ["*"],
    supportedIntervals: Object.values(Interval),
    supportedQuoteCurrencies: "ANY",
    supportsOrderBook: true,
    supportsNativeStreaming: false,
    rateLimitPerMinute: 9999,
    requiresAuth: false,
  };
}

/** Adapter backed by pre-seeded in-memory data. */
export class InMemoryAdapter implements MarketDataAdapter {
  public readonly adapterId: string;
  public readonly capability: Capability;

  private readonly data: ReadonlyMap<string, InMemorySymbolData>;
  private readonly failModes: readonly InMemoryFailMode[];
  private readonly apiKey?: string;
  private readonly apiKeyProvider?: ApiKeyProvider;
  private readonly clock: Clock;
  private readonly callCounts = new Map<string, number>();

  public constructor(options: InMemoryAdapterOptions) {
    if (options.adapterId.length === 0) {
      throw new Error("adapterId must be non-empty");
    }
    this.adapterId = options.adapterId;
    this.data = options.data ?? new Map();
    this.failModes = (options.failModes ?? []).map(
      (s) => new InMemoryFailMode(s),
    );
    if (options.apiKey !== undefined) {
      this.apiKey = options.apiKey;
    }
    if (options.apiKeyProvider !== undefined) {
      this.apiKeyProvider = options.apiKeyProvider;
    }
    this.clock = options.clock ?? new SystemClock();
    this.capability = options.capability ?? defaultCapability();
  }

  /** Per-operation call counts — test introspection only. */
  public get callCountsView(): ReadonlyMap<string, number> {
    return this.callCounts;
  }

  public async getSpot(
    symbol: string,
    quoteCurrency: string,
  ): Promise<SpotPrice> {
    await this.gate("spot", symbol, quoteCurrency);
    const record = this.symbol(symbol);
    if (record.spot === undefined) {
      throw new SymbolNotFoundError(
        `adapter ${this.adapterId} has no spot for '${symbol}'`,
        this.adapterId,
      );
    }
    return { ...record.spot, symbol, sourceAdapter: this.adapterId };
  }

  public async getOhlcv(
    symbol: string,
    interval: Interval,
    since: Date | undefined,
    limit: number,
  ): Promise<readonly Candle[]> {
    await this.gate("ohlcv", symbol, undefined);
    if (!this.capability.supportedIntervals.includes(interval)) {
      throw new InvalidIntervalError(
        `adapter ${this.adapterId} does not support interval ${interval}`,
        this.adapterId,
      );
    }
    const record = this.symbol(symbol);
    const candles = record.ohlcv?.get(interval);
    if (candles === undefined || candles.length === 0) {
      throw new SymbolNotFoundError(
        `adapter ${this.adapterId} has no ohlcv for '${symbol}'/${interval}`,
        this.adapterId,
      );
    }
    return candles
      .filter(
        (c) => since === undefined || c.timestamp.getTime() >= since.getTime(),
      )
      .sort((a, b) => a.timestamp.getTime() - b.timestamp.getTime())
      .slice(0, limit);
  }

  public async getOrderBook(symbol: string, depth: number): Promise<OrderBook> {
    await this.gate("order_book", symbol, undefined);
    if (!this.capability.supportsOrderBook) {
      throw new AdapterFeatureNotSupportedError(
        `adapter ${this.adapterId} does not support order book`,
        this.adapterId,
      );
    }
    const record = this.symbol(symbol);
    if (record.orderBook === undefined) {
      throw new SymbolNotFoundError(
        `adapter ${this.adapterId} has no order book for '${symbol}'`,
        this.adapterId,
      );
    }
    const book = record.orderBook;
    return {
      ...book,
      symbol,
      sourceAdapter: this.adapterId,
      bids: [...book.bids]
        .sort((a, b) => b.price.comparedTo(a.price))
        .slice(0, depth),
      asks: [...book.asks]
        .sort((a, b) => a.price.comparedTo(b.price))
        .slice(0, depth),
    };
  }

  public async *subscribeTicker(symbol: string): AsyncIterable<Ticker> {
    await this.gate("ticker", symbol, undefined);
    const record = this.symbol(symbol);
    for (const ticker of record.tickerStream ?? []) {
      yield { ...ticker, symbol, sourceAdapter: this.adapterId };
    }
  }

  private async gate(
    op: string,
    symbol: string,
    quoteCurrency: string | undefined,
  ): Promise<void> {
    this.callCounts.set(op, (this.callCounts.get(op) ?? 0) + 1);

    if (this.capability.requiresAuth) {
      await resolveApiKey(
        this.adapterId,
        this.apiKey,
        this.apiKeyProvider,
        true,
      );
    }

    if (
      quoteCurrency !== undefined &&
      !quoteCurrencySupported(
        this.capability.supportedQuoteCurrencies,
        quoteCurrency,
      )
    ) {
      throw new UnsupportedQuoteCurrencyError(
        `adapter ${this.adapterId} does not support quote currency '${quoteCurrency}'`,
        this.adapterId,
      );
    }

    for (const fm of this.failModes) {
      if (fm.symbol === symbol && fm.consume()) {
        const error: MarketDataError = errorForCode(
          fm.code,
          `adapter ${this.adapterId} forced ${fm.code} on symbol '${symbol}'`,
          this.adapterId,
        );
        throw error;
      }
    }
  }

  private symbol(symbol: string): InMemorySymbolData {
    const entry = this.data.get(symbol);
    if (entry === undefined) {
      throw new SymbolNotFoundError(
        `adapter ${this.adapterId} has no data for symbol '${symbol}'`,
        this.adapterId,
      );
    }
    return entry;
  }
}

// Re-export for convenience so tests can build an empty extra map.
export { EMPTY_EXTRA };
