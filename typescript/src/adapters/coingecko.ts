/**
 * CoinGecko REST adapter. `/simple/price` for spot, `/coins/{id}/ohlc` for
 * candles. Order book is not on the public REST tier.
 */
import { Decimal } from "decimal.js";
import type { ApiKeyProvider, MarketDataAdapter } from "../adapter.js";
import { resolveApiKey } from "../internal/credentials.js";
import {
  type FetchLike,
  defaultFetch,
  getJson,
  queryString,
} from "../internal/http.js";
import { streamingNotSupported } from "../internal/streaming.js";
import {
  AdapterFeatureNotSupportedError,
  AdapterUnavailableError,
  InvalidIntervalError,
  SymbolNotFoundError,
} from "../errors.js";
import {
  type Candle,
  type Capability,
  EMPTY_EXTRA,
  Interval,
  type OrderBook,
  type SpotPrice,
  type Ticker,
} from "../types.js";

const TIER_BASE_URL: Record<string, string> = {
  demo: "https://api.coingecko.com/api/v3",
  free: "https://api.coingecko.com/api/v3",
  pro: "https://pro-api.coingecko.com/api/v3",
};

const TIER_AUTH_HEADER: Record<string, string | null> = {
  demo: "x-cg-demo-api-key",
  free: null,
  pro: "x-cg-pro-api-key",
};

const DAYS_FOR_INTERVAL = new Map<Interval, string>([
  [Interval.M30, "1"],
  [Interval.H1, "1"],
  [Interval.H4, "7"],
  [Interval.D1, "30"],
  [Interval.W1, "365"],
]);

const RATE_LIMIT_BY_TIER: Record<string, number> = {
  free: 10,
  demo: 30,
  pro: 500,
};

/** Options for {@link CoinGeckoAdapter}. */
export interface CoinGeckoOptions {
  readonly apiKey?: string;
  readonly apiKeyProvider?: ApiKeyProvider;
  readonly tier?: "demo" | "free" | "pro";
  readonly baseUrl?: string;
  readonly fetchFn?: FetchLike;
}

function optDecimal(value: unknown): Decimal | undefined {
  return value === undefined || value === null
    ? undefined
    : new Decimal(value as Decimal.Value);
}

/** CoinGecko crypto market-data adapter. */
export class CoinGeckoAdapter implements MarketDataAdapter {
  public readonly adapterId = "coingecko";
  public readonly capability: Capability;

  private readonly apiKey?: string;
  private readonly apiKeyProvider?: ApiKeyProvider;
  private readonly tier: string;
  private readonly baseUrl: string;
  private readonly fetchFn: FetchLike;

  public constructor(options: CoinGeckoOptions = {}) {
    this.tier = options.tier ?? "demo";
    const base = TIER_BASE_URL[this.tier];
    if (base === undefined) {
      throw new Error(`unknown tier '${this.tier}'; expected demo/free/pro`);
    }
    if (options.apiKey !== undefined) {
      this.apiKey = options.apiKey;
    }
    if (options.apiKeyProvider !== undefined) {
      this.apiKeyProvider = options.apiKeyProvider;
    }
    this.baseUrl = (options.baseUrl ?? base).replace(/\/+$/, "");
    this.fetchFn = options.fetchFn ?? defaultFetch;
    this.capability = {
      supportedMarkets: ["crypto"],
      supportedIntervals: [...DAYS_FOR_INTERVAL.keys()],
      supportedQuoteCurrencies: "ANY",
      supportsOrderBook: false,
      supportsNativeStreaming: false,
      rateLimitPerMinute: RATE_LIMIT_BY_TIER[this.tier] ?? 30,
      requiresAuth: this.tier === "pro",
      tierOptions: ["demo", "free", "pro"],
    };
  }

  private async headers(
    signal?: AbortSignal,
  ): Promise<Record<string, string> | undefined> {
    const headerName = TIER_AUTH_HEADER[this.tier];
    if (headerName === null || headerName === undefined) {
      return undefined;
    }
    const key = await resolveApiKey(
      this.adapterId,
      this.apiKey,
      this.apiKeyProvider,
      this.capability.requiresAuth,
      signal,
    );
    return key !== undefined && key !== "" ? { [headerName]: key } : undefined;
  }

