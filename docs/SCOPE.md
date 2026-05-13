# SCOPE — contriwork-market-data v0.1.0

**Status**: Draft for user approval (2026-04-30)
**References**: [DW_AUDIT.md](./DW_AUDIT.md) §10 (locked decisions),
[`PACKAGES_ROADMAP.md`](../../../PACKAGES_ROADMAP.md) §4.3.

This document is the human-readable scope statement for v0.1.0. It declares
what is in, what is out, what extension points exist, and how the package
behaves around contentious design dimensions (streaming, currency, cache,
rate limit). The machine-readable contract lives in [`CONTRACT.md`](../CONTRACT.md).

---

## 1. Mission

Provide a unified, provider-agnostic market-data port across **Python**,
**.NET**, and **TypeScript** with identical contract behavior. Pull semantics
(`get_spot`, `get_ohlcv`, `get_order_book`) and streaming (`subscribe_ticker`)
are first-class. Adapter implementations are extensible — `InMemory` ships
for testing, eleven public-data providers ship for production use.

The package is the first of two market-related Contriwork packages:
- **contriwork-market-data** (this) — read-only public market data.
- **contriwork-exchange** (later) — authenticated trading, order placement,
  position management. Uses provider SDKs (e.g. `python-binance`).

Confusion between the two is a frequent failure mode; SCOPE makes the
boundary explicit (§3).

---

## 2. In scope (v0.1.0)

### 2.1 Methods

The port exposes **four operations**, identical across languages modulo
casing convention:

| Operation | Purpose |
|---|---|
| `get_spot(symbol, market, quote_currency)` | Latest spot price + optional 24h stats |
| `get_ohlcv(symbol, market, interval, since, limit)` | Historical candles |
| `get_order_book(symbol, market, depth)` | Top-N bid/ask levels (limit order book) |
| `subscribe_ticker(symbol, market, polling_fallback, polling_interval_s)` | Live ticker stream (native WSS or polling emulation) |

Full method specs: [`CONTRACT.md`](../CONTRACT.md) §4.

### 2.2 Adapters (12 in Python, 11 in C#/TS)

| Adapter | Py | C# | TS | Markets supported | Native streaming |
|---|:-:|:-:|:-:|---|:-:|
| InMemory | ✅ | ✅ | ✅ | All (test fixture) | ✅ (synthetic) |
| CoinGecko | ✅ | ✅ | ✅ | crypto | ❌ (free tier) |
| CoinMarketCap | ✅ | ✅ | ✅ | crypto | ❌ (Pro WSS exists, v0.1.0 polls) |
| Binance (public) | ✅ | ✅ | ✅ | crypto | ✅ |
| Kraken (public) | ✅ | ✅ | ✅ | crypto | ✅ |
| Coinbase (public) | ✅ | ✅ | ✅ | crypto | ✅ |
| Alpha Vantage | ✅ | ✅ | ✅ | crypto, stocks_us, stocks_tr, forex | ❌ |
| Finnhub | ✅ | ✅ | ✅ | stocks_us | ✅ (free tier limited) |
| IEX Cloud | ✅ | ✅ | ✅ | stocks_us | ❌ (REST only in v0.1.0) |
| Polygon.io | ✅ | ✅ | ✅ | stocks_us, forex | ❌ (paid WSS, v0.1.0 polls) |
| Tiingo | ✅ | ✅ | ✅ | stocks_us | ❌ |
| **YFinance** | ✅ | ❌ | ❌ | stocks_us, stocks_tr, stocks_eu, stocks_global, commodities, indices | ❌ |

**YFinance is Python-only.** C#/TS users cover stocks_tr / stocks_eu /
stocks_global / commodities / indices via Alpha Vantage. The decision is
deliberate (brief E): unofficial scraping wrappers exist for C#/TS
(`YahooFinanceApi`, `yahoo-finance2`) but their API stability is poor;
contriwork-market-data does not commit to maintaining parity with them in
v0.1.0. README + adapter docs state this explicitly.

For **streaming** (§5), adapters where `Capability.supports_native_streaming
= false` still implement `subscribe_ticker` via package-level polling
emulation — caller controls the trade-off via `polling_fallback`.

### 2.3 Markets (well-known values)

`market` is a **string parameter**, not an enum, allowing forward-compatible
extension. Eight well-known values ship with v0.1.0:

```
crypto, stocks_us, stocks_tr, stocks_eu, stocks_global,
forex, commodities, indices
```

`Capability.supported_markets` declares which markets each adapter handles.
`AdapterRegistry` maps `market → ordered list[Adapter]` (default chains in
§4 below, caller-overridable).

