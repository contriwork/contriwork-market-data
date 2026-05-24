/**
 * Concrete adapter implementations. v0.1.0 ships InMemory + ten provider
 * adapters (five crypto, five stocks). YFinance is Python-only (SCOPE.md §2.2).
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
export {
  AlphaVantageAdapter,
  type AlphaVantageOptions,
} from "./alpha-vantage.js";
export { FinnhubAdapter, type FinnhubOptions } from "./finnhub.js";
export { IEXCloudAdapter, type IEXCloudOptions } from "./iex-cloud.js";
export { PolygonIOAdapter, type PolygonIOOptions } from "./polygon-io.js";
export { TiingoAdapter, type TiingoOptions } from "./tiingo.js";
export { type FetchLike } from "../internal/http.js";
