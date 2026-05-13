# CONTRACT — MarketData

This document is the **language-agnostic contract** for `contriwork-market-data`.
It is the single source of truth for the public surface. Every change to
public behavior MUST start here before any code is written in `python/`,
`csharp/`, or `typescript/`.

Contract revision (bumped on any behavior-visible change): **v1**

References:
- [`docs/SCOPE.md`](./docs/SCOPE.md) — human-readable scope, defaults, rationale.
- [`docs/DW_AUDIT.md`](./docs/DW_AUDIT.md) §10 — locked design decisions.

---

## 1. Overview

The package provides a unified, provider-agnostic market-data port. A
`MarketDataClient` orchestrates one or more `Adapter` instances per
`market` (asset class string). Four operations are exposed:

- `get_spot` — latest spot price + optional 24-hour stats.
- `get_ohlcv` — historical candles for an interval.
- `get_order_book` — top-N bid/ask levels.
- `subscribe_ticker` — live ticker stream (native WSS or polling emulation).

Cross-cutting concerns are first-class: TTL cache (opt-in), per-adapter
token-bucket rate limiting (with retry and chain fall-through), ordered
adapter fallback, lazy credential resolution.

---

## 2. Port operations

| Operation | Input | Output | Failure modes |
|---|---|---|---|
| `get_spot` | `symbol: str`, `market: str`, `quote_currency: str = "USD"` | `SpotPrice` | `INVALID_INPUT`, `UNSUPPORTED_QUOTE_CURRENCY`, `SYMBOL_NOT_FOUND`, `RATE_LIMITED`, `ADAPTER_UNAVAILABLE`, `MISSING_CREDENTIALS`, `NO_ADAPTER_FOR_MARKET`, `ALL_ADAPTERS_FAILED` |
| `get_ohlcv` | `symbol: str`, `market: str`, `interval: Interval`, `since: DateTime?`, `limit: int = 100` | `list[Candle]` | All of the above plus `INVALID_INTERVAL` |
| `get_order_book` | `symbol: str`, `market: str`, `depth: int = 20` | `OrderBook` | All of get_spot plus `ADAPTER_FEATURE_NOT_SUPPORTED` |
| `subscribe_ticker` | `symbol: str`, `market: str`, `polling_fallback: bool = True`, `polling_interval_s: float = 4.0` | `AsyncIterator[Ticker]` | `STREAMING_NOT_SUPPORTED`, `STREAM_DISCONNECTED`, plus pull-time errors at subscribe time |

Method names adapt to language casing only (`snake_case` Python,
`PascalCaseAsync` C#, `camelCase` TypeScript). All async.

---

## 3. Method specifications

### 3.1 `get_spot`

**Signature** — `get_spot(symbol: str, market: str, quote_currency: str = "USD") -> SpotPrice`

**Parameters**:
- `symbol` — adapter-native string. Non-empty, length ≤ 64, ASCII printable.
  No normalization is performed; caller passes the format expected by the
  resolved adapter (e.g. `"BTCUSDT"` for Binance, `"bitcoin"` for CoinGecko,
  `"AAPL"` for Polygon, `"AKBNK.IS"` for YFinance).
- `market` — well-known string from `SCOPE.md` §2.3 or caller-defined;
  resolves to an adapter chain.
- `quote_currency` — ISO-4217 or crypto ticker (e.g. `"USD"`, `"TRY"`,
  `"USDT"`, `"BTC"`). Default `"USD"`.

**Returns**: `SpotPrice` (§5.1). At minimum `symbol`, `last`,
`quote_currency`, `timestamp`, `source_adapter` are populated.

**Preconditions**: `symbol` and `market` non-empty. `quote_currency` of
length ≥ 2 and ≤ 8.

**Postconditions**: For successful return, `SpotPrice.last` is finite (not
NaN, not infinity), `> 0`, and `SpotPrice.timestamp` is within the last 24
hours (otherwise the adapter signals `ADAPTER_UNAVAILABLE`).

### 3.2 `get_ohlcv`

**Signature** — `get_ohlcv(symbol: str, market: str, interval: Interval, since: DateTime? = None, limit: int = 100) -> list[Candle]`

**Parameters**:
- `interval` — enum (§5.5).
- `since` — UTC DateTime. If None, adapter chooses sensible default (last
  `limit` candles ending at "now").
- `limit` — `1 ≤ limit ≤ 1000`; adapter may impose a stricter cap (raises
  `INVALID_INPUT` with hint).

**Returns**: `list[Candle]` ordered by `timestamp` ascending. Empty list is
valid (e.g. interval has no data after `since`).

**Preconditions**: as above; `since` if supplied not in the future.

**Postconditions**: each candle's `low ≤ open, close ≤ high`; `volume ≥ 0`;
contiguous `timestamp` spacing equals `interval` (within adapter precision).

### 3.3 `get_order_book`

**Signature** — `get_order_book(symbol: str, market: str, depth: int = 20) -> OrderBook`

**Parameters**:
- `depth` — number of levels per side; `1 ≤ depth ≤ 100`. Adapter may impose
  a stricter cap.

**Returns**: `OrderBook` (§5.3). `bids` sorted descending by price; `asks`
sorted ascending. Each level is `(price, size)` where both `> 0`.

**Adapter support**: `Capability.supports_order_book` indicates which
adapters implement this. Adapters that don't (CoinGecko, CMC, Alpha Vantage,
Tiingo, yfinance, IEX Cloud free tier) raise `ADAPTER_FEATURE_NOT_SUPPORTED`;
the orchestrator skips them in the chain.

