/**
 * MarketDataClient — public orchestrator. CONTRACT.md §1, §3, §4.
 */
import type { MarketDataAdapter } from "./adapter.js";
import { type Clock, SystemClock } from "./clock.js";
import { type ClientConfig, defaultClientConfig } from "./config.js";
import {
  AdapterFeatureNotSupportedError,
  AdapterUnavailableError,
  AllAdaptersFailedError,
  InvalidInputError,
  InvalidIntervalError,
  type MarketDataError,
  MissingCredentialsError,
  NoAdapterForMarketError,
  RateLimitedError,
  StreamingNotSupportedError,
  SymbolNotFoundError,
  UnsupportedQuoteCurrencyError,
  errorForCode,
} from "./errors.js";
import { TtlCache } from "./internal/cache.js";
import { TokenBucket, runWithRetry } from "./internal/rate-limit.js";
import { pollTicker } from "./internal/streaming.js";
import type { MarketDataPort, SubscribeTickerOptions } from "./port.js";
import type { AdapterRegistry } from "./registry.js";
import {
  type Candle,
  Interval,
  type OrderBook,
  type SpotPrice,
  type Ticker,
  quoteCurrencySupported,
} from "./types.js";

const OHLCV_LIMIT_CAP = 1000;
const ORDER_BOOK_DEPTH_CAP = 100;
const SYMBOL_MAX_LENGTH = 64;

// Codes that, when uniform across the chain, surface directly instead of
// being wrapped in ALL_ADAPTERS_FAILED. CONTRACT.md §3.
const FATAL_PASSTHROUGH_CODES = new Set<string>([
  InvalidIntervalError.CODE,
  UnsupportedQuoteCurrencyError.CODE,
  MissingCredentialsError.CODE,
  SymbolNotFoundError.CODE,
]);

const ASCII_PRINTABLE = /^[\x20-\x7E]+$/;

/** Concrete {@link MarketDataPort} implementation. */
export class MarketDataClient implements MarketDataPort {
  private readonly registry: AdapterRegistry;
  private readonly config: ClientConfig;
  private readonly clock: Clock;
  private readonly spotCache: TtlCache<SpotPrice>;
  private readonly ohlcvCache: TtlCache<readonly Candle[]>;
  private readonly orderBookCache: TtlCache<OrderBook>;
  private readonly buckets = new Map<string, TokenBucket>();

  public constructor(
    registry: AdapterRegistry,
    config?: ClientConfig,
    clock?: Clock,
  ) {
    this.registry = registry;
    this.config = config ?? defaultClientConfig();
    this.clock = clock ?? new SystemClock();
    this.spotCache = new TtlCache<SpotPrice>(
      this.config.cache.maxEntries,
      this.clock,
    );
    this.ohlcvCache = new TtlCache<readonly Candle[]>(
      this.config.cache.maxEntries,
      this.clock,
    );
    this.orderBookCache = new TtlCache<OrderBook>(
      this.config.cache.maxEntries,
      this.clock,
    );
  }

  public async getSpot(
    symbol: string,
    market: string,
    quoteCurrency = "USD",
  ): Promise<SpotPrice> {
    validateSymbol(symbol);
    validateMarket(market);
    validateQuoteCurrency(quoteCurrency);
    const chain = this.resolveChain(market);
    const cacheKey = `get_spot|${market}|${symbol}|${quoteCurrency}`;
    if (this.config.cache.enabled) {
      const hit = this.spotCache.get(cacheKey);
      if (hit !== undefined) {
        return hit;
      }
    }

    const result = await this.runChain(chain, (adapter) => {
      rejectIfUnsupportedQuote(adapter, quoteCurrency);
      return adapter.getSpot(symbol, quoteCurrency);
    });

    if (this.config.cache.enabled) {
      this.spotCache.set(cacheKey, result, this.config.cache.spotTtlSeconds);
    }
    return result;
  }

