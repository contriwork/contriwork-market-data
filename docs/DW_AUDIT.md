# DW Market-Data Surface Audit

**Audit tarihi**: 2026-04-30
**DW path** (canonical): `/Users/emirguvenc/Task/Digital Worker/digital-worker`
**Hedef**: `contriwork-market-data` v0.1.0 paketinin DW'nin gerçek market-data
yüzeyine ne kadar oturduğunu doğrulamak; v0.1.0 adapter MVP scope'unu kanıta
dayandırmak; `services/exchange_provider.py` gibi market-data dışı modüllerin
kapsam dışı olduğunu netleştirmek.

> Bu rapor scope ve contract kararlarına temel oluşturur. Kod yazılmadan önce
> kullanıcı onayı şart (`docs/SCOPE.md` + `CONTRACT.md` zincirinde ilk halka).

---

## 1. Hedef dosyalar (paketin ezeceği surface)

| Dosya | Satır | Görev | İçindeki provider çağrıları |
|---|---:|---|---|
| `backend/agents/market_agent.py` | 294 | Crypto fiyatları (BTC, ETH) | CoinGecko (primary), Alpha Vantage (fallback) |
| `backend/agents/bist_agent.py` | 234 | BIST hisse fiyatları | yfinance (primary), Alpha Vantage `<SYM>.IST` (fallback) |
| `backend/services/price_stream.py` | 340 | Subscription + snapshot cache + multi-source fetch | Binance public REST (primary, batch ticker), CoinGecko (fallback via MarketAgent), yfinance (BIST via BISTAgent) |

**Toplam**: 868 satır. Paket consume edildikten sonra net silinebilecek miktar
**~500 satır civarı** (network-fetch helper'lar). `price_stream.py`'ın subscription
yönetimi + WebSocket relay kısmı (≈ 200 satır) DW'nin uygulama-seviyesi
sorumluluğu olarak kalır — paket bunu yapmaz.

---

## 2. Ham bulgu — `market_agent.py`

- `MarketAgent` class — async, per-call `httpx.AsyncClient(timeout=15.0)` (yeni
  client her çağrıda; connection pool yok).
- API key kaynağı: `await get_coingecko_key()` / `await get_alphavantage_key()` —
  her ikisi de **DB-backed async resolver** (ExchangeConfig tablosu →
  bulunamazsa `.env` fallback). Paket için kritik tasarım kararı: adapter'lar
  hem static `api_key: str | None` hem de `api_key_provider: AsyncCallable[[],
  str | None]` desteklemeli; provider varsa o kazanır.
- Methods: `get_crypto_prices(coins)`, `get_coin_details(coin_id)`,
  `check_api_limits()`. Sadece `get_crypto_prices` core fetcher; diğer ikisi
  provider-spesifik tooling.
- Symbol map: `bitcoin → BTC`, `ethereum → ETH` (yalnızca 2 kayıt). Kapsam
  genişlemediği için sözlük caller'da kalmalı (paket symbol normalization
  yapmaz — brief teyitli).
- CoinGecko endpoint: `simple/price?ids=...&vs_currencies=usd,try`. Yani DW
  **iki para birimi** (USD + TRY) tek çağrıda alıyor. Bu paket için önemli:
  `get_spot(symbol, market, quote_currency="USD")` veya çoklu-currency
  desteği? — bkz. §6 tasarım sorusu.
- Cache yok, rate limiter yok. CoinGecko rate-limit header'ları yalnızca
  `check_api_limits` ile **sergileniyor** (zorlanmıyor).
- Fallback: CoinGecko başarısızsa AlphaVantage'a düşüyor; şu an **per-coin
  loop** (AV crypto için 2 ayrı USD ve TRY çağrısı = N coin × 2 çağrı). Free
  tier 5 req/min limitini sessizce aşma riski var.

---

## 3. Ham bulgu — `bist_agent.py`

- `BISTAgent` class — yfinance sync olduğu için `ThreadPoolExecutor(max_workers=2)`
  ile sarmalı.
- 10 default sembol (XU100, THYAO, GARAN, AKBNK, SISE, KCHOL, EREGL, BIMAS,
  ASELS, TUPRS — `.IS` suffix).
