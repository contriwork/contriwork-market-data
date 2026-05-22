/**
 * Public port — mirror of CONTRACT.md §2. The production implementation is
 * `MarketDataClient`; method names mirror the Python (`snake_case`) and C#
 * (`PascalCaseAsync`) ports.
 */
import type {
  Candle,
  Interval,
  OrderBook,
  SpotPrice,
  Ticker,
} from "./types.js";

/** The consumer-facing market-data port. */
export interface MarketDataPort {
  /**
   * Fetch the latest spot price for a symbol.
   *
   * @param symbol - Adapter-native symbol.
   * @param market - Market string resolving to an adapter chain.
   * @param quoteCurrency - Quote currency; defaults to `USD`.
   * @returns The latest spot price.
   */
  getSpot(
    symbol: string,
    market: string,
    quoteCurrency?: string,
  ): Promise<SpotPrice>;

  /**
   * Fetch historical OHLCV candles ordered ascending by timestamp.
   *
   * @param symbol - Adapter-native symbol.
   * @param market - Market string resolving to an adapter chain.
   * @param interval - Candle interval.
   * @param since - Lower-bound UTC timestamp, or `undefined`.
   * @param limit - Maximum candle count (1..1000).
   * @returns The candles.
   */
  getOhlcv(
    symbol: string,
    market: string,
    interval: Interval,
    since?: Date,
    limit?: number,
  ): Promise<readonly Candle[]>;

  /**
   * Fetch the top-N order book.
   *
   * @param symbol - Adapter-native symbol.
   * @param market - Market string resolving to an adapter chain.
   * @param depth - Levels per side (1..100).
   * @returns The order book.
   */
  getOrderBook(
    symbol: string,
    market: string,
    depth?: number,
  ): Promise<OrderBook>;

  /**
   * Subscribe to a live ticker stream (native or polling emulation).
   *
   * @param symbol - Adapter-native symbol.
   * @param market - Market string resolving to an adapter chain.
   * @param options - Streaming options.
   * @returns An async stream of ticker updates.
   */
  subscribeTicker(
    symbol: string,
    market: string,
    options?: SubscribeTickerOptions,
  ): AsyncIterable<Ticker>;
}

/** Options for {@link MarketDataPort.subscribeTicker}. */
export interface SubscribeTickerOptions {
  /** Emulate streaming via polling when no native feed exists (default true). */
  readonly pollingFallback?: boolean;
  /** Interval between polling requests in seconds (default 4, range 1..3600). */
  readonly pollingIntervalSeconds?: number;
  /** Cancellation signal that stops the stream. */
  readonly signal?: AbortSignal;
}
