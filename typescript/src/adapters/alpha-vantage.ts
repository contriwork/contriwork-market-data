/**
 * Alpha Vantage adapter — crypto + US stocks + BIST + forex. GLOBAL_QUOTE
 * for stocks, CURRENCY_EXCHANGE_RATE for 3-char pairs, TIME_SERIES_* for
 * OHLCV. Free tier is throttled (5 req/min).
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
  RateLimitedError,
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

const INTRADAY_INTERVAL = new Map<Interval, string>([
  [Interval.M1, "1min"],
  [Interval.M5, "5min"],
  [Interval.M15, "15min"],
  [Interval.M30, "30min"],
  [Interval.H1, "60min"],
]);

const SUPPORTED_INTERVALS: readonly Interval[] = [
  Interval.M1,
  Interval.M5,
  Interval.M15,
  Interval.M30,
  Interval.H1,
  Interval.D1,
  Interval.W1,
  Interval.MN1,
];

/** Options for {@link AlphaVantageAdapter}. */
export interface AlphaVantageOptions {
  readonly apiKey?: string;
  readonly apiKeyProvider?: ApiKeyProvider;
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

function parseAlphaTimestamp(text: string): Date {
  // "2026-04-30" or "2026-04-30 16:00:00" — treat as UTC.
  const iso = text.includes(" ")
    ? text.replace(" ", "T") + "Z"
    : text + "T00:00:00Z";
  return new Date(iso);
}

/** Alpha Vantage multi-asset adapter. */
export class AlphaVantageAdapter implements MarketDataAdapter {
  public readonly adapterId = "alpha-vantage";
  public readonly capability: Capability;

  private readonly apiKey?: string;
  private readonly apiKeyProvider?: ApiKeyProvider;
  private readonly baseUrl: string;
  private readonly fetchFn: FetchLike;