- Her sembol için `yf.Ticker(sym).history(period="5d")` çağrısı; `yf.download`'ın
  bazı sürümlerde `.IS` ile boş JSON döndürdüğü için kasten history kullanılmış.
- AV fallback: yfinance'in başaramadığı semboller için `GLOBAL_QUOTE` API,
  symbol formatı `<TICKER>.IST` (yfinance'in `.IS`'inden farklı).
- `BISTStock` model: price (TRY), change_percent, volume, high, low,
  previous_close — yani günlük high/low **spot fiyatla birlikte** geliyor.
- Cache yok; rate limiter yok. yfinance scraping olduğu için sessiz IP
  throttling riski mevcut ama DW bunu görmezden geliyor.

---

## 4. Ham bulgu — `price_stream.py`

- **Streaming = app-level polling**, upstream WebSocket **yok**. Polling loop
  (`while True: fetch_prices(); sleep(4)`) `main.py:4929+` (WebSocket
  handler'ı) içinde. Yani `price_stream.py` sadece "tek tick fiyat çekici"
  + "subscription/cache state".
- Crypto chain: **Binance public REST `/ticker/24hr`** (primary, batch JSON
  array param ile tek istekte birden çok sembol) → CoinGecko (eksikleri
  tamamlamak için, MarketAgent üzerinden) → Binance verisi varsa CoinGecko'dan
  TRY fiyat takviyesi (Binance USDT-only).
- BIST chain: yfinance (BISTAgent kullanılarak).
- `_fetch_binance_tickers` private helper'ı `trade_pipeline.py:603` tarafından
  **doğrudan import** ediliyor — leaky abstraction; paket consume edildikten
  sonra düzelecek.
- `PriceSnapshot` dataclass: symbol, market ("crypto"|"bist"), price_usd,
  price_try, change_24h, volume_24h, high, low, updated_at, source. Bu paketin
  `SpotPrice` modeline benzer ama daha zengin.
- `PriceStreamService` singleton: `_subscriptions` (ws_id → set of keys),
  `_latest_prices` (cache-like store). **Bu kısım paketin değil DW'nin
  sorumluluğu** — paket consume edildikten sonra burada kalacak.

### Streaming hakkında karar

DW'nin "live price" sistemi:
- FE → BE arası **WebSocket** (`ws_price_stream` handler, `main.py:4920+`).
- BE → upstream provider arası **HTTP polling** (4 sn interval).

Paket bu nedenle `subscribe_ticker(symbol, market) → AsyncIterator[Ticker]`
metodunu **v0.1.0'da implement etmek zorunda değil**. DW'nin polling loop'u
package-level değil, application-level. v0.1.0'da `subscribe_ticker`
CONTRACT'ta tanımlı kalır, gövde `NotImplementedError` raise eder; v0.2.0
gerçek WebSocket implementation'ı (Binance WSS, CoinGecko Pro WSS, vb.)
geldiğinde aynı imzayla doldurulur — caller breakage olmaz.

---

## 5. Provider grep özeti — DW genelinde gerçekte hangileri çağrılıyor?

```
backend/agents/market_agent.py        — coingecko, alphavantage
backend/agents/bist_agent.py          — yfinance, alphavantage
backend/services/price_stream.py      — binance (public REST), coingecko, yfinance
backend/crew/tools.py                 — MarketAgent + BISTAgent (proxy)
backend/mcp/market_server.py          — MarketAgent + BISTAgent (proxy)
backend/services/trade_pipeline.py    — _fetch_binance_tickers (leaky import)
backend/services/exchange_provider.py — binance (auth'lu, python-binance SDK) — TRADING, MARKET DATA DEĞİL
backend/services/portfolio_snapshot_service.py — provider isim string'i, çağrı yok
backend/database/* — provider config tabloları, çağrı yok
backend/config/settings.py — base URL'ler ve key resolver'lar
```

**Aktif kullanılan public market-data provider'lar (4):**
- ✅ CoinGecko (crypto)
- ✅ Binance public REST (crypto)
- ✅ Alpha Vantage (crypto + BIST fallback)
- ✅ yfinance (BIST + global stock fallback amaçlı)

**Roadmap §4.3'te listelenip DW'de KULLANILMAYAN 7 provider:**
- ❌ CoinMarketCap — kayıt var, kod yok
- ❌ Kraken — kayıt var, kod yok
- ❌ Coinbase — kayıt var, kod yok
- ❌ Finnhub — kayıt var, kod yok
- ❌ IEX Cloud — kayıt var, kod yok
- ❌ Polygon.io — kayıt var, kod yok
- ❌ Tiingo — kayıt var, kod yok

> Brief'in "v0.1.0 = DW'nin gerçekten kullandığı 3-4 adapter, 11 değil" kuralı
> bu bulguyla doğrulanmış oluyor (CLAUDE.md "over-engineering yasak" + brief
> "Defer to v0.2.0+" disiplini).

**Trading SDK'sı (kapsam dışı):** `python-binance` (auth'lu, order placement)
`services/exchange_provider.py`'da; bu ileride `contriwork-exchange` paketinin
işi. Karıştırılmamalı.

