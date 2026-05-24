/**
 * Polygon.io adapter — US stocks + forex. `/v2/last/trade/{ticker}` +
 * aggregates. Free tier: 5 req/min.
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

const INTERVAL_MAP = new Map<Interval, { multiplier: number; span: string }>([
  [Interval.M1, { multiplier: 1, span: "minute" }],
  [Interval.M5, { multiplier: 5, span: "minute" }],
  [Interval.M15, { multiplier: 15, span: "minute" }],
  [Interval.M30, { multiplier: 30, span: "minute" }],
  [Interval.H1, { multiplier: 1, span: "hour" }],
  [Interval.H4, { multiplier: 4, span: "hour" }],
  [Interval.D1, { multiplier: 1, span: "day" }],
  [Interval.W1, { multiplier: 1, span: "week" }],
  [Interval.MN1, { multiplier: 1, span: "month" }],
]);

/** Options for {@link PolygonIOAdapter}. */
export interface PolygonIOOptions {
  readonly apiKey?: string;
  readonly apiKeyProvider?: ApiKeyProvider;
  readonly baseUrl?: string;
  readonly fetchFn?: FetchLike;
}

function ymd(date: Date): string {
  return date.toISOString().slice(0, 10);
}

/** Polygon.io stocks + forex adapter. */
export class PolygonIOAdapter implements MarketDataAdapter {
  public readonly adapterId = "polygon-io";
  public readonly capability: Capability;

  private readonly apiKey?: string;
  private readonly apiKeyProvider?: ApiKeyProvider;
  private readonly baseUrl: string;
  private readonly fetchFn: FetchLike;

  public constructor(options: PolygonIOOptions = {}) {
    if (options.apiKey !== undefined) {
      this.apiKey = options.apiKey;
    }
    if (options.apiKeyProvider !== undefined) {
      this.apiKeyProvider = options.apiKeyProvider;
    }
    this.baseUrl = (options.baseUrl ?? "https://api.polygon.io").replace(
      /\/+$/,
      "",
    );
    this.fetchFn = options.fetchFn ?? defaultFetch;
    this.capability = {
      supportedMarkets: ["stocks_us", "forex"],
      supportedIntervals: [...INTERVAL_MAP.keys()],
      supportedQuoteCurrencies: ["USD"],
      supportsOrderBook: false,
      supportsNativeStreaming: false,
      rateLimitPerMinute: 5,
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

  private async get(
    path: string,
    query: Record<string, string | number | undefined>,
    signal?: AbortSignal,
  ): Promise<unknown> {
    try {
      return await getJson(
        this.fetchFn,
        this.adapterId,
        this.baseUrl + path + queryString(query),
        undefined,
        signal,
      );
    } catch (err) {
      if (
        err instanceof AdapterUnavailableError &&
        err.message.includes("HTTP 404")
      ) {
        throw new SymbolNotFoundError(
          `polygon-io does not know ticker (404): ${path}`,
          this.adapterId,
        );
      }
      throw err;
    }
  }

  public async getSpot(
    symbol: string,
    quoteCurrency: string,
    signal?: AbortSignal,
  ): Promise<SpotPrice> {
    const key = await this.key(signal);
    const root = (await this.get(
      `/v2/last/trade/${symbol}`,
      { apiKey: key },
      signal,
    )) as Record<string, unknown>;
    const results = root["results"] as Record<string, unknown> | undefined;
    if (results === undefined) {
      throw new AdapterUnavailableError(
        "polygon-io trade returned unexpected payload",
        this.adapterId,
      );
    }
    if (results["p"] === undefined) {
      throw new SymbolNotFoundError(
        `polygon-io returned no last trade for '${symbol}'`,
        this.adapterId,
      );
    }
    const tsNs = Number(results["t"] ?? 0);
    const vol =
      results["s"] !== undefined
        ? new Decimal(results["s"] as Decimal.Value)
        : undefined;
    return {
      symbol,
      last: new Decimal(results["p"] as Decimal.Value),
      quoteCurrency,
      timestamp: new Date(tsNs / 1_000_000),
      sourceAdapter: this.adapterId,
      extra: EMPTY_EXTRA,
      ...(vol !== undefined && { volume24h: vol }),
    };
  }

  public async getOhlcv(
    symbol: string,
    interval: Interval,
    since: Date | undefined,
    limit: number,
    signal?: AbortSignal,
  ): Promise<readonly Candle[]> {
    const spec = INTERVAL_MAP.get(interval);
    if (spec === undefined) {
      throw new InvalidIntervalError(
        `polygon-io does not support interval ${interval}`,
        this.adapterId,
      );
    }
    const key = await this.key(signal);
    const end = new Date();
    const start = since ?? new Date(end.getTime() - 365 * 24 * 60 * 60 * 1000);
    const path = `/v2/aggs/ticker/${symbol}/range/${String(spec.multiplier)}/${spec.span}/${ymd(start)}/${ymd(end)}`;
    const root = (await this.get(
      path,
      { apiKey: key, limit: Math.min(limit, 5000) },
      signal,
    )) as Record<string, unknown>;
    const results =
      (root["results"] as Record<string, unknown>[] | undefined) ?? [];
    return results.slice(0, limit).map((row) => ({
      timestamp: new Date(Number(row["t"] ?? 0)),
      open: new Decimal(row["o"] as Decimal.Value),
      high: new Decimal(row["h"] as Decimal.Value),
      low: new Decimal(row["l"] as Decimal.Value),
      close: new Decimal(row["c"] as Decimal.Value),
      volume:
        row["v"] !== undefined
          ? new Decimal(row["v"] as Decimal.Value)
          : new Decimal(0),
      ...(row["n"] !== undefined ? { tradeCount: Number(row["n"]) } : {}),
      extra: EMPTY_EXTRA,
    }));
  }

  public getOrderBook(
    _symbol: string,
    _depth: number,
    _signal?: AbortSignal,
  ): Promise<OrderBook> {
    return Promise.reject(
      new AdapterFeatureNotSupportedError(
        "polygon-io order book requires the L2 paid tier and is out of v0.1.0 scope",
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
