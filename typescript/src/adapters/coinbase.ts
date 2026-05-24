/**
 * Coinbase Exchange public-data adapter. `/products/{id}/ticker` + `/stats`
 * + `/candles` + `/book`. Symbols use product IDs (`BTC-USD`).
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
  [Interval.M1, 60],
  [Interval.M5, 300],
  [Interval.M15, 900],
  [Interval.H1, 3600],
  [Interval.H4, 21600],
  [Interval.D1, 86400],
]);

/** Options for {@link CoinbaseAdapter}. */
export interface CoinbaseOptions {
  readonly baseUrl?: string;
  readonly fetchFn?: FetchLike;
}

function dv(value: unknown): Decimal {
  return new Decimal(value as Decimal.Value);
}

function optDv(value: unknown): Decimal | undefined {
  return value === undefined || value === null || value === ""
    ? undefined
    : dv(value);
}

/** Coinbase Exchange crypto adapter. */
export class CoinbaseAdapter implements MarketDataAdapter {
  public readonly adapterId = "coinbase";
  public readonly capability: Capability;

  private readonly baseUrl: string;
  private readonly fetchFn: FetchLike;

  public constructor(options: CoinbaseOptions = {}) {
    this.baseUrl = (
      options.baseUrl ?? "https://api.exchange.coinbase.com"
    ).replace(/\/+$/, "");
    this.fetchFn = options.fetchFn ?? defaultFetch;
    this.capability = {
      supportedMarkets: ["crypto"],
      supportedIntervals: [...INTERVAL_MAP.keys()],
      supportedQuoteCurrencies: "ANY",
      supportsOrderBook: true,
      supportsNativeStreaming: false,
      rateLimitPerMinute: 600,
      requiresAuth: false,
    };
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
        { Accept: "application/json" },
        signal,
      );
    } catch (err) {
      if (
        err instanceof AdapterUnavailableError &&
        err.message.includes("HTTP 404")
      ) {
        throw new SymbolNotFoundError(
          `coinbase does not know product (404): ${path}`,
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
    const ticker = (await this.get(
      `/products/${symbol}/ticker`,
      {},
      signal,
    )) as Record<string, unknown>;
    const stats = (await this.get(
      `/products/${symbol}/stats`,
      {},
      signal,
    )) as Record<string, unknown>;
    if (ticker["price"] === undefined) {
      throw new AdapterUnavailableError(
        "coinbase ticker returned unexpected payload",
        this.adapterId,
      );
    }
    const time = ticker["time"];
    const bid = optDv(ticker["bid"]);
    const ask = optDv(ticker["ask"]);
    const high = optDv(stats["high"]);
    const low = optDv(stats["low"]);
    const vol = optDv(stats["volume"]);
    return {
      symbol,
      last: dv(ticker["price"]),
      quoteCurrency,
      timestamp: typeof time === "string" ? new Date(time) : new Date(),
      sourceAdapter: this.adapterId,
      extra: EMPTY_EXTRA,
      ...(bid !== undefined && { bid }),
      ...(ask !== undefined && { ask }),
      ...(high !== undefined && { high24h: high }),
      ...(low !== undefined && { low24h: low }),
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
    const granularity = INTERVAL_MAP.get(interval);
    if (granularity === undefined) {
      throw new InvalidIntervalError(
        `coinbase does not support interval ${interval}`,
        this.adapterId,
      );
    }
    const payload = await this.get(
      `/products/${symbol}/candles`,
      {
        granularity,
        ...(since !== undefined ? { start: since.toISOString() } : {}),
      },
      signal,
    );
    if (!Array.isArray(payload)) {
      throw new AdapterUnavailableError(
        "coinbase candles returned unexpected payload",
        this.adapterId,
      );
    }
    // Coinbase returns descending; reverse for the ascending contract.
    const rows = (payload as unknown[][]).slice(0, limit).reverse();
    return rows.map((row) => ({
      timestamp: new Date(Number(row[0]) * 1000),
      open: dv(row[3]),
      high: dv(row[2]),
      low: dv(row[1]),
      close: dv(row[4]),
      volume: dv(row[5]),
      extra: EMPTY_EXTRA,
    }));
  }

  public async getOrderBook(
    symbol: string,
    depth: number,
    signal?: AbortSignal,
  ): Promise<OrderBook> {
    const root = (await this.get(
      `/products/${symbol}/book`,
      { level: 2 },
      signal,
    )) as Record<string, unknown>;
    const parse = (rows: unknown): BookLevel[] =>
      (rows as unknown[][])
        .slice(0, depth)
        .map((p) => ({ price: dv(p[0]), size: dv(p[1]) }));
    return {
      symbol,
      bids: parse(root["bids"]).sort((a, b) => b.price.comparedTo(a.price)),
      asks: parse(root["asks"]).sort((a, b) => a.price.comparedTo(b.price)),
      timestamp: new Date(),
      sourceAdapter: this.adapterId,
      extra: EMPTY_EXTRA,
      ...(root["sequence"] !== undefined
        ? { sequence: Number(root["sequence"]) }
        : {}),
    };
  }

  public subscribeTicker(
    _symbol: string,
    _signal?: AbortSignal,
  ): AsyncIterable<Ticker> {
    return streamingNotSupported(this.adapterId);
  }
}