---

## 6. Tasarım soruları (SCOPE / CONTRACT öncesi netleşmesi gerek)

### Q1 — Quote currency desteği: `quote_currency` parametresi?
- DW iki para birimi kullanıyor: USD (crypto USDT proxy'si) + TRY (BIST + crypto
  TRY).
- CoinGecko `vs_currencies=usd,try` ile **tek çağrıda iki currency** verir.
- Binance USDT-only.
- Alpha Vantage `to_currency` param'ı destekler (her çağrı bir hedef).
- yfinance native currency (BIST için TRY).

**Önerim**: `get_spot(symbol, market, quote_currency: str = "USD")` parametresi
eklenir; her adapter `Capability.supported_quote_currencies: list[str]` ile
hangi currency'leri desteklediğini bildirir; `quote_currency` desteklenmiyorsa
`UNSUPPORTED_QUOTE_CURRENCY` error code. CoinGecko hangi currency'yi
istersen veriyor; Binance "USDT" kabul eder, başka şey için fallback adapter'a
geçer; yfinance native (USD/TRY otomatik); AV explicit `to_currency`.

### Q2 — `SpotPrice` field zenginliği
- DW `CryptoPrice`/`BISTStock` field'ları: price_usd, price_try, change_24h,
  volume_24h, market_cap (crypto), high, low, previous_close (BIST).

**Önerim** (v0.1.0):
```
SpotPrice: {
  symbol: str,
  last: Decimal,
  quote_currency: str,
  bid: Decimal | None,
  ask: Decimal | None,
  high_24h: Decimal | None,
  low_24h: Decimal | None,
  volume_24h: Decimal | None,
  change_24h_pct: Decimal | None,
  timestamp: datetime,
  source_adapter: str,
}
```
`market_cap` ve `previous_close` v0.1.0'a girmez (over-engineering); v0.2.0'da
gerek olursa eklenir.

### Q3 — API key dynamic resolution
DW'nin DB-backed async key okumasını kırmamak için **adapter constructor
hem `api_key: str | None` hem `api_key_provider: AsyncCallable | None` alır,
provider varsa öncelikli**. C#'ta `Func<Task<string?>>`, TS'te
`() => Promise<string | null>`.