  public async getSpot(
    symbol: string,
    quoteCurrency: string,
    signal?: AbortSignal,
  ): Promise<SpotPrice> {
    const vs = quoteCurrency.toLowerCase();
    const url =
      `${this.baseUrl}/simple/price` +
      queryString({
        ids: symbol,
        vs_currencies: vs,
        include_24hr_change: "true",
        include_24hr_vol: "true",
        include_market_cap: "true",
        include_last_updated_at: "true",
        precision: "full",
      });
    const payload = await getJson(
      this.fetchFn,
      this.adapterId,
      url,
      await this.headers(signal),
      signal,
    );
    const body = (payload as Record<string, Record<string, unknown>>)[symbol];
    if (body === undefined) {
      throw new SymbolNotFoundError(
        `coingecko has no spot for '${symbol}'`,
        this.adapterId,
      );
    }
    const price = body[vs];
    if (price === undefined) {
      throw new AdapterUnavailableError(
        `coingecko returned unexpected payload for '${symbol}'`,
        this.adapterId,
      );
    }
    const updatedAt = body["last_updated_at"];
    const change = optDecimal(body[`${vs}_24h_change`]);
    const volume = optDecimal(body[`${vs}_24h_vol`]);
    const marketCap = optDecimal(body[`${vs}_market_cap`]);
    return {
      symbol,
      last: new Decimal(price as Decimal.Value),
      quoteCurrency,
      timestamp:
        updatedAt !== undefined
          ? new Date(Number(updatedAt) * 1000)
          : new Date(),
      sourceAdapter: this.adapterId,
      extra: EMPTY_EXTRA,
      ...(change !== undefined && { change24hPct: change }),
      ...(volume !== undefined && { volume24h: volume }),
      ...(marketCap !== undefined && { marketCap }),
    };
  }

  public async getOhlcv(
    symbol: string,
    interval: Interval,
    since: Date | undefined,
    limit: number,
    signal?: AbortSignal,
  ): Promise<readonly Candle[]> {
    const days = DAYS_FOR_INTERVAL.get(interval);
    if (days === undefined) {
      throw new InvalidIntervalError(
        `coingecko does not support interval ${interval}`,
        this.adapterId,
      );
    }
    const url =
      `${this.baseUrl}/coins/${symbol}/ohlc` +
      queryString({ vs_currency: "usd", days });
    const payload = await getJson(
      this.fetchFn,
      this.adapterId,
      url,
      await this.headers(signal),
      signal,
    );
    if (!Array.isArray(payload)) {
      throw new SymbolNotFoundError(
        `coingecko has no ohlcv for '${symbol}'`,
        this.adapterId,
      );
    }
    const candles: Candle[] = [];
    for (const row of payload as unknown[][]) {
      const ts = new Date(Number(row[0]));
      if (since !== undefined && ts.getTime() < since.getTime()) {
        continue;
      }
      candles.push({
        timestamp: ts,
        open: new Decimal(row[1] as Decimal.Value),
        high: new Decimal(row[2] as Decimal.Value),
        low: new Decimal(row[3] as Decimal.Value),
        close: new Decimal(row[4] as Decimal.Value),
        volume: new Decimal(0),
        extra: EMPTY_EXTRA,
      });
      if (candles.length >= limit) {
        break;
      }
    }
    candles.sort((a, b) => a.timestamp.getTime() - b.timestamp.getTime());
    return candles;
  }

  public getOrderBook(
    _symbol: string,
    _depth: number,
    _signal?: AbortSignal,
  ): Promise<OrderBook> {
    return Promise.reject(
      new AdapterFeatureNotSupportedError(
        "coingecko does not support order book",
        this.adapterId,
      ),
    );
  }

  public subscribeTicker(
    _symbol: string,
    _signal?: AbortSignal,
  ): AsyncIterable<Ticker> {
    return streamingNotSupported(this.adapterId);
  }
}
