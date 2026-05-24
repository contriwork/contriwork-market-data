# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html).

Each release MUST contain three language sub-sections (`### Python`, `### C#`, `### npm`). The release workflow refuses to publish if any sub-section is missing.

## [Unreleased]

## [0.1.0] - 2026-05-24

Initial release. The `MarketDataClient` orchestrator (opt-in TTL cache,
per-adapter token-bucket rate limiting with jittered retry, ordered adapter
fallback, native-or-polling-emulated streaming) is identical across all three
languages and validated by the shared 20-case `contract-tests` fixture.
Contract revision v1: `get_spot` / `get_ohlcv` / `get_order_book` /
`subscribe_ticker`.

### Python

- `MarketDataClient` + `AdapterRegistry` + 11 provider adapters (CoinGecko,
  CoinMarketCap, Binance public, Kraken, Coinbase, Alpha Vantage, Finnhub,
  IEX Cloud, Polygon.io, Tiingo) plus the `InMemoryAdapter` reference.
- `YFinanceAdapter` ships behind the optional `[yfinance]` extra
  (`pip install "contriwork-market-data[yfinance]"`) — Python-only.
- Frozen `Decimal`-typed dataclasses, 12-code error taxonomy, lazy
  credential resolution. 119 tests.

### C#

- `MarketDataClient` + `AdapterRegistry` + 10 provider adapters (same set,
  no YFinance) plus the `InMemoryAdapter` reference.
- `decimal`-typed records, `IAsyncEnumerable<Ticker>` streaming,
  `MarketDataException` taxonomy. 100 tests.

### npm

- `MarketDataClient` + `AdapterRegistry` + 10 provider adapters (same set,
  no YFinance) plus the `InMemoryAdapter` reference. Pure-TS reimplementation
  (Strategy A); `decimal.js` for exact precision; `fetch`-based adapters.
- `AsyncIterable<Ticker>` streaming with `AbortSignal` cancellation. 65 tests.

[Unreleased]: https://github.com/contriwork/contriwork-market-data/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/contriwork/contriwork-market-data/releases/tag/v0.1.0