### Q4 — `market` enum değerleri
**Önerim**:
- `"crypto"` — chain default: CoinGecko + Binance (Binance primary, CG fallback) veya tersi
- `"stocks_us"` — Alpha Vantage (DW şu an aktif kullanmıyor ama spec'lenebilir)
- `"stocks_tr"` — yfinance + Alpha Vantage `.IST` (yfinance primary, AV fallback)

DW'nin signal modelinde `MarketType.CRYPTO` ve `MarketType.BIST` enum'ları var;
paket consume edildiğinde DW wrapper'ı `bist → "stocks_tr"` mapping'i tutar.

### Q5 — Adapter chain default'ları
Brief'in "AdapterRegistry: market → ordered list[Adapter]" pattern'i için
v0.1.0 default'ları:

| `market` | Default chain |
|---|---|
| `"crypto"` | `[BinancePublicAdapter, CoinGeckoAdapter, AlphaVantageAdapter]` (Binance daha hızlı; CG TRY için fallback olmalı; AV fallback) |
| `"stocks_us"` | `[AlphaVantageAdapter]` (DW kullanmıyor; Py YFinance var ama opt-in) |
| `"stocks_tr"` | `[YFinanceAdapter (Py-only), AlphaVantageAdapter]` |

C#/TS'te `stocks_tr` chain'inde YFinance yok → tek adapter (AV); SCOPE.md
bunu açıkça yazmalı.

### Q6 — Cache TTL default'ları
Brief: spot=5s, ohlcv=60s, order_book=1s. Ama **default disabled** (config flag
ile opt-in). DW şu an cache yok, ekledikten sonra A/B karşılaştırılabilir.

### Q7 — Rate limiter behavior
Brief: aşımda backoff + retry (drop yerine). DW şu an manuel rate limiting
yapmıyor — AV 5 req/min limitini sessizce aşıyor olabilir. Token-bucket per
adapter çok değerli olur. `Capability.rate_limit_per_minute` adapter
tarafından bildirilir; client orchestrator bunu enforce eder.

---

## 7. Cleanup target — paket consume edildikten sonra DW'de ne silinir?

| Dosya | v0.1.0 sonrası | Notu |
|---|---|---|
| `backend/agents/market_agent.py` | **silinir** (294 satır) | Tamamı paket çağrılarıyla yer değişir |
| `backend/agents/bist_agent.py` | **silinir** (234 satır) | Tamamı paket çağrılarıyla yer değişir |
| `backend/services/price_stream.py` | **kısmen silinir** (~150 satır) | `_fetch_binance_tickers` + `_fetch_coingecko_prices` helper'ları gider; `PriceStreamService` (subscription + cache) kalır, sadece içindeki fetch çağrıları `MarketDataClient.get_spot(market="crypto")`'ye yer değişir. ~190 satır subscription/cache state kalır. |
| `backend/services/trade_pipeline.py:603-605` | **fix** | Leaky `_fetch_binance_tickers` import yerine `MarketDataClient.get_spot(symbol, market="crypto")` |
| `backend/crew/tools.py` | **fix** | `MarketAgent`/`BISTAgent` instantiation yerine `MarketDataClient` |
| `backend/mcp/market_server.py` | **fix** | aynı |
| `backend/main.py:342-343` | **fix** | Module-level `MarketAgent()`/`BISTAgent()` yerine `MarketDataClient` (lifespan'de init edilir) |
| `backend/services/exchange_provider.py` | **dokunulmaz** | Trading SDK; `contriwork-exchange` paketinin işi |
| `backend/models/signals.py:CryptoPrice/BISTStock` | **adapter** | DW domain modelleri, paketin `SpotPrice`'ından dönüştürülür (mapping fonksiyonu DW wrapper'ında) |

**Beklenen LOC delta**: ~528 satır direct deletion + ~170 satır `services/price_stream.py`'da
(toplamda **~700 satır eski kod erir**, paketin DW içindeki wrapper +
mapping kodu yaklaşık **150-200 satır** eklenir → net delta **-500 ile -550 satır
arasında**).

---

## 8. Kritik gotcha listesi (config-core + notifications PR'larından)

DW consume PR'ında dikkat:

1. **Async key resolver kırılmasın**: `get_coingecko_key()` async; adapter
   constructor sync olabilir (kullanıcı tarafı). Adapter'ın **lazy** key
   resolution yapması şart (constructor'da şipşak çağırma yok).
2. **httpx connection pool**: DW şu an her çağrıda yeni client kuruyor. Paket
   shared async client + retry transport kullanmalı (performans iyileştirmesi).
3. **yfinance Python-only**: SCOPE.md ve README açıkça yazmalı. C#/TS
   kullanıcısı `stocks_tr` market'inde YFinanceAdapter beklememeli; sadece
   AlphaVantage var.
4. **AV symbol formatı farkı**: yfinance `.IS` ↔ AV `.IST`. Symbol
   normalization paketin işi değil ama her iki adapter ayrı ayrı doğru formatı
   kabul etmeli; DW wrapper'ı routing yapar.
5. **Decimal vs float**: DW `float` kullanıyor; paket `Decimal` (Py),
   `decimal` (C#), `string`-encoded number (TS) tercih etmeli. Mapping
   sırasında precision kaybı olmamalı (`Decimal(str(value))` Python'da).
6. **Binance batch endpoint quirks**: Tek sembolde `?symbol=`, çoklu sembolde
   `?symbols=` JSON array (URL-encoded). Adapter bu farkı içselleştirmeli.
7. **CoinGecko demo key header**: `x-cg-demo-api-key` (pro key başka
   header). Free vs Demo vs Pro key route'ları farklı; adapter `tier` config
   ile davransın (default = demo).

---

## 9. Sonraki adımlar

1. Bu raporu kullanıcı onaylar.
2. `docs/SCOPE.md` yazılır (adapter MVP scope + method scope + Q1-Q7 cevapları).
3. Kullanıcı SCOPE'u onaylar.
4. `CONTRACT.md` v1 yazılır (Q1-Q7 kararları contract'ta donar).
5. Kullanıcı CONTRACT'ı onaylar.
6. PR 1 (Foundation) açılır.

> Bu zincirde herhangi bir adımı atlamak v0.2.0'da geri dönmesi pahalı olan
> tasarım hatalarına yol açar (notifications + config-core deneyiminden
> öğrendik).

---

## 10. Locked decisions (2026-04-30 kullanıcı onayıyla)

§5'in "DW'de kullanılmayan 7 provider'ı v0.2.0+'a defer" önerisi **iptal**;
§6'nın Q1-Q7 default'ları kullanıcı kararıyla aşağıdaki şekilde donmuştur.
Bu bölüm artık tek gerçek kaynaktır — `docs/SCOPE.md` ve `CONTRACT.md` bunu
referans alır.

### 10.1 Adapter scope = 11 adapter v0.1.0'da

DW kullanımı önemli değil — paket tam (PACKAGES_ROADMAP §4.3 listesi):

| Adapter | Py | C# | TS | Asset class(es) |
|---|:-:|:-:|:-:|---|
| InMemoryAdapter | ✅ | ✅ | ✅ | All (test/reference) |
| CoinGeckoAdapter | ✅ | ✅ | ✅ | crypto |
| CoinMarketCapAdapter | ✅ | ✅ | ✅ | crypto |
| BinancePublicAdapter | ✅ | ✅ | ✅ | crypto |
| KrakenAdapter | ✅ | ✅ | ✅ | crypto |
| CoinbaseAdapter | ✅ | ✅ | ✅ | crypto |
| AlphaVantageAdapter | ✅ | ✅ | ✅ | crypto + stocks_us + stocks_tr + forex |
| FinnhubAdapter | ✅ | ✅ | ✅ | stocks_us |
| IEXCloudAdapter | ✅ | ✅ | ✅ | stocks_us |
| PolygonIOAdapter | ✅ | ✅ | ✅ | stocks_us + forex |
| TiingoAdapter | ✅ | ✅ | ✅ | stocks_us |
| YFinanceAdapter | ✅ | ❌ | ❌ | stocks_us + stocks_tr + stocks_eu + stocks_global + commodities + indices |

Toplam: 11 public-data adapter + InMemory = **12 adapter Py'de, 11 C#/TS'de**.

YFinance C#/TS'de **yok** (kullanıcı kararı, brief E maddesi). C#/TS
kullanıcıları `stocks_tr` / `stocks_eu` / `stocks_global` / `commodities` /
`indices` için **Alpha Vantage** kullanır. SCOPE.md ve README açıkça yazar.

### 10.2 Method scope = 4 metod v0.1.0'da

| Metod | Durum |
|---|---|
| `get_spot(symbol, market, quote_currency="USD")` | ✅ implement |
| `get_ohlcv(symbol, market, interval, since, limit)` | ✅ implement |
| `get_order_book(symbol, market, depth=20)` | ✅ implement (destekleyen adapter'lar) |
| `subscribe_ticker(symbol, market, polling_fallback=True, polling_interval_s=4.0) → AsyncIterator[Ticker]` | ✅ implement (native WS varsa WS, yoksa polling fallback) |

`get_order_book` **v0.1.0'da implement edilir**. Desteklemeyen adapter'lar
(CoinGecko, CMC, Alpha Vantage, Tiingo, yfinance vb.) `Capability.supports_order_book=false`
bildirir; çağrıldığında `ADAPTER_FEATURE_NOT_SUPPORTED` raise eder; client
fallback chain'inde sıradaki adapter'ı dener.

`subscribe_ticker` **v0.1.0'da tüm adapter'larda implement edilir**:
- `Capability.supports_native_streaming=true` olanlar (Binance, Kraken,
  Coinbase, CMC Pro, CoinGecko Pro): native WSS bağlantısı.
- Diğerleri: paket-level polling emülasyonu (`get_spot` her
  `polling_interval_s` saniyede tetiklenir, sonuç `Ticker` olarak yield
  edilir).
- Caller `polling_fallback=False` derse, native yoksa
  `STREAMING_NOT_SUPPORTED` raise.

### 10.3 Karar matrisi — Q1-Q7

| Q | Karar |
|---|---|
| Q1 — Quote currency | `quote_currency: str = "USD"` parametresi; `Capability.supported_quote_currencies: list[str] \| "ANY"`; desteklenmiyorsa `UNSUPPORTED_QUOTE_CURRENCY` |
| Q2 — Data type field zenginliği | Core required + standard optional + `extra: Mapping[str, Any]` provider-spesifik (immutable). Hangi adapter hangi optional'ı doldurur Capability'de bildirilmez (best-effort) |
| Q3 — API key dynamic resolution | Adapter constructor `api_key: str \| None` + `api_key_provider: AsyncCallable \| None`; provider varsa öncelikli; lazy resolution |
| Q4 — Market parametresi | `market: str` (enum değil); paket "well-known" liste verir (`crypto`, `stocks_us`, `stocks_tr`, `stocks_eu`, `stocks_global`, `forex`, `commodities`, `indices`); caller başka string verebilir; `Capability.supported_markets` adapter bildirir |
| Q5 — Default chain (caller override edebilir) | bkz. §10.4 tablosu |
| Q6 — Cache TTL | spot=5s, ohlcv=60s, order_book=1s; **default disabled**, opt-in; TTL'ler caller config'le override edilebilir |
| Q7 — Rate limit aşımı | backoff + retry (drop yerine); `Capability.rate_limit_per_minute` adapter bildirir; max_attempts dolarsa `RATE_LIMITED` bubble up |

### 10.4 Default adapter chain'leri

| `market` | Chain (ilk-success-wins) |
|---|---|
| `crypto` | Binance → CoinGecko → Kraken → Coinbase → CoinMarketCap → AlphaVantage |
| `stocks_us` | Polygon → IEX → Finnhub → Tiingo → AlphaVantage → YFinance(Py) |
| `stocks_tr` | YFinance(Py) → AlphaVantage |
| `stocks_eu` | YFinance(Py) → AlphaVantage |
| `stocks_global` | YFinance(Py) → AlphaVantage |
| `forex` | AlphaVantage → Polygon |
| `commodities` | YFinance(Py) → AlphaVantage |
| `indices` | YFinance(Py) → AlphaVantage |

C#/TS'te YFinance yok → o chain'lerde YFinance düşer, kalan adapter'lar
çalışır (örn. `stocks_tr` C#/TS'te sadece AlphaVantage).

### 10.5 PR planı (revize, 11 PR)

| # | Branch | İçerik |
|---|---|---|
| 1 | `scaffold/foundation` | rename placeholders + Docker pin + metadata + DW_AUDIT + SCOPE + CONTRACT + fixtures |
| 2 | `feat/python-core` | Port + types + MarketDataClient + InMemoryAdapter + cache + rate limiter + streaming framework + tests |
| 3 | `feat/python-crypto` | CoinGecko + Binance + Kraken + Coinbase + CMC + per-adapter unit tests + contract-tests coverage |
| 4 | `feat/python-stocks` | AlphaVantage + Finnhub + IEX + Polygon + Tiingo + YFinance + tests |
| 5 | `feat/csharp-core` | (aynı yapı, IMarketDataPort + records + DI) |
| 6 | `feat/csharp-crypto` | 5 adapter (YFinance hariç) |
| 7 | `feat/csharp-stocks` | 5 adapter |
| 8 | `feat/typescript-core` | (aynı yapı + strategy.md = pure-TS reimpl) |
| 9 | `feat/typescript-crypto` | 5 adapter |
| 10 | `feat/typescript-stocks` | 5 adapter |
| 11 | `release/0.1.0` | VERSION + VERSION_MATRIX + CHANGELOG → tag → 4 workflow |
