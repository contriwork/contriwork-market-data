/**
 * contriwork-market-data — public surface.
 *
 * See CONTRACT.md for the language-agnostic specification and docs/SCOPE.md
 * for v0.1.0 rationale. Provider adapters (CoinGecko, Binance, …) ship in
 * PR 9 / PR 10 and are wired by the caller into an `AdapterRegistry` passed
 * to `MarketDataClient`.
 */
export * from "./types.js";
export * from "./errors.js";
export * from "./config.js";
export * from "./port.js";
export type { MarketDataAdapter, ApiKeyProvider } from "./adapter.js";
export { type Clock, SystemClock, ManualClock } from "./clock.js";
export { AdapterRegistry } from "./registry.js";
export { MarketDataClient } from "./client.js";
export {
  InMemoryAdapter,
  InMemoryFailMode,
  type InMemoryAdapterOptions,
  type InMemoryFailModeSpec,
  type InMemorySymbolData,
} from "./adapters/index.js";