### 3.4 `subscribe_ticker`

**Signature** — `subscribe_ticker(symbol: str, market: str, polling_fallback: bool = True, polling_interval_s: float = 4.0) -> AsyncIterator[Ticker]`

**Parameters**:
- `polling_fallback` — when the resolved adapter has no native streaming,
  this controls behavior:
  - `true` — package emulates streaming via repeated `get_spot` calls.
  - `false` — `STREAMING_NOT_SUPPORTED` raised at subscribe time.
- `polling_interval_s` — interval between request starts when emulating
  (back-pressure aware: if a request takes longer than the interval, the
  next request waits, no overlap). Range `1.0 ≤ interval ≤ 3600.0`.

**Returns**: `AsyncIterator[Ticker]` (Python), `IAsyncEnumerable<Ticker>`
(C#), `AsyncIterable<Ticker>` (TypeScript). One `Ticker` yielded per price
update (native) or per polling tick (emulation).

**Adapter resolution**: orchestrator picks the **first** adapter in the
`market` chain that satisfies the `(native_or_fallback)` constraint. Native
takes precedence within tied positions.

**Reconnect semantics** (native WSS only): on disconnect, the orchestrator
reconnects with exponential backoff (`reconnect_backoff_s`, jittered) up to
`max_reconnect_attempts`; on exhaustion, the iterator raises
`STREAM_DISCONNECTED`. Polling emulation uses the same retry budget for
consecutive fetch failures.

**Cancellation**: closing the iterator (Python `aclose()`, C#
`DisposeAsync()`, TS `AbortSignal.abort()`) cleanly cancels pending network
operations and stops further yields.

**Preconditions**: same as `get_spot`.

**Postconditions**: `Ticker.timestamp` is monotonically non-decreasing
within a single subscription.

---

## 4. Adapter protocol

Every adapter implements:

```
async fn get_spot(symbol, quote_currency)            -> SpotPrice | raise
async fn get_ohlcv(symbol, interval, since, limit)   -> list[Candle] | raise
async fn get_order_book(symbol, depth)               -> OrderBook | raise [or NotSupported if Capability.supports_order_book = false]
async fn subscribe_ticker(symbol)                    -> AsyncIterator[Ticker] | raise [or NotSupported if Capability.supports_native_streaming = false]
property capability                                  -> Capability
property adapter_id                                  -> str   # stable kebab-case id, e.g. "coingecko", "binance-public"
```

The orchestrator (`MarketDataClient`) is responsible for `market` resolution,
`quote_currency` mismatch handling, fallback chain, cache, rate limiting,
and streaming polling emulation. Adapters do not implement those concerns.

`Capability` (§5.7) declares static traits per adapter.

---

## 5. Data types

### 5.1 `SpotPrice`

```
{
  # Core required
  symbol: str
  last: Decimal
  quote_currency: str
  timestamp: DateTime          # UTC
  source_adapter: str

  # Standard optional (best-effort)
  bid: Decimal?
  ask: Decimal?
  high_24h: Decimal?
  low_24h: Decimal?
  volume_24h: Decimal?
  change_24h_pct: Decimal?
  market_cap: Decimal?
  previous_close: Decimal?

  # Provider-specific extension
  extra: Mapping[str, Any]     # immutable, keys namespaced "<adapter_id>.<field>"
}
```

### 5.2 `Candle`

```
{
  # Core required
  timestamp: DateTime          # UTC, candle open time
  open: Decimal
  high: Decimal
  low: Decimal
  close: Decimal
  volume: Decimal

  # Standard optional
  quote_volume: Decimal?       # in quote_currency
  trade_count: int?

  # Provider-specific extension
  extra: Mapping[str, Any]
}
```

### 5.3 `OrderBook`

```
{
  # Core required
  symbol: str
  bids: list[BookLevel]        # sorted desc by price
  asks: list[BookLevel]        # sorted asc by price
  timestamp: DateTime
  source_adapter: str

  # Standard optional
  sequence: int?               # adapter-specific update id

  extra: Mapping[str, Any]
}

BookLevel = (price: Decimal, size: Decimal)
```

### 5.4 `Ticker`

```
{
  # Core required
  symbol: str
  price: Decimal
  quote_currency: str
  timestamp: DateTime          # UTC
  source_adapter: str

  # Standard optional
  side: "bid" | "ask" | "trade" | None
  size: Decimal?
  bid: Decimal?
  ask: Decimal?

  extra: Mapping[str, Any]
}
```

### 5.5 `Interval`

Enum, ordering and names invariant within v1:

```
M1, M5, M15, M30, H1, H4, D1, W1, MN1
```

Strings are language-agnostic. Adapters declare supported intervals via
`Capability.supported_intervals`. Asking for an unsupported interval raises
`INVALID_INTERVAL`.

### 5.6 `MarketDataError` (base class)

```
{
  code: ErrorCode              # see §7
  message: str                 # human-readable, English
  adapter_id: str?             # adapter that raised, if applicable
  cause: list[MarketDataError]?  # for ALL_ADAPTERS_FAILED
}
```

### 5.7 `Capability`

```
{
  supported_markets: list[str]
  supported_intervals: list[Interval]
  supported_quote_currencies: list[str] | "ANY"
  supports_order_book: bool
  supports_native_streaming: bool
  rate_limit_per_minute: int           # default; caller may override via config
  requires_auth: bool
  tier_options: list[str]              # e.g. ["demo", "free", "pro"] or []
}
```

---

## 6. Cache behavior

- Default: **disabled** (`cache.enabled = false`). Opt-in via config.
- Key: `(method, adapter_id, market, symbol, *args)` where `*args` includes
  `quote_currency` for `get_spot`/`get_ohlcv` and `depth` for
  `get_order_book`.
- TTL per method, configurable, defaults invariant within v1:
  - `spot_ttl_s = 5`
  - `ohlcv_ttl_s = 60`
  - `order_book_ttl_s = 1`
- Eviction: LRU at `cache.max_entries` (default 10,000).
- Streaming (`subscribe_ticker`) bypasses cache entirely.
- Cache hit returns the cached value as-is (immutable types — no defensive
  copy required).

---

## 7. Error taxonomy (locked)

Code values are SCREAMING_SNAKE_CASE strings, stable within v1.

| Code | Description |
|---|---|
| `INVALID_INPUT` | Validation failure on caller-supplied parameters. |
| `INVALID_INTERVAL` | Requested `Interval` not in `Capability.supported_intervals`. |
| `UNSUPPORTED_QUOTE_CURRENCY` | `quote_currency` not in `Capability.supported_quote_currencies`. |
| `SYMBOL_NOT_FOUND` | Provider says it doesn't recognize the symbol. |
| `RATE_LIMITED` | Adapter quota exhausted after retry exhaustion (when `rate_limit_strategy = "bubble"`). |
| `ADAPTER_UNAVAILABLE` | Adapter HTTP/network/parse failure (5xx, timeout, malformed body). |
| `ADAPTER_FEATURE_NOT_SUPPORTED` | Adapter capability flag is false for the requested operation. |
| `MISSING_CREDENTIALS` | Adapter requires auth, neither static `api_key` nor `api_key_provider` resolved a usable value. |
| `NO_ADAPTER_FOR_MARKET` | Registry has no adapter chain for the requested `market`. |
| `ALL_ADAPTERS_FAILED` | Every adapter in chain failed; `cause` lists per-adapter errors. |
| `STREAMING_NOT_SUPPORTED` | `polling_fallback=false` and adapter has no native streaming. |
| `STREAM_DISCONNECTED` | Active stream lost connection beyond reconnect budget. |

Per-language exception types wrap these codes:
- Python — `MarketDataError(code, message, adapter_id, cause)` extending
  `Exception`. Subclasses per code (e.g. `RateLimitedError`).
- C# — `MarketDataException` extending `Exception`, `Code` property.
  Subclasses per code.
- TypeScript — `MarketDataError` extending `Error`, `code` property.

---

## 8. Config schema

| Section | Key | Env var | Type | Default | Required |
|---|---|---|---|---|---|
| `cache` | `enabled` | `CONTRIWORK_MARKET_DATA_CACHE_ENABLED` | bool | `false` | no |
| `cache` | `spot_ttl_s` | `CONTRIWORK_MARKET_DATA_CACHE_SPOT_TTL_S` | int | `5` | no |
| `cache` | `ohlcv_ttl_s` | `CONTRIWORK_MARKET_DATA_CACHE_OHLCV_TTL_S` | int | `60` | no |
| `cache` | `order_book_ttl_s` | `CONTRIWORK_MARKET_DATA_CACHE_ORDER_BOOK_TTL_S` | int | `1` | no |
| `cache` | `max_entries` | `CONTRIWORK_MARKET_DATA_CACHE_MAX_ENTRIES` | int | `10000` | no |
| `rate_limit` | `enabled` | `CONTRIWORK_MARKET_DATA_RATE_LIMIT_ENABLED` | bool | `true` | no |
| `rate_limit` | `strategy` | `CONTRIWORK_MARKET_DATA_RATE_LIMIT_STRATEGY` | `"bubble"` \| `"fallthrough"` | `"fallthrough"` | no |
| `rate_limit` | `max_retry_attempts` | `CONTRIWORK_MARKET_DATA_RATE_LIMIT_MAX_RETRY_ATTEMPTS` | int | `3` | no |
| `rate_limit` | `initial_backoff_s` | `CONTRIWORK_MARKET_DATA_RATE_LIMIT_INITIAL_BACKOFF_S` | float | `0.5` | no |
| `rate_limit` | `max_backoff_s` | `CONTRIWORK_MARKET_DATA_RATE_LIMIT_MAX_BACKOFF_S` | float | `30.0` | no |
| `rate_limit` | `jitter` | `CONTRIWORK_MARKET_DATA_RATE_LIMIT_JITTER` | bool | `true` | no |
| `streaming` | `default_polling_interval_s` | `CONTRIWORK_MARKET_DATA_STREAMING_POLLING_INTERVAL_S` | float | `4.0` | no |
| `streaming` | `max_reconnect_attempts` | `CONTRIWORK_MARKET_DATA_STREAMING_MAX_RECONNECT_ATTEMPTS` | int | `5` | no |
| `streaming` | `reconnect_backoff_s` | `CONTRIWORK_MARKET_DATA_STREAMING_RECONNECT_BACKOFF_S` | float | `2.0` | no |

Adapter-specific config (per-adapter `api_key`, `tier`, `base_url`,
`timeout_s`, etc.) is detailed in each adapter's README and is not part of
the cross-language config invariants, except that env var names follow the
pattern `CONTRIWORK_MARKET_DATA_<ADAPTER_ID_UPPER>_<KEY>`
(e.g. `CONTRIWORK_MARKET_DATA_COINGECKO_API_KEY`).

---

## 9. Compatibility

- **Python**: ≥ 3.13.
- **.NET**: ≥ 10.0 LTS.
- **Node.js**: ≥ 24 Active LTS.
- **npm strategy**: pure-TS reimplementation (Strategy A — see
  [`typescript/src/strategy.md`](./typescript/src/strategy.md)).

Runtime baseline is a hard constraint — no parallel matrix support for older
LTSes.

---

## 10. Invariants (within v1)

The following do **not** change without a major contract bump:

1. Method signatures — argument count, names, types, ordering.
2. `Interval` enum values and their string spellings.
3. Error codes (no rename).
4. `Capability` keys (additive grow only).
5. Config key names and env var names.
6. `extra` field key namespace pattern (`<adapter_id>.<field>`).
7. Default cache TTLs (`5/60/1`).
8. The four core operations — no removal.

The following **may** change in minor releases:
- Default adapter chains (bug fix or new adapter registration).
- New optional method parameters (additive).
- New `Capability` keys (additive).
- New error codes (additive — but never reuse a removed code).
- New adapters (additive registry).

---

## 11. Change log

Contract revisions only — bumped when any of the sections above change in a
way a consumer can observe. Does NOT track patch fixes or internal
refactors; those go in `CHANGELOG.md`.

| Revision | Summary | Released with package version |
|---|---|---|
| v1 | Initial contract: 4 operations, 12 adapter protocol slots, market-string convention, native-or-emulated streaming, opt-in cache, fall-through rate limit, lazy credential resolution, extra-dict extension model. | 0.1.0 |
