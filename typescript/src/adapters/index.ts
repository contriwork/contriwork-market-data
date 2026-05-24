/**
 * Concrete adapter implementations. v0.1.0 ships InMemory + five crypto
 * adapters here; stocks adapters arrive in PR 10.
 */
export {
  InMemoryAdapter,
  InMemoryFailMode,
  type InMemoryAdapterOptions,
  type InMemoryFailModeSpec,
  type InMemorySymbolData,
} from "./in-memory.js";
export { CoinGeckoAdapter, type CoinGeckoOptions } from "./coingecko.js";
export {
  BinancePublicAdapter,
  type BinancePublicOptions,
} from "./binance-public.js";
export { KrakenAdapter, type KrakenOptions } from "./kraken.js";
export { CoinbaseAdapter, type CoinbaseOptions } from "./coinbase.js";
export {
  CoinMarketCapAdapter,
  type CoinMarketCapOptions,
} from "./coinmarketcap.js";
export { type FetchLike } from "../internal/http.js";