  public async getOhlcv(
    symbol: string,
    market: string,
    interval: Interval,
    since?: Date,
    limit = 100,
  ): Promise<readonly Candle[]> {
    validateSymbol(symbol);
    validateMarket(market);
    if (limit < 1 || limit > OHLCV_LIMIT_CAP) {
      throw new InvalidInputError(
        `limit must be 1..${OHLCV_LIMIT_CAP.toString()}, got ${limit.toString()}`,
      );
    }
    if (since !== undefined && since.getTime() > this.clock.now().getTime()) {
      throw new InvalidInputError("since must not be in the future");
    }

    const chain = this.resolveChain(market);
    const sinceKey = since !== undefined ? since.toISOString() : "null";
    const cacheKey = `get_ohlcv|${market}|${symbol}|${interval}|${sinceKey}|${limit.toString()}`;
    if (this.config.cache.enabled) {
      const hit = this.ohlcvCache.get(cacheKey);
      if (hit !== undefined) {
        return hit;
      }
    }

    const result = await this.runChain(chain, (adapter) => {
      if (!adapter.capability.supportedIntervals.includes(interval)) {
        throw new InvalidIntervalError(
          `adapter ${adapter.adapterId} does not support interval ${interval}`,
          adapter.adapterId,
        );
      }
      return adapter.getOhlcv(symbol, interval, since, limit);
    });

    if (this.config.cache.enabled) {
      this.ohlcvCache.set(cacheKey, result, this.config.cache.ohlcvTtlSeconds);
    }
    return result;
  }

  public async getOrderBook(
    symbol: string,
    market: string,
    depth = 20,
  ): Promise<OrderBook> {
    validateSymbol(symbol);
    validateMarket(market);
    if (depth < 1 || depth > ORDER_BOOK_DEPTH_CAP) {
      throw new InvalidInputError(
        `depth must be 1..${ORDER_BOOK_DEPTH_CAP.toString()}, got ${depth.toString()}`,
      );
    }

    const chain = this.resolveChain(market);
    const cacheKey = `get_order_book|${market}|${symbol}|${depth.toString()}`;
    if (this.config.cache.enabled) {
      const hit = this.orderBookCache.get(cacheKey);
      if (hit !== undefined) {
        return hit;
      }
    }

    const result = await this.runChain(chain, (adapter) => {
      if (!adapter.capability.supportsOrderBook) {
        throw new AdapterFeatureNotSupportedError(
          `adapter ${adapter.adapterId} does not support order book`,
          adapter.adapterId,
        );
      }
      return adapter.getOrderBook(symbol, depth);
    });

    if (this.config.cache.enabled) {
      this.orderBookCache.set(
        cacheKey,
        result,
        this.config.cache.orderBookTtlSeconds,
      );
    }
    return result;
  }

  public async *subscribeTicker(
    symbol: string,
    market: string,
    options?: SubscribeTickerOptions,
  ): AsyncIterable<Ticker> {
    validateSymbol(symbol);
    validateMarket(market);
    const pollingFallback = options?.pollingFallback ?? true;
    const pollingIntervalSeconds = options?.pollingIntervalSeconds ?? 4.0;
    const signal = options?.signal;
    if (pollingIntervalSeconds < 1.0 || pollingIntervalSeconds > 3600.0) {
      throw new InvalidInputError(
        `pollingIntervalSeconds must be 1.0..3600.0, got ${pollingIntervalSeconds.toString()}`,
      );
    }

    const chain = this.resolveChain(market);
    let native: MarketDataAdapter | undefined;
    let polling: MarketDataAdapter | undefined;
    for (const adapter of chain) {
      if (adapter.capability.supportsNativeStreaming) {
        native = adapter;
        break;
      }
      if (pollingFallback && polling === undefined) {
        polling = adapter;
      }
    }

    if (native === undefined && polling === undefined) {
      throw new StreamingNotSupportedError(
        `no adapter in chain for market '${market}' supports streaming ` +
          "(neither native nor polling fallback applies)",
      );
    }

    if (native !== undefined) {
      yield* native.subscribeTicker(symbol, signal);
      return;
    }

    yield* pollTicker(
      polling as MarketDataAdapter,
      symbol,
      "USD",
      pollingIntervalSeconds,
      this.clock,
      3,
      signal,
    );
  }

