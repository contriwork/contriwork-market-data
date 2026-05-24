/**
 * CoinMarketCap Pro adapter. Only the latest-quote endpoint is wired in
 * v0.1.0 (free tier); historical OHLCV is paid-tier. Order book + streaming
 * are not provided on supported tiers.
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
  type Interval,
  type OrderBook,
  type SpotPrice,
  type Ticker,
} from "../types.js";

/** Options for {@link CoinMarketCapAdapter}. */
export interface CoinMarketCapOptions {
  readonly apiKey?: string;
  readonly apiKeyProvider?: ApiKeyProvider;
  readonly baseUrl?: string;
  readonly fetchFn?: FetchLike;
}

function optDv(value: unknown): Decimal | undefined {
  return value === undefined || value === null
    ? undefined
    : new Decimal(value as Decimal.Value);
}

/** CoinMarketCap crypto adapter (spot only in v0.1.0). */
export class CoinMarketCapAdapter implements MarketDataAdapter {
  public readonly adapterId = "coinmarketcap";
  public readonly capability: Capability;

  private readonly apiKey?: string;
  private readonly apiKeyProvider?: ApiKeyProvider;
  private readonly baseUrl: string;
  private readonly fetchFn: FetchLike;

  public constructor(options: CoinMarketCapOptions = {}) {
    if (options.apiKey !== undefined) {
      this.apiKey = options.apiKey;
    }
    if (options.apiKeyProvider !== undefined) {
      this.apiKeyProvider = options.apiKeyProvider;
    }
    this.baseUrl = (
      options.baseUrl ?? "https://pro-api.coinmarketcap.com"
    ).replace(/\/+$/, "");
    this.fetchFn = options.fetchFn ?? defaultFetch;
    this.capability = {
      supportedMarkets: ["crypto"],
      supportedIntervals: [],
      supportedQuoteCurrencies: "ANY",
      supportsOrderBook: false,
      supportsNativeStreaming: false,
      rateLimitPerMinute: 30,
      requiresAuth: true,
      tierOptions: [
        "basic",
        "hobbyist",
        "startup",
        "standard",
        "professional",
        "enterprise",
      ],
    };
  }

  public async getSpot(
    symbol: string,
    quoteCurrency: string,
    signal?: AbortSignal,
  ): Promise<SpotPrice> {
    const key = await resolveApiKey(
      this.adapterId,
      this.apiKey,
      this.apiKeyProvider,
      true,
      signal,
    );
    const url =
      `${this.baseUrl}/v2/cryptocurrency/quotes/latest` +
      queryString({ symbol, convert: quoteCurrency.toUpperCase() });
    const payload = (await getJson(
      this.fetchFn,
      this.adapterId,
      url,
      { "X-CMC_PRO_API_KEY": key as string, Accept: "application/json" },
      signal,
    )) as Record<string, unknown>;

    const data = payload["data"] as Record<string, unknown> | undefined;
    const entries = (data?.[symbol] ?? data?.[symbol.toUpperCase()]) as unknown;
    if (entries === undefined) {
      throw new SymbolNotFoundError(
        `coinmarketcap does not know symbol '${symbol}'`,
        this.adapterId,
      );
    }
    const entry = (Array.isArray(entries) ? entries[0] : entries) as Record<
      string,
      unknown
    >;
    const quoteBlock = entry["quote"] as Record<string, unknown> | undefined;
    const quote = quoteBlock?.[quoteCurrency.toUpperCase()] as
      | Record<string, unknown>
      | undefined;
    if (quote === undefined) {
      throw new AdapterUnavailableError(
        `coinmarketcap returned no quote for '${symbol}'/'${quoteCurrency}'`,
        this.adapterId,
      );
    }
    const lastUpdated = quote["last_updated"];
    const vol = optDv(quote["volume_24h"]);
    const chg = optDv(quote["percent_change_24h"]);
    const mcap = optDv(quote["market_cap"]);
    return {
      symbol,
      last: new Decimal(quote["price"] as Decimal.Value),
      quoteCurrency,
      timestamp:
        typeof lastUpdated === "string" ? new Date(lastUpdated) : new Date(),
      sourceAdapter: this.adapterId,
      extra: EMPTY_EXTRA,
      ...(vol !== undefined && { volume24h: vol }),
      ...(chg !== undefined && { change24hPct: chg }),
      ...(mcap !== undefined && { marketCap: mcap }),
    };
  }

  public getOhlcv(
    _symbol: string,
    _interval: Interval,
    _since: Date | undefined,
    _limit: number,
    _signal?: AbortSignal,
  ): Promise<readonly Candle[]> {
    return Promise.reject(
      new InvalidIntervalError(
        "coinmarketcap historical OHLCV is paid-tier and out of v0.1.0 scope",
        this.adapterId,
      ),
    );
  }

  public getOrderBook(
    _symbol: string,
    _depth: number,
    _signal?: AbortSignal,
  ): Promise<OrderBook> {
    return Promise.reject(
      new AdapterFeatureNotSupportedError(
        "coinmarketcap does not expose order book on the supported tiers",
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