  public constructor(options: AlphaVantageOptions = {}) {
    if (options.apiKey !== undefined) {
      this.apiKey = options.apiKey;
    }
    if (options.apiKeyProvider !== undefined) {
      this.apiKeyProvider = options.apiKeyProvider;
    }
    this.baseUrl = (options.baseUrl ?? "https://www.alphavantage.co").replace(
      /\/+$/,
      "",
    );
    this.fetchFn = options.fetchFn ?? defaultFetch;
    this.capability = {
      supportedMarkets: ["crypto", "stocks_us", "stocks_tr", "forex"],
      supportedIntervals: SUPPORTED_INTERVALS,
      supportedQuoteCurrencies: "ANY",
      supportsOrderBook: false,
      supportsNativeStreaming: false,
      rateLimitPerMinute: 5,
      requiresAuth: true,
      tierOptions: ["free", "premium"],
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

  private checkThrottle(root: Record<string, unknown>): void {
    const text =
      `${String(root["Note"] ?? "")} ${String(root["Information"] ?? "")}`.toLowerCase();
    if (
      text.includes("thank you for using") ||
      text.includes("rate") ||
      text.includes("throttle")
    ) {
      throw new RateLimitedError(
        `alpha-vantage throttled: ${String(root["Note"] ?? root["Information"])}`,
        this.adapterId,
      );
    }
  }

  public async getSpot(
    symbol: string,
    quoteCurrency: string,
    signal?: AbortSignal,
  ): Promise<SpotPrice> {
    const key = await this.key(signal);
    const isPair =
      symbol.includes("/") || (symbol.length === 3 && /^[a-z]+$/i.test(symbol));

    if (isPair) {
      const url =
        `${this.baseUrl}/query` +
        queryString({
          function: "CURRENCY_EXCHANGE_RATE",
          from_currency: symbol.split("/")[0] as string,
          to_currency: quoteCurrency,
          apikey: key,
        });
      const root = (await getJson(
        this.fetchFn,
        this.adapterId,
        url,
        undefined,
        signal,
      )) as Record<string, unknown>;
      this.checkThrottle(root);
      const block = root["Realtime Currency Exchange Rate"] as
        | Record<string, unknown>
        | undefined;
      if (block === undefined) {
        throw new SymbolNotFoundError(
          `alpha-vantage has no exchange rate for '${symbol}'/'${quoteCurrency}'`,
          this.adapterId,
        );
      }
      const bid = optDv(block["8. Bid Price"]);
      const ask = optDv(block["9. Ask Price"]);
      return {
        symbol,
        last: dv(block["5. Exchange Rate"]),
        quoteCurrency,
        timestamp: new Date(),
        sourceAdapter: this.adapterId,
        extra: EMPTY_EXTRA,
        ...(bid !== undefined && { bid }),
        ...(ask !== undefined && { ask }),
      };
    }

    const url =
      `${this.baseUrl}/query` +
      queryString({ function: "GLOBAL_QUOTE", symbol, apikey: key });
    const root = (await getJson(
      this.fetchFn,
      this.adapterId,
      url,
      undefined,
      signal,
    )) as Record<string, unknown>;
    this.checkThrottle(root);
    const quote = root["Global Quote"] as Record<string, unknown> | undefined;
    if (quote === undefined || quote["05. price"] === undefined) {
      throw new SymbolNotFoundError(
        `alpha-vantage has no quote for '${symbol}'`,
        this.adapterId,
      );
    }
    const changeRaw = String(quote["10. change percent"] ?? "").replace(
      "%",
      "",
    );
    const high = optDv(quote["03. high"]);
    const low = optDv(quote["04. low"]);
    const vol = optDv(quote["06. volume"]);
    const prev = optDv(quote["08. previous close"]);
    const change = changeRaw === "" ? undefined : new Decimal(changeRaw);
    return {
      symbol,
      last: dv(quote["05. price"]),
      quoteCurrency,
      timestamp: new Date(),
      sourceAdapter: this.adapterId,
      extra: EMPTY_EXTRA,
      ...(high !== undefined && { high24h: high }),
      ...(low !== undefined && { low24h: low }),
      ...(vol !== undefined && { volume24h: vol }),
      ...(change !== undefined && { change24hPct: change }),
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
    if (!SUPPORTED_INTERVALS.includes(interval)) {
      throw new InvalidIntervalError(
        `alpha-vantage does not support interval ${interval}`,
        this.adapterId,
      );
    }
    const key = await this.key(signal);
    const query: Record<string, string | number | undefined> = {
      symbol,
      apikey: key,
    };
    let seriesKey: string;
    const intraday = INTRADAY_INTERVAL.get(interval);
    if (intraday !== undefined) {
      query["function"] = "TIME_SERIES_INTRADAY";
      query["interval"] = intraday;
      query["outputsize"] = "full";
      seriesKey = `Time Series (${intraday})`;
    } else {
      const map: Record<string, [string, string]> = {
        [Interval.D1]: ["TIME_SERIES_DAILY", "Time Series (Daily)"],
        [Interval.W1]: ["TIME_SERIES_WEEKLY", "Weekly Time Series"],
        [Interval.MN1]: ["TIME_SERIES_MONTHLY", "Monthly Time Series"],
      };
      const entry = map[interval] as [string, string];
      query["function"] = entry[0];
      seriesKey = entry[1];
    }

    const url = `${this.baseUrl}/query` + queryString(query);
    const root = (await getJson(
      this.fetchFn,
      this.adapterId,
      url,
      undefined,
      signal,
    )) as Record<string, unknown>;
    this.checkThrottle(root);
    const series = root[seriesKey] as
      | Record<string, Record<string, unknown>>
      | undefined;
    if (series === undefined || Object.keys(series).length === 0) {
      throw new SymbolNotFoundError(
        `alpha-vantage has no time series for '${symbol}'/${interval}`,
        this.adapterId,
      );
    }
    const candles: Candle[] = [];
    for (const [tsText, row] of Object.entries(series)) {
      const ts = parseAlphaTimestamp(tsText);
      if (since !== undefined && ts.getTime() < since.getTime()) {
        continue;
      }
      candles.push({
        timestamp: ts,
        open: dv(row["1. open"]),
        high: dv(row["2. high"]),
        low: dv(row["3. low"]),
        close: dv(row["4. close"]),
        volume:
          row["5. volume"] !== undefined
            ? dv(row["5. volume"])
            : new Decimal(0),
        extra: EMPTY_EXTRA,
      });
    }
    candles.sort((a, b) => a.timestamp.getTime() - b.timestamp.getTime());
    return candles.length > limit
      ? candles.slice(candles.length - limit)
      : candles;
  }

  public getOrderBook(
    _symbol: string,
    _depth: number,
    _signal?: AbortSignal,
  ): Promise<OrderBook> {
    return Promise.reject(
      new AdapterFeatureNotSupportedError(
        "alpha-vantage does not expose order book",
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