  private resolveChain(market: string): readonly MarketDataAdapter[] {
    const chain = this.registry.chainFor(market);
    if (chain.length === 0) {
      throw new NoAdapterForMarketError(
        `no adapter chain registered for market '${market}'`,
      );
    }
    return chain;
  }

  private bucketFor(adapter: MarketDataAdapter): TokenBucket | undefined {
    if (!this.config.rateLimit.enabled) {
      return undefined;
    }
    let bucket = this.buckets.get(adapter.adapterId);
    if (bucket === undefined) {
      const rpm = Math.max(1, adapter.capability.rateLimitPerMinute);
      bucket = new TokenBucket(rpm, rpm / 60, this.clock);
      this.buckets.set(adapter.adapterId, bucket);
    }
    return bucket;
  }

  private async runChain<TResult>(
    chain: readonly MarketDataAdapter[],
    operation: (adapter: MarketDataAdapter) => Promise<TResult>,
  ): Promise<TResult> {
    if (chain.length === 1) {
      return this.invokeOne(chain[0] as MarketDataAdapter, operation);
    }

    const causes: MarketDataError[] = [];
    for (const adapter of chain) {
      try {
        return await this.invokeOne(adapter, operation);
      } catch (err) {
        if (err instanceof RateLimitedError) {
          causes.push(err);
          if (this.config.rateLimit.strategy === "bubble") {
            throw err;
          }
          continue;
        }
        if (
          err instanceof AdapterFeatureNotSupportedError ||
          err instanceof AdapterUnavailableError ||
          err instanceof InvalidIntervalError ||
          err instanceof MissingCredentialsError ||
          err instanceof SymbolNotFoundError ||
          err instanceof UnsupportedQuoteCurrencyError
        ) {
          causes.push(err);
          continue;
        }
        throw err;
      }
    }

    const codes = new Set(causes.map((c) => c.code));
    if (codes.size === 1) {
      const only = [...codes][0] as string;
      if (FATAL_PASSTHROUGH_CODES.has(only)) {
        const first = causes[0] as MarketDataError;
        throw errorForCode(
          only,
          `all ${causes.length.toString()} adapter(s) failed with ${only}`,
          first.adapterId,
        );
      }
    }
    throw new AllAdaptersFailedError(
      `all ${causes.length.toString()} adapter(s) failed`,
      causes,
    );
  }

  private invokeOne<TResult>(
    adapter: MarketDataAdapter,
    operation: (adapter: MarketDataAdapter) => Promise<TResult>,
  ): Promise<TResult> {
    const bucket = this.bucketFor(adapter);
    return runWithRetry(
      () => operation(adapter),
      this.config.rateLimit,
      this.clock,
      bucket,
    );
  }
}

function validateSymbol(symbol: string): void {
  if (symbol.length < 1 || symbol.length > SYMBOL_MAX_LENGTH) {
    throw new InvalidInputError(
      `symbol must be 1..${SYMBOL_MAX_LENGTH.toString()} chars, got length ${symbol.length.toString()}`,
    );
  }
  if (!ASCII_PRINTABLE.test(symbol)) {
    throw new InvalidInputError("symbol must be ASCII-printable");
  }
}

function validateMarket(market: string): void {
  if (market.length === 0 || !ASCII_PRINTABLE.test(market)) {
    throw new InvalidInputError("market must be a non-empty ASCII string");
  }
}

function validateQuoteCurrency(quoteCurrency: string): void {
  if (quoteCurrency.length < 2 || quoteCurrency.length > 8) {
    throw new InvalidInputError(
      `quoteCurrency must be 2..8 chars, got length ${quoteCurrency.length.toString()}`,
    );
  }
}

function rejectIfUnsupportedQuote(
  adapter: MarketDataAdapter,
  quoteCurrency: string,
): void {
  if (
    !quoteCurrencySupported(
      adapter.capability.supportedQuoteCurrencies,
      quoteCurrency,
    )
  ) {
    throw new UnsupportedQuoteCurrencyError(
      `adapter ${adapter.adapterId} does not support quote currency '${quoteCurrency}'`,
      adapter.adapterId,
    );
  }
}
