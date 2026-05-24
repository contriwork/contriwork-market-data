/**
 * Binance public-data adapter — no API key. `/api/v3/ticker/24hr`,
 * `/api/v3/klines`, `/api/v3/depth`. Symbols are pairs (`BTCUSDT`).
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

const INTERVAL_MAP = new Map<Interval, string>([
  [Interval.M1, "1m"],
  [Interval.M5, "5m"],
  [Interval.M15, "15m"],
  [Interval.M30, "30m"],
  [Interval.H1, "1h"],
  [Interval.H4, "4h"],
  [Interval.D1, "1d"],
  [Interval.W1, "1w"],
  [Interval.MN1, "1M"],
]);

/** Options for {@link BinancePublicAdapter}. */
export interface BinancePublicOptions {
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

/** Binance public crypto adapter. */
export class BinancePublicAdapter implements MarketDataAdapter {
  public readonly adapterId = "binance-public";
  public readonly capability: Capability;

  private readonly baseUrl: string;
  private readonly fetchFn: FetchLike;

  public constructor(options: BinancePublicOptions = {}) {
    this.baseUrl = (options.baseUrl ?? "https://api.binance.com").replace(
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
      rateLimitPerMinute: 1000,
      requiresAuth: false,
    };
  }

  public async getSpot(
    symbol: string,
    quoteCurrency: string,
    signal?: AbortSignal,
  ): Promise<SpotPrice> {
    const url = `${this.baseUrl}/api/v3/ticker/24hr` + queryString({ symbol });
    const root = (await getJson(
      this.fetchFn,
      this.adapterId,
      url,
      undefined,
      signal,
    )) as Record<string, unknown>;
    if (root["code"] !== undefined) {
      if (root["code"] === -1121) {
        throw new SymbolNotFoundError(
          `binance does not know symbol '${symbol}'`,
          this.adapterId,
        );
      }
      throw new AdapterUnavailableError(
        `binance error ${String(root["code"])}: ${String(root["msg"])}`,
        this.adapterId,
      );
    }
    const bid = optDv(root["bidPrice"]);
    const ask = optDv(root["askPrice"]);
    const high = optDv(root["highPrice"]);
    const low = optDv(root["lowPrice"]);
    const vol = optDv(root["quoteVolume"]);
    const chg = optDv(root["priceChangePercent"]);
    const prev = optDv(root["prevClosePrice"]);
    return {
      symbol,
      last: dv(root["lastPrice"]),
      quoteCurrency,
      timestamp: new Date(Number(root["closeTime"] ?? 0)),
      sourceAdapter: this.adapterId,
      extra: EMPTY_EXTRA,
      ...(bid !== undefined && { bid }),
      ...(ask !== undefined && { ask }),
      ...(high !== undefined && { high24h: high }),
      ...(low !== undefined && { low24h: low }),
      ...(vol !== undefined && { volume24h: vol }),
      ...(chg !== undefined && { change24hPct: chg }),
      ...(prev !== undefined && { previousClose: prev }),
    };
  }

  public async getOhlcv(
    symbol: string,
    interval: Interval,
    since: Date | undefined,
    limit: number,
    signal?: AbortSignal,
  ): Promise<readonly Candle[]> {
    const binInterval = INTERVAL_MAP.get(interval);
    if (binInterval === undefined) {
      throw new InvalidIntervalError(
        `binance does not support interval ${interval}`,
        this.adapterId,
      );
    }
    const url =
      `${this.baseUrl}/api/v3/klines` +
      queryString({
        symbol,
        interval: binInterval,
        limit: Math.min(limit, 1000),
        ...(since !== undefined ? { startTime: since.getTime() } : {}),
      });
    const payload = await getJson(
      this.fetchFn,
      this.adapterId,
      url,
      undefined,
      signal,
    );
    if (!Array.isArray(payload)) {
      const obj = payload as Record<string, unknown>;
      if (obj["code"] === -1121) {
        throw new SymbolNotFoundError(
          `binance does not know symbol '${symbol}'`,
          this.adapterId,
        );
      }
      throw new AdapterUnavailableError(
        "binance klines returned unexpected payload",
        this.adapterId,
      );
    }
    return (payload as unknown[][]).map((row) => ({
      timestamp: new Date(Number(row[0])),
      open: dv(row[1]),
      high: dv(row[2]),
      low: dv(row[3]),
      close: dv(row[4]),
      volume: dv(row[5]),
      quoteVolume: dv(row[7]),
      tradeCount: Number(row[8]),
      extra: EMPTY_EXTRA,
    }));
  }

  public async getOrderBook(
    symbol: string,
    depth: number,
    signal?: AbortSignal,
  ): Promise<OrderBook> {
    const binLimit = [5, 10, 20, 50, 100].find((c) => c >= depth) ?? 100;
    const url =
      `${this.baseUrl}/api/v3/depth` + queryString({ symbol, limit: binLimit });
    const root = (await getJson(
      this.fetchFn,
      this.adapterId,
      url,
      undefined,
      signal,
    )) as Record<string, unknown>;
    if (root["code"] === -1121) {
      throw new SymbolNotFoundError(
        `binance does not know symbol '${symbol}'`,
        this.adapterId,
      );
    }
    const parse = (rows: unknown): BookLevel[] =>
      (rows as unknown[][])
        .slice(0, depth)
        .map((p) => ({ price: dv(p[0]), size: dv(p[1]) }));
    const bids = parse(root["bids"]).sort((a, b) =>
      b.price.comparedTo(a.price),
    );
    const asks = parse(root["asks"]).sort((a, b) =>
      a.price.comparedTo(b.price),
    );
    return {
      symbol,
      bids,
      asks,
      timestamp: new Date(),
      sourceAdapter: this.adapterId,
      extra: EMPTY_EXTRA,
      ...(root["lastUpdateId"] !== undefined
        ? { sequence: Number(root["lastUpdateId"]) }
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
