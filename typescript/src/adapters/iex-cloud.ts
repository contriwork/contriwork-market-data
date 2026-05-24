/**
 * IEX Cloud adapter — preserves the v1 path scheme (the service was sunset
 * in August 2024; compatible mirrors can swap baseUrl). `/stable/stock/{s}/quote`
 * + `/chart/{range}`.
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

const RANGE_FOR_INTERVAL = new Map<Interval, string>([
  [Interval.D1, "1m"],
  [Interval.W1, "1y"],
  [Interval.MN1, "max"],
]);

/** Options for {@link IEXCloudAdapter}. */
export interface IEXCloudOptions {
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

/** IEX Cloud US-stocks adapter. */
export class IEXCloudAdapter implements MarketDataAdapter {
  public readonly adapterId = "iex-cloud";
  public readonly capability: Capability;

  private readonly apiKey?: string;
  private readonly apiKeyProvider?: ApiKeyProvider;
  private readonly baseUrl: string;
  private readonly fetchFn: FetchLike;

  public constructor(options: IEXCloudOptions = {}) {
    if (options.apiKey !== undefined) {
      this.apiKey = options.apiKey;
    }
    if (options.apiKeyProvider !== undefined) {
      this.apiKeyProvider = options.apiKeyProvider;
    }
    this.baseUrl = (options.baseUrl ?? "https://cloud.iexapis.com").replace(
      /\/+$/,
      "",
    );
    this.fetchFn = options.fetchFn ?? defaultFetch;
    this.capability = {
      supportedMarkets: ["stocks_us"],
      supportedIntervals: [...RANGE_FOR_INTERVAL.keys()],
      supportedQuoteCurrencies: ["USD"],
      supportsOrderBook: false,
      supportsNativeStreaming: false,
      rateLimitPerMinute: 100,
      requiresAuth: true,
      tierOptions: ["sandbox", "standard"],
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
          `iex-cloud does not know symbol (404): ${path}`,
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
      `/stable/stock/${symbol}/quote`,
      { token: key },
      signal,
    )) as Record<string, unknown>;
    if (root["latestPrice"] === undefined) {
      throw new AdapterUnavailableError(
        "iex-cloud returned unexpected payload",
        this.adapterId,
      );
    }
    const bid = optDv(root["iexBidPrice"] ?? root["bidPrice"]);
    const ask = optDv(root["iexAskPrice"] ?? root["askPrice"]);
    const high = optDv(root["high"]);
    const low = optDv(root["low"]);
    const vol = optDv(root["latestVolume"]);
    const chg = optDv(root["changePercent"]);
    const prev = optDv(root["previousClose"]);
    const mcap = optDv(root["marketCap"]);
    return {
      symbol,
      last: new Decimal(root["latestPrice"] as Decimal.Value),
      quoteCurrency,
      timestamp: new Date(Number(root["latestUpdate"] ?? 0)),
      sourceAdapter: this.adapterId,
      extra: EMPTY_EXTRA,
      ...(bid !== undefined && { bid }),
      ...(ask !== undefined && { ask }),
      ...(high !== undefined && { high24h: high }),
      ...(low !== undefined && { low24h: low }),
      ...(vol !== undefined && { volume24h: vol }),
      ...(chg !== undefined && { change24hPct: chg }),
      ...(prev !== undefined && { previousClose: prev }),
      ...(mcap !== undefined && { marketCap: mcap }),
    };
  }

  public async getOhlcv(
    symbol: string,
    interval: Interval,
    since: Date | undefined,
    limit: number,
    signal?: AbortSignal,
  ): Promise<readonly Candle[]> {
    const range = RANGE_FOR_INTERVAL.get(interval);
    if (range === undefined) {
      throw new InvalidIntervalError(
        `iex-cloud does not support interval ${interval}`,
        this.adapterId,
      );
    }
    const key = await this.key(signal);
    const payload = await this.get(
      `/stable/stock/${symbol}/chart/${range}`,
      { token: key, chartCloseOnly: "false" },
      signal,
    );
    if (!Array.isArray(payload)) {
      throw new AdapterUnavailableError(
        "iex-cloud chart returned unexpected payload",
        this.adapterId,
      );
    }
    const candles: Candle[] = [];
    for (const row of payload as Record<string, unknown>[]) {
      const ts = new Date(`${String(row["date"])}T00:00:00Z`);
      if (since !== undefined && ts.getTime() < since.getTime()) {
        continue;
      }
      candles.push({
        timestamp: ts,
        open: new Decimal(row["open"] as Decimal.Value),
        high: new Decimal(row["high"] as Decimal.Value),
        low: new Decimal(row["low"] as Decimal.Value),
        close: new Decimal(row["close"] as Decimal.Value),
        volume:
          row["volume"] !== undefined
            ? new Decimal(row["volume"] as Decimal.Value)
            : new Decimal(0),
        extra: EMPTY_EXTRA,
      });
      if (candles.length >= limit) {
        break;
      }
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
        "iex-cloud does not expose a public order book endpoint",
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