Caller-supplied novel markets (e.g. `"futures_cme"`) work if a registered
adapter declares support; otherwise `NO_ADAPTER_FOR_MARKET` is raised.

### 2.4 Cross-cutting features

- **TTL cache** — in-memory, key = `(method, adapter, market, symbol, *args)`,
  per-method TTL (`spot_ttl_s=5`, `ohlcv_ttl_s=60`, `order_book_ttl_s=1`),
  **default disabled**, opt-in via config (`cache.enabled=True`). Caller
  overrides default TTLs via config.
- **Per-adapter token-bucket rate limiter** — `Capability.rate_limit_per_minute`
  is the bucket size; on exhaustion the orchestrator backs off with jittered
  exponential retry (`max_attempts` configurable; default 3); after exhaustion
  the orchestrator either bubbles `RATE_LIMITED` or falls through to the next
  adapter in the chain (configurable: `rate_limit_strategy="bubble" | "fallthrough"`).
- **Ordered adapter fallback** — first adapter to return success wins.
  Adapter returning `SYMBOL_NOT_FOUND`, `ADAPTER_FEATURE_NOT_SUPPORTED`,
  `ADAPTER_UNAVAILABLE`, `RATE_LIMITED` (after exhaustion if `fallthrough`)
  triggers next-adapter retry. `ALL_ADAPTERS_FAILED` is the terminal error
  containing the full per-adapter failure trail.
- **API key resolution** — every adapter constructor accepts both static
  `api_key: str | None` AND a lazy `api_key_provider: AsyncCallable[[], str |
  None] | None`. Provider wins when both are present. Resolution is
  deferred until first call (lazy), so DB-backed credential stores (DW
  pattern) are not blocked at startup.

### 2.5 Data type extension model

All response types follow the same shape: `core required + standard optional
+ extra`.

```
SpotPrice / Candle / OrderBook / Ticker = {
  # Core (always populated)
  ...required fields specific to type...,
  # Standard optional (best-effort, populated when provider supplies)
  ...optional fields...,
  # Provider-specific extension
  extra: Mapping[str, Any]   # immutable (Py: types.MappingProxyType, C#: IReadOnlyDictionary, TS: Readonly<Record<string, unknown>>)
}
```

`extra` is the escape hatch: provider-specific fields (CoinGecko's
`ath_change_percentage`, Binance's `lastQty`, Polygon's `vwap`) flow through
without bloating the core schema. Field names in `extra` are
**adapter-prefixed** to avoid clashes (e.g. `coingecko.ath`,
`binance.last_qty`).

Full schemas: [`CONTRACT.md`](../CONTRACT.md) §5.

---

## 3. Out of scope (deferred to v0.2.0+ or a separate package)

**Out of v0.1.0 (will return in v0.2.0+ as additive, non-breaking changes):**

- Bulk historical download (months/years of OHLCV in one call) — needs
  pagination + rate-limit aware iteration; design space large enough to
  warrant its own RFC.
- WebSocket-based streaming on adapters that have it on paid tiers but
  default to polling in v0.1.0 (CMC Pro, Polygon.io, IEX Cloud, CoinGecko Pro).
  v0.1.0 polls; v0.2.0 may add `streaming_tier` config to opt into native WSS.
- Per-symbol metadata (logo URL, description, contract addresses) — can use
  `extra` field stop-gap; first-class `get_symbol_metadata` deferred.
- Cross-adapter consistency checks (price-divergence detection) — application
  concern; package gives raw data only.
- Currency conversion outside provider-supported quote currencies — caller
  responsibility (paired with FX adapter chain).

**Out of this package permanently (lives elsewhere):**

- **Authenticated trading**, order placement, position management → `contriwork-exchange`.
- **Trading SDK wrappers** (e.g. `python-binance`, `ccxt`) → `contriwork-exchange`.
- News, sentiment, fundamental data → `contriwork-news` (separate Tier 1 package).
- Symbol normalization across providers (`BTC` ↔ `bitcoin` ↔ `BTCUSDT`) →
  caller responsibility. Each adapter accepts its native symbol format.
  Caller may build a symbol-mapping wrapper on top.
- Persistent storage of fetched data — caller responsibility.

---

## 4. Default adapter chains

Caller can override per-instance via `MarketDataClient(adapter_chains={...})`.
These defaults reflect "fastest free tier first, fallback to slower/paid":

