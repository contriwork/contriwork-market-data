/**
 * Tiingo adapter — US stocks via the IEX-routed feed. `/iex/{ticker}` +
 * `/iex/{ticker}/prices`. Auth via `Authorization: Token`.
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

const RESAMPLE_FREQ = new Map<Interval, string>([
  [Interval.M1, "1min"],
  [Interval.M5, "5min"],
  [Interval.M15, "15min"],
  [Interval.M30, "30min"],
  [Interval.H1, "1hour"],
  [Interval.D1, "daily"],
]);

/** Options for {@link TiingoAdapter}. */
export interface TiingoOptions {
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

/** Tiingo US-stocks adapter. */
export class TiingoAdapter implements MarketDataAdapter {
  public readonly adapterId = "tiingo";
  public readonly capability: Capability;

  private readonly apiKey?: string;
  private readonly apiKeyProvider?: ApiKeyProvider;
  private readonly baseUrl: string;
  private readonly fetchFn: FetchLike;

  public constructor(options: TiingoOptions = {}) {
    if (options.apiKey !== undefined) {
      this.apiKey = options.apiKey;
    }
    if (options.apiKeyProvider !== undefined) {
      this.apiKeyProvider = options.apiKeyProvider;
    }
    this.baseUrl = (options.baseUrl ?? "https://api.tiingo.com").replace(
      /\/+$/,
      "",
    );
    this.fetchFn = options.fetchFn ?? defaultFetch;
    this.capability = {
      supportedMarkets: ["stocks_us"],
      supportedIntervals: [...RESAMPLE_FREQ.keys()],
      supportedQuoteCurrencies: ["USD"],
      supportsOrderBook: false,
      supportsNativeStreaming: false,
      rateLimitPerMinute: 60,
      requiresAuth: true,
    };
  }

  private async headers(signal?: AbortSignal): Promise<Record<string, string>> {
    const key = await resolveApiKey(
      this.adapterId,
      this.apiKey,
      this.apiKeyProvider,
      true,
      signal,
    );
    return { Authorization: `Token ${key as string}` };
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
        await this.headers(signal),
        signal,
      );
    } catch (err) {
      if (
        err instanceof AdapterUnavailableError &&
        err.message.includes("HTTP 404")
      ) {
        throw new SymbolNotFoundError(
          `tiingo does not know ticker (404): ${path}`,
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
    const payload = await this.get(`/iex/${symbol}`, {}, signal);
    if (!Array.isArray(payload) || payload.length === 0) {
      throw new SymbolNotFoundError(
        `tiingo returned no quote for '${symbol}'`,
        this.adapterId,
      );
    }
    const item = payload[0] as Record<string, unknown>;
    const last = optDv(item["last"] ?? item["tngoLast"]);
    if (last === undefined) {
      throw new SymbolNotFoundError(
        `tiingo returned empty quote for '${symbol}'`,
        this.adapterId,
      );
    }
    const tsRaw = item["timestamp"];
    const bid = optDv(item["bidPrice"]);
    const ask = optDv(item["askPrice"]);
    const high = optDv(item["high"]);
    const low = optDv(item["low"]);
    const vol = optDv(item["volume"]);
    const prev = optDv(item["prevClose"]);
    return {
      symbol,
      last,
      quoteCurrency,
      timestamp: typeof tsRaw === "string" ? new Date(tsRaw) : new Date(),
      sourceAdapter: this.adapterId,
      extra: EMPTY_EXTRA,
      ...(bid !== undefined && { bid }),
      ...(ask !== undefined && { ask }),
      ...(high !== undefined && { high24h: high }),
      ...(low !== undefined && { low24h: low }),
      ...(vol !== undefined && { volume24h: vol }),
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
    const freq = RESAMPLE_FREQ.get(interval);
    if (freq === undefined) {
      throw new InvalidIntervalError(
        `tiingo does not support interval ${interval}`,
        this.adapterId,
      );
    }
    const query: Record<string, string | number | undefined> = {
      resampleFreq: freq,
    };
    if (since !== undefined) {
      query["startDate"] = since.toISOString().slice(0, 10);
    }
    const payload = await this.get(`/iex/${symbol}/prices`, query, signal);
    if (!Array.isArray(payload)) {
      throw new AdapterUnavailableError(
        "tiingo prices returned unexpected payload",
        this.adapterId,
      );
    }
    return (payload as Record<string, unknown>[])
      .slice(0, limit)
      .map((row) => ({
        timestamp: new Date(String(row["date"] ?? row["timestamp"])),
        open: new Decimal(row["open"] as Decimal.Value),
        high: new Decimal(row["high"] as Decimal.Value),
        low: new Decimal(row["low"] as Decimal.Value),
        close: new Decimal(row["close"] as Decimal.Value),
        volume:
          row["volume"] !== undefined
            ? new Decimal(row["volume"] as Decimal.Value)
            : new Decimal(0),
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
        "tiingo does not expose order book",
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
