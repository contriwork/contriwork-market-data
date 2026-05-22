/**
 * Adapter contract — CONTRACT.md §4. Each provider adapter implements this;
 * the orchestrator (`MarketDataClient`) layers cache, rate limiting, and
 * chain fallback on top.
 */
import type {
  Candle,
  Capability,
  Interval,
  OrderBook,
  SpotPrice,
  Ticker,
} from "./types.js";

/** A provider adapter. */
export interface MarketDataAdapter {
  /** Stable kebab-case adapter id (e.g. `"coingecko"`). */
  readonly adapterId: string;

  /** Static description of what the adapter supports. */
  readonly capability: Capability;

  /** Fetch the latest spot price. */
  getSpot(
    symbol: string,
    quoteCurrency: string,
    signal?: AbortSignal,
  ): Promise<SpotPrice>;

  /** Fetch historical OHLCV candles. */
  getOhlcv(
    symbol: string,
    interval: Interval,
    since: Date | undefined,
    limit: number,
    signal?: AbortSignal,
  ): Promise<readonly Candle[]>;

  /** Fetch the top-N order book. */
  getOrderBook(
    symbol: string,
    depth: number,
    signal?: AbortSignal,
  ): Promise<OrderBook>;

  /** Open a native ticker stream for the symbol. */
  subscribeTicker(symbol: string, signal?: AbortSignal): AsyncIterable<Ticker>;
}

/** A lazy credential provider — resolves a key on demand. */
export type ApiKeyProvider = (
  signal?: AbortSignal,
) => Promise<string | undefined>;