| `market` | Default chain (Py) | Default chain (C# / TS) |
|---|---|---|
| `crypto` | Binance → CoinGecko → Kraken → Coinbase → CoinMarketCap → AlphaVantage | (same; YFinance not in chain) |
| `stocks_us` | Polygon → IEX → Finnhub → Tiingo → AlphaVantage → YFinance | Polygon → IEX → Finnhub → Tiingo → AlphaVantage |
| `stocks_tr` | YFinance → AlphaVantage | AlphaVantage |
| `stocks_eu` | YFinance → AlphaVantage | AlphaVantage |
| `stocks_global` | YFinance → AlphaVantage | AlphaVantage |
| `forex` | AlphaVantage → Polygon | (same) |
| `commodities` | YFinance → AlphaVantage | AlphaVantage |
| `indices` | YFinance → AlphaVantage | AlphaVantage |

**Caveat for C#/TS users on stocks_tr/eu/global/commodities/indices:** chain
collapses to a single adapter (Alpha Vantage). README + per-adapter docs
explicitly mention this so users know to provide an AV API key.

---

## 5. Streaming strategy

`subscribe_ticker(symbol, market, polling_fallback=True, polling_interval_s=4.0) → AsyncIterator[Ticker]`

| Adapter `Capability.supports_native_streaming` | `polling_fallback` | Behavior |
|:-:|:-:|---|
| `true` | n/a | Native WSS connection. |
| `false` | `true` | Package-level polling emulation: `get_spot` invoked every `polling_interval_s` seconds, results yielded as `Ticker`. |
| `false` | `false` | `STREAMING_NOT_SUPPORTED` raised at subscribe time. |

**Fallback chain in streaming:** unlike pull operations, streaming bound to a
single adapter at subscribe time (changing source mid-stream confuses
downstream consumers). The orchestrator picks the **first adapter in the
chain that satisfies the (native_or_fallback) constraint**. If subsequently
that adapter disconnects, the orchestrator reconnects to the same adapter
(exponential backoff up to `max_reconnect_attempts`); after exhaustion the
iterator raises `STREAM_DISCONNECTED`.

**Polling emulation guarantees**:
- `polling_interval_s` is the interval **between request starts** (skew-aware).
- If a request takes longer than `polling_interval_s`, no overlap — next
  request waits until current finishes (backpressure).
- If `get_spot` fails on an iteration, `Ticker` is **not** yielded; iterator
  retries on next interval. Three consecutive failures trigger
  `STREAM_DISCONNECTED`.
- Cancellation: closing the iterator (Py: `await aiter.aclose()`, C#:
  `IAsyncEnumerable.GetAsyncEnumerator().DisposeAsync()`, TS: AbortSignal)
  cleanly stops the loop.

**Per-language streaming primitives:**
- Python — `async def subscribe_ticker(...) -> AsyncIterator[Ticker]`.
  Native WSS via `websockets` library.
- C# — `IAsyncEnumerable<Ticker> SubscribeTickerAsync(..., CancellationToken ct)`.
  Native WSS via `System.Net.WebSockets.ClientWebSocket`.
- TypeScript — `async function* subscribeTicker(...) -> AsyncIterable<Ticker>`,
  `AbortSignal` for cancellation. Native WSS via `ws` library (Node 24
  doesn't have stable native WS client for the `ws://` protocol DEFLATE
  features used here; npm `ws` is dependency).

---

## 6. API key resolution

Adapter constructors accept dual credential sources:

```python
# Python
adapter = CoinGeckoAdapter(
    api_key="cg-demo-...",                      # static
    # OR
    api_key_provider=async_db_key_resolver,      # lazy callable
    tier="demo",
)
```

```csharp
// C#
var adapter = new CoinGeckoAdapter(
    apiKey: "cg-demo-...",
    apiKeyProvider: () => Task.FromResult<string?>(...),
    tier: "demo");
```

```typescript
// TS
const adapter = new CoinGeckoAdapter({
  apiKey: "cg-demo-...",
  apiKeyProvider: async () => "...",
  tier: "demo",
});
```

**Resolution rules:**
1. If `api_key_provider` is set, call it on each request that needs auth.
   Cache the value for the request only — do not memoize across requests
   (DB key may rotate).
2. If only `api_key` is set, use the static value.
3. If both are set, **provider wins** (allows runtime rotation).
4. If neither is set and the adapter requires auth, raise `MISSING_CREDENTIALS`
   on first call (lazy).
5. Adapters that do not require auth (Binance public, Kraken public, Coinbase
   public, yfinance, InMemory) ignore credential parameters with no error.

---

## 7. Error taxonomy (locked codes)

Stable across the v1 contract revision. Renaming a code = major bump.

| Code | When raised |
|---|---|
| `INVALID_INPUT` | Caller-side validation failure (empty symbol, negative limit, etc.) |
| `INVALID_INTERVAL` | `Interval` value not supported by the chosen adapter |
| `UNSUPPORTED_QUOTE_CURRENCY` | Adapter doesn't support requested `quote_currency` |
| `SYMBOL_NOT_FOUND` | Provider says it doesn't know the symbol |
| `RATE_LIMITED` | Adapter quota exhausted after retry exhaustion |
| `ADAPTER_UNAVAILABLE` | Adapter network/HTTP/parse failure (5xx, timeout, malformed) |
| `ADAPTER_FEATURE_NOT_SUPPORTED` | Adapter doesn't support requested operation (e.g. order book on CoinGecko) |
| `MISSING_CREDENTIALS` | Adapter needs auth, none provided/resolved |
| `NO_ADAPTER_FOR_MARKET` | No adapter in registry handles the requested `market` |
| `ALL_ADAPTERS_FAILED` | Every adapter in chain failed; aggregate `cause` lists per-adapter errors |
| `STREAMING_NOT_SUPPORTED` | Caller asked for `polling_fallback=False` and adapter has no native streaming |
| `STREAM_DISCONNECTED` | Active streaming subscription lost connection beyond reconnect budget |

---

## 8. Configuration model

Two levels: **adapter config** and **client config**.

### Adapter config (per-adapter, scoped)

```yaml
# example for an adapter
api_key: "..."                    # OR
api_key_provider: <callable>
tier: "demo" | "free" | "pro"     # adapter-specific
base_url: "..."                   # override default endpoint (testing)
timeout_s: 15.0
http_proxy: null
rate_limit_per_minute: null       # override Capability default
supports_native_streaming: null   # override Capability default (testing)
```

### Client config (orchestrator-wide)

```yaml
adapter_chains:
  crypto: [binance, coingecko, kraken, ...]
  stocks_us: [polygon, iex, ...]
  # ...

cache:
  enabled: false
  spot_ttl_s: 5
  ohlcv_ttl_s: 60
  order_book_ttl_s: 1
  max_entries: 10_000

rate_limit:
  enabled: true
  strategy: "bubble" | "fallthrough"  # default: fallthrough
  max_retry_attempts: 3
  initial_backoff_s: 0.5
  max_backoff_s: 30.0
  jitter: true

streaming:
  default_polling_interval_s: 4.0
  max_reconnect_attempts: 5
  reconnect_backoff_s: 2.0
```

Config keys, env var names, defaults are identical across languages
(`CONTRIWORK_MARKET_DATA_CACHE_SPOT_TTL_S`, etc.).

---

## 9. Versioning + invariants

- Method signatures grow additively only within v1 (new optional params OK,
  reorder/remove = major bump).
- Error codes never renamed within v1.
- Default cache TTLs (`5/60/1`) **invariant** within v1 (changing them = minor
  bump).
- `Capability` dict grows additively only.
- Default adapter chains **may** change within v1 (semver minor) — they are
  recommendations not contract; caller can always override.
- `extra` field key namespace (`<adapter_id>.<field>`) is invariant.

---

## 10. Out-of-band concerns

- **Determinism in tests**: contract-tests use `InMemoryAdapter` with
  pre-seeded data and a freezable clock injection. CI never hits real
  endpoints. Mock libraries: Py `respx` + `freezegun`, C# `WireMock.NET` +
  `Microsoft.Extensions.TimeProvider.Testing`, TS `msw` + `@sinonjs/fake-timers`.
- **Performance baseline**: `get_spot` p50 < 50 ms with cache miss + healthy
  adapter on local dev. Per-language perf tests not part of v0.1.0 release
  gate but tracked in `tests/perf/` (smoke harness).
- **Symbol normalization**: not in scope (§3). Each adapter accepts its
  native format. Caller wires the dictionary.
- **Free-tier API key requirements** (CI-relevant):
  - CoinGecko demo key — public env, low-risk
  - Alpha Vantage free key — 5/min, public env
  - Finnhub free key — public env
  - Tiingo free key — public env
  - IEX Cloud — sandbox key (no PII data)
  - Polygon.io — sandbox key
  - CoinMarketCap — basic free key
  - Binance / Kraken / Coinbase / yfinance — no key needed
  CI tests **mock all of these**; real keys not used in CI. See
  `tests/conftest.py` (Py), `tests/Fixtures/` (C#), `tests/setup.ts` (TS).

---

## 11. Open questions for the user

None at this stage — all design decisions are locked per
[`DW_AUDIT.md`](./DW_AUDIT.md) §10. If during implementation a new question
arises (e.g. an adapter's free-tier surface is more limited than expected),
it returns to this document as a Q before code lands.
