/**
 * Public data types — mirror of CONTRACT.md §5.
 *
 * Numeric fields use `Decimal` (decimal.js) to preserve exact precision
 * across providers and across the wire; timestamps use `Date` (always UTC).
 * The `extra` map is the provider-specific extension slot with keys
 * namespaced `<adapterId>.<field>`.
 */
import { Decimal } from "decimal.js";

export { Decimal };

/** Time interval for OHLCV candles. CONTRACT v1 §5.5 — names invariant. */
export enum Interval {
  M1 = "M1",
  M5 = "M5",
  M15 = "M15",
  M30 = "M30",
  H1 = "H1",
  H4 = "H4",
  D1 = "D1",
  W1 = "W1",
  MN1 = "MN1",
}

/** Immutable provider-specific extension map. */
export type Extra = Readonly<Record<string, unknown>>;

/** Latest spot price plus optional 24-hour statistics. CONTRACT v1 §5.1. */
export interface SpotPrice {
  readonly symbol: string;
  readonly last: Decimal;
  readonly quoteCurrency: string;
  readonly timestamp: Date;
  readonly sourceAdapter: string;
  readonly bid?: Decimal;
  readonly ask?: Decimal;
  readonly high24h?: Decimal;
  readonly low24h?: Decimal;
  readonly volume24h?: Decimal;
  readonly change24hPct?: Decimal;
  readonly marketCap?: Decimal;
  readonly previousClose?: Decimal;
  readonly extra: Extra;
}

/** A single OHLCV candle. CONTRACT v1 §5.2. */
export interface Candle {
  readonly timestamp: Date;
  readonly open: Decimal;
  readonly high: Decimal;
  readonly low: Decimal;
  readonly close: Decimal;
  readonly volume: Decimal;
  readonly quoteVolume?: Decimal;
  readonly tradeCount?: number;
  readonly extra: Extra;
}

/** A single order-book price level. CONTRACT v1 §5.3. */
export interface BookLevel {
  readonly price: Decimal;
  readonly size: Decimal;
}

/** Top-N order book. CONTRACT v1 §5.3. */
export interface OrderBook {
  readonly symbol: string;
  readonly bids: readonly BookLevel[];
  readonly asks: readonly BookLevel[];
  readonly timestamp: Date;
  readonly sourceAdapter: string;
  readonly sequence?: number;
  readonly extra: Extra;
}

/** Side of a streamed ticker update. CONTRACT v1 §5.4. */
export type TickerSide = "bid" | "ask" | "trade";

/** A live ticker update. CONTRACT v1 §5.4. */
export interface Ticker {
  readonly symbol: string;
  readonly price: Decimal;
  readonly quoteCurrency: string;
  readonly timestamp: Date;
  readonly sourceAdapter: string;
  readonly side?: TickerSide;
  readonly size?: Decimal;
  readonly bid?: Decimal;
  readonly ask?: Decimal;
  readonly extra: Extra;
}

/**
 * Quote-currency support marker: either an explicit set, or the dynamic
 * `"ANY"` literal when the adapter resolves quote currency on demand.
 */
export type QuoteCurrencySupport = readonly string[] | "ANY";

/** Static description of what an adapter supports. CONTRACT v1 §5.7. */
export interface Capability {
  readonly supportedMarkets: readonly string[];
  readonly supportedIntervals: readonly Interval[];
  readonly supportedQuoteCurrencies: QuoteCurrencySupport;
  readonly supportsOrderBook: boolean;
  readonly supportsNativeStreaming: boolean;
  readonly rateLimitPerMinute: number;
  readonly requiresAuth: boolean;
  readonly tierOptions?: readonly string[];
}

/** Shared empty immutable extension map. */
export const EMPTY_EXTRA: Extra = Object.freeze({});

/** Whether a capability's quote-currency set covers `currency`. */
export function quoteCurrencySupported(
  support: QuoteCurrencySupport,
  currency: string,
): boolean {
  if (support === "ANY") {
    return true;
  }
  return support.some((c) => c.toUpperCase() === currency.toUpperCase());
}
