/**
 * Finnhub adapter — US stocks. `/api/v1/quote` + `/api/v1/stock/candle`.
 * Free tier ~60 req/min.
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

const RESOLUTION_MAP = new Map<Interval, string>([
  [Interval.M1, "1"],
  [Interval.M5, "5"],
  [Interval.M15, "15"],
  [Interval.M30, "30"],
  [Interval.H1, "60"],
  [Interval.D1, "D"],
  [Interval.W1, "W"],
  [Interval.MN1, "M"],
]);

/** Options for {@link FinnhubAdapter}. */
export interface FinnhubOptions {
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

/** Finnhub US-stocks adapter. */
export class FinnhubAdapter implements MarketDataAdapter {
  public readonly adapterId = "finnhub";
  public readonly capability: Capability;

  private readonly apiKey?: string;
  private readonly apiKeyProvider?: ApiKeyProvider;
  private readonly baseUrl: string;
  private readonly fetchFn: FetchLike;

  public constructor(options: FinnhubOptions = {}) {
    if (options.apiKey !== undefined) {
      this.apiKey = options.apiKey;
    }
    if (options.apiKeyProvider !== undefined) {
      this.apiKeyProvider = options.apiKeyProvider;
    }
    this.baseUrl = (options.baseUrl ?? "https://finnhub.io").replace(
      /\/+$/,
      "",
    );
    this.fetchFn = options.fetchFn ?? defaultFetch;
    this.capability = {
      supportedMarkets: ["stocks_us"],
      supportedIntervals: [...RESOLUTION_MAP.keys()],
      supportedQuoteCurrencies: ["USD"],
      supportsOrderBook: false,
      supportsNativeStreaming: false,
      rateLimitPerMinute: 60,
      requiresAuth: true,
    };
  }

  private async key(signal?: AbortSignal): Promise<string> {
    return (await resolveApiKey(
      this.adapterId,
      this.apiKey,
      this.apiKeyProvider,
      true,
      signal,
    )) as string;
  }

  public async getSpot(
    symbol: string,
    quoteCurrency: string,
    signal?: AbortSignal,
  ): Promise<SpotPrice> {
    const key = await this.key(signal);
    const url =
      `${this.baseUrl}/api/v1/quote` + queryString({ symbol, token: key });
    const root = (await getJson(
      this.fetchFn,
      this.adapterId,
      url,
      undefined,
      signal,
    )) as Record<string, unknown>;
    if (root["c"] === undefined || root["c"] === 0) {
      throw new SymbolNotFoundError(
        `finnhub has no quote for '${symbol}'`,
        this.adapterId,
      );
    }
    const high = optDv(root["h"]);
    const low = optDv(root["l"]);
    const prev = optDv(root["pc"]);
    const chg = optDv(root["dp"]);
    return {
      symbol,
      last: new Decimal(root["c"] as Decimal.Value),
      quoteCurrency,
      timestamp: new Date(Number(root["t"] ?? 0) * 1000),
      sourceAdapter: this.adapterId,
      extra: EMPTY_EXTRA,
      ...(high !== undefined && { high24h: high }),
      ...(low !== undefined && { low24h: low }),
      ...(prev !== undefined && { previousClose: prev }),
      ...(chg !== undefined && { change24hPct: chg }),
    };
  }

  public async getOhlcv(
    symbol: string,
    interval: Interval,
    since: Date | undefined,
    limit: number,
    signal?: AbortSignal,
  ): Promise<readonly Candle[]> {
    const resolution = RESOLUTION_MAP.get(interval);
    if (resolution === undefined) {
      throw new InvalidIntervalError(
        `finnhub does not support interval ${interval}`,
        this.adapterId,
      );
    }
    const key = await this.key(signal);
    const to = Math.floor(Date.now() / 1000);
    const from =
      since !== undefined
        ? Math.floor(since.getTime() / 1000)
        : to - 60 * 60 * 24 * 30;
    const url =
      `${this.baseUrl}/api/v1/stock/candle` +
      queryString({ symbol, resolution, from, to, token: key });
    const root = (await getJson(
      this.fetchFn,
      this.adapterId,
      url,
      undefined,
      signal,
    )) as Record<string, unknown>;
    if (root["s"] !== "ok") {
      throw new SymbolNotFoundError(
        `finnhub has no candle data for '${symbol}'`,
        this.adapterId,
      );
    }
    const t = root["t"] as number[];
    const o = root["o"] as number[];
    const h = root["h"] as number[];
    const l = root["l"] as number[];
    const c = root["c"] as number[];
    const v = root["v"] as number[];
    const count = Math.min(t.length, limit);
    const candles: Candle[] = [];
    for (let i = 0; i < count; i++) {
      candles.push({
        timestamp: new Date((t[i] as number) * 1000),
        open: new Decimal(o[i] as Decimal.Value),
        high: new Decimal(h[i] as Decimal.Value),
        low: new Decimal(l[i] as Decimal.Value),
        close: new Decimal(c[i] as Decimal.Value),
        volume: new Decimal(v[i] as Decimal.Value),
        extra: EMPTY_EXTRA,
      });
    }
    return candles;
  }

  public getOrderBook(
    _symbol: string,
    _depth: number,
    _signal?: AbortSignal,
  ): Promise<OrderBook> {
    return Promise.reject(
      new AdapterFeatureNotSupportedError(
        "finnhub does not expose order book on the free tier",
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
