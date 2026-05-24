/**
 * Kraken public-data adapter. `/0/public/Ticker`, `/0/public/OHLC`,
 * `/0/public/Depth`. Symbols use Kraken pair notation (`XXBTZUSD`).
 */
import { Decimal } from "decimal.js";
import type { MarketDataAdapter } from "../adapter.js";
import {
  type FetchLike,
  defaultFetch,
  getJson,
  queryString,
} from "../internal/http.js";
import { streamingNotSupported } from "../internal/streaming.js";
import {
  AdapterUnavailableError,
  InvalidIntervalError,
  SymbolNotFoundError,
} from "../errors.js";
import {
  type BookLevel,
  type Candle,
  type Capability,
  EMPTY_EXTRA,
  Interval,
  type OrderBook,
  type SpotPrice,
  type Ticker,
} from "../types.js";

const INTERVAL_MAP = new Map<Interval, number>([
  [Interval.M1, 1],
  [Interval.M5, 5],
  [Interval.M15, 15],
  [Interval.M30, 30],
  [Interval.H1, 60],
  [Interval.H4, 240],
  [Interval.D1, 1440],
  [Interval.W1, 10080],
  [Interval.MN1, 21600],
]);

/** Options for {@link KrakenAdapter}. */
export interface KrakenOptions {
  readonly baseUrl?: string;
  readonly fetchFn?: FetchLike;
}

function dv(value: unknown): Decimal {
  return new Decimal(value as Decimal.Value);
}

/** Kraken public crypto adapter. */
export class KrakenAdapter implements MarketDataAdapter {
  public readonly adapterId = "kraken";
  public readonly capability: Capability;

  private readonly baseUrl: string;
  private readonly fetchFn: FetchLike;

  public constructor(options: KrakenOptions = {}) {
    this.baseUrl = (options.baseUrl ?? "https://api.kraken.com").replace(
      /\/+$/,
      "",
    );
    this.fetchFn = options.fetchFn ?? defaultFetch;
    this.capability = {
      supportedMarkets: ["crypto"],
      supportedIntervals: [...INTERVAL_MAP.keys()],
      supportedQuoteCurrencies: "ANY",
      supportsOrderBook: true,
      supportsNativeStreaming: false,
      rateLimitPerMinute: 60,
      requiresAuth: false,
    };
  }

  private checkErrors(
    payload: unknown,
    symbol: string,
  ): Record<string, unknown> {
    const root = payload as Record<string, unknown>;
    const errors = (root["error"] as string[] | undefined) ?? [];
    if (errors.length > 0) {
      const joined = errors.join(";");
      if (joined.includes("Unknown asset")) {
        throw new SymbolNotFoundError(
          `kraken does not know symbol '${symbol}'`,
          this.adapterId,
        );
      }
      throw new AdapterUnavailableError(
        `kraken error: ${joined}`,
        this.adapterId,
      );
    }
    const result = root["result"];
    if (typeof result !== "object" || result === null) {
      throw new AdapterUnavailableError(
        "kraken returned no result block",
        this.adapterId,
      );
    }
    return result as Record<string, unknown>;
  }

  public async getSpot(
    symbol: string,
    quoteCurrency: string,
    signal?: AbortSignal,
  ): Promise<SpotPrice> {
    const url =
      `${this.baseUrl}/0/public/Ticker` + queryString({ pair: symbol });
    const result = this.checkErrors(
      await getJson(this.fetchFn, this.adapterId, url, undefined, signal),
      symbol,
    );
    const first = Object.values(result)[0] as
      | Record<string, unknown>
      | undefined;
    if (first === undefined) {
      throw new SymbolNotFoundError(
        `kraken returned empty result for '${symbol}'`,
        this.adapterId,
      );
    }
    const c = first["c"] as unknown[];
    const b = first["b"] as unknown[];
    const a = first["a"] as unknown[];
    const h = first["h"] as unknown[];
    const l = first["l"] as unknown[];
    const v = first["v"] as unknown[];
    return {
      symbol,
      last: dv(c[0]),
      quoteCurrency,
      timestamp: new Date(),
      sourceAdapter: this.adapterId,
      extra: EMPTY_EXTRA,
      bid: dv(b[0]),
      ask: dv(a[0]),
      high24h: dv(h[1]),
      low24h: dv(l[1]),
      volume24h: dv(v[1]),
    };
  }

  public async getOhlcv(
    symbol: string,
    interval: Interval,
    since: Date | undefined,
    limit: number,
    signal?: AbortSignal,
  ): Promise<readonly Candle[]> {
    const minutes = INTERVAL_MAP.get(interval);
    if (minutes === undefined) {
      throw new InvalidIntervalError(
        `kraken does not support interval ${interval}`,
        this.adapterId,
      );
    }
    const url =
      `${this.baseUrl}/0/public/OHLC` +
      queryString({
        pair: symbol,
        interval: minutes,
        ...(since !== undefined
          ? { since: Math.floor(since.getTime() / 1000) }
          : {}),
      });
    const result = this.checkErrors(
      await getJson(this.fetchFn, this.adapterId, url, undefined, signal),
      symbol,
    );
    let rows: unknown[] | undefined;
    for (const [key, value] of Object.entries(result)) {
      if (key !== "last" && Array.isArray(value)) {
        rows = value;
        break;
      }
    }
    if (rows === undefined || rows.length === 0) {
      throw new SymbolNotFoundError(
        `kraken returned no candles for '${symbol}'`,
        this.adapterId,
      );
    }
    return (rows as unknown[][]).slice(0, limit).map((row) => ({
      timestamp: new Date(Number(row[0]) * 1000),
      open: dv(row[1]),
      high: dv(row[2]),
      low: dv(row[3]),
      close: dv(row[4]),
      volume: dv(row[6]),
      tradeCount: Number(row[7]),
      extra: EMPTY_EXTRA,
    }));
  }

  public async getOrderBook(
    symbol: string,
    depth: number,
    signal?: AbortSignal,
  ): Promise<OrderBook> {
    const url =
      `${this.baseUrl}/0/public/Depth` +
      queryString({ pair: symbol, count: Math.min(depth, 500) });
    const result = this.checkErrors(
      await getJson(this.fetchFn, this.adapterId, url, undefined, signal),
      symbol,
    );
    const first = Object.values(result)[0] as
      | Record<string, unknown>
      | undefined;
    if (first === undefined) {
      throw new SymbolNotFoundError(
        `kraken returned no depth for '${symbol}'`,
        this.adapterId,
      );
    }
    const parse = (rows: unknown): BookLevel[] =>
      (rows as unknown[][])
        .slice(0, depth)
        .map((p) => ({ price: dv(p[0]), size: dv(p[1]) }));
    return {
      symbol,
      bids: parse(first["bids"]).sort((x, y) => y.price.comparedTo(x.price)),
      asks: parse(first["asks"]).sort((x, y) => x.price.comparedTo(y.price)),
      timestamp: new Date(),
      sourceAdapter: this.adapterId,
      extra: EMPTY_EXTRA,
    };
  }

  public subscribeTicker(
    _symbol: string,
    _signal?: AbortSignal,
  ): AsyncIterable<Ticker> {
    return streamingNotSupported(this.adapterId);
  }
}
