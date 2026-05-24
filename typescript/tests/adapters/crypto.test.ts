import { describe, expect, it } from "vitest";
import { Decimal } from "decimal.js";
import {
  AdapterFeatureNotSupportedError,
  Interval,
  InvalidIntervalError,
  MissingCredentialsError,
  RateLimitedError,
  SymbolNotFoundError,
} from "../../src/index.js";
import {
  BinancePublicAdapter,
  CoinbaseAdapter,
  CoinGeckoAdapter,
  CoinMarketCapAdapter,
  KrakenAdapter,
} from "../../src/adapters/index.js";
import { FakeFetch } from "./fake-fetch.js";

describe("CoinGeckoAdapter", () => {
  it("returns spot", async () => {
    const fake = new FakeFetch().respondTo("/simple/price", {
      bitcoin: {
        usd: 65000.5,
        usd_24h_change: 1.23,
        usd_24h_vol: 12345.6,
        usd_market_cap: 1_300_000_000_000,
        last_updated_at: 1714492800,
      },
    });
    const adapter = new CoinGeckoAdapter({ apiKey: "demo", fetchFn: fake.fn });
    const spot = await adapter.getSpot("bitcoin", "USD");
    expect(spot.last.equals(new Decimal("65000.5"))).toBe(true);
    expect(spot.sourceAdapter).toBe("coingecko");
  });

  it("maps empty payload to SymbolNotFound", async () => {
    const fake = new FakeFetch().respondTo("/simple/price", {});
    const adapter = new CoinGeckoAdapter({ fetchFn: fake.fn });
    await expect(adapter.getSpot("nope", "USD")).rejects.toBeInstanceOf(
      SymbolNotFoundError,
    );
  });

  it("maps 429 to RateLimited", async () => {
    const fake = new FakeFetch().respondTo("/simple/price", {}, 429);
    const adapter = new CoinGeckoAdapter({ fetchFn: fake.fn });
    await expect(adapter.getSpot("bitcoin", "USD")).rejects.toBeInstanceOf(
      RateLimitedError,
    );
  });

  it("filters ohlcv by since", async () => {
    const fake = new FakeFetch().respondTo("/coins/bitcoin/ohlc", [
      [1714492800000, 100, 101, 99, 100],
      [1714496400000, 100, 102, 99, 101],
      [1714500000000, 101, 105, 100, 104],
    ]);
    const adapter = new CoinGeckoAdapter({ fetchFn: fake.fn });
    const since = new Date(1714496400000);
    const candles = await adapter.getOhlcv("bitcoin", Interval.H1, since, 100);
    expect(candles).toHaveLength(2);
  });

  it("rejects unsupported interval", async () => {
    const adapter = new CoinGeckoAdapter();
    await expect(
      adapter.getOhlcv("bitcoin", Interval.M5, undefined, 10),
    ).rejects.toBeInstanceOf(InvalidIntervalError);
  });

  it("rejects order book", async () => {
    const adapter = new CoinGeckoAdapter();
    await expect(adapter.getOrderBook("bitcoin", 20)).rejects.toBeInstanceOf(
      AdapterFeatureNotSupportedError,
    );
  });
});

describe("BinancePublicAdapter", () => {
  it("returns spot", async () => {
    const fake = new FakeFetch().respondTo("/api/v3/ticker/24hr", {
      symbol: "BTCUSDT",
      lastPrice: "65000.10",
      bidPrice: "64999.99",
      askPrice: "65000.21",
      highPrice: "66000.00",
      lowPrice: "64000.00",
      quoteVolume: "1234567.89",
      priceChangePercent: "1.45",
      prevClosePrice: "64999.00",
      closeTime: 1714492800000,
    });
    const adapter = new BinancePublicAdapter({ fetchFn: fake.fn });
    const spot = await adapter.getSpot("BTCUSDT", "USDT");
    expect(spot.last.equals(new Decimal("65000.10"))).toBe(true);
    expect(spot.bid?.equals(new Decimal("64999.99"))).toBe(true);
  });

  it("maps -1121 envelope to SymbolNotFound", async () => {
    const fake = new FakeFetch().respondTo("/api/v3/ticker/24hr", {
      code: -1121,
      msg: "Invalid symbol.",
    });
    const adapter = new BinancePublicAdapter({ fetchFn: fake.fn });
    await expect(adapter.getSpot("ZZZUSDT", "USDT")).rejects.toBeInstanceOf(
      SymbolNotFoundError,
    );
  });

  it("returns sorted order book", async () => {
    const fake = new FakeFetch().respondTo("/api/v3/depth", {
      lastUpdateId: 42,
      bids: [
        ["64999.0", "1.0"],
        ["64998.0", "2.0"],
      ],
      asks: [
        ["65001.0", "1.0"],
        ["65002.0", "0.5"],
      ],
    });
    const adapter = new BinancePublicAdapter({ fetchFn: fake.fn });
    const book = await adapter.getOrderBook("BTCUSDT", 2);
    expect(book.sequence).toBe(42);
    expect(book.bids[0]?.price.gt(book.bids[1]?.price as Decimal)).toBe(true);
  });
});

describe("KrakenAdapter", () => {
  it("returns spot", async () => {
    const fake = new FakeFetch().respondTo("/0/public/Ticker", {
      error: [],
      result: {
        XXBTZUSD: {
          a: ["65010.00", "1", "1.000"],
          b: ["64990.00", "1", "1.000"],
          c: ["65000.00", "0.50"],
          v: ["1.0", "10.0"],
          h: ["66000.00", "66200.00"],
          l: ["64000.00", "63500.00"],
        },
      },
    });
    const adapter = new KrakenAdapter({ fetchFn: fake.fn });
    const spot = await adapter.getSpot("XXBTZUSD", "USD");
    expect(spot.last.equals(new Decimal("65000.00"))).toBe(true);
    expect(spot.high24h?.equals(new Decimal("66200.00"))).toBe(true);
  });

  it("maps unknown pair to SymbolNotFound", async () => {
    const fake = new FakeFetch().respondTo("/0/public/Ticker", {
      error: ["EQuery:Unknown asset pair"],
      result: {},
    });
    const adapter = new KrakenAdapter({ fetchFn: fake.fn });
    await expect(adapter.getSpot("NOPE", "USD")).rejects.toBeInstanceOf(
      SymbolNotFoundError,
    );
  });
});

describe("CoinbaseAdapter", () => {
  it("returns spot", async () => {
    const fake = new FakeFetch()
      .respondTo("/products/BTC-USD/ticker", {
        price: "65000.10",
        bid: "64999.50",
        ask: "65000.50",
        time: "2026-04-30T12:00:00.000Z",
      })
      .respondTo("/products/BTC-USD/stats", {
        high: "66000",
        low: "64000",
        volume: "12345.6",
      });
    const adapter = new CoinbaseAdapter({ fetchFn: fake.fn });
    const spot = await adapter.getSpot("BTC-USD", "USD");
    expect(spot.last.equals(new Decimal("65000.10"))).toBe(true);
    expect(spot.high24h?.equals(new Decimal("66000"))).toBe(true);
  });

  it("reverses descending candles", async () => {
    const fake = new FakeFetch().respondTo("/products/BTC-USD/candles", [
      [1714492920, 65000, 65200, 65050, 65180, 12.0],
      [1714492860, 64900, 65100, 65000, 65050, 10.5],
    ]);
    const adapter = new CoinbaseAdapter({ fetchFn: fake.fn });
    const candles = await adapter.getOhlcv(
      "BTC-USD",
      Interval.M1,
      undefined,
      2,
    );
    expect(candles[0]?.timestamp.getTime()).toBeLessThan(
      candles[1]?.timestamp.getTime() as number,
    );
  });

  it("maps 404 to SymbolNotFound", async () => {
    const fake = new FakeFetch(); // no routes → 404
    const adapter = new CoinbaseAdapter({ fetchFn: fake.fn });
    await expect(adapter.getSpot("ZZZ-USD", "USD")).rejects.toBeInstanceOf(
      SymbolNotFoundError,
    );
  });
});

describe("CoinMarketCapAdapter", () => {
  const payload = {
    data: {
      BTC: [
        {
          id: 1,
          quote: {
            USD: {
              price: 65000.12,
              volume_24h: 12345.6,
              percent_change_24h: 1.23,
              market_cap: 1.3e12,
              last_updated: "2026-04-30T12:00:00.000Z",
            },
          },
        },
      ],
    },
  };

  it("returns spot and sends the API key header", async () => {
    const fake = new FakeFetch().respondTo(
      "/v2/cryptocurrency/quotes/latest",
      payload,
    );
    const adapter = new CoinMarketCapAdapter({
      apiKey: "test-key",
      fetchFn: fake.fn,
    });
    const spot = await adapter.getSpot("BTC", "USD");
    expect(spot.last.equals(new Decimal("65000.12"))).toBe(true);
    expect(fake.lastHeaders?.["X-CMC_PRO_API_KEY"]).toBe("test-key");
  });

  it("throws MissingCredentials without a key", async () => {
    const adapter = new CoinMarketCapAdapter();
    await expect(adapter.getSpot("BTC", "USD")).rejects.toBeInstanceOf(
      MissingCredentialsError,
    );
  });

  it("api key provider overrides static key", async () => {
    const fake = new FakeFetch().respondTo(
      "/v2/cryptocurrency/quotes/latest",
      payload,
    );
    const adapter = new CoinMarketCapAdapter({
      apiKey: "static",
      apiKeyProvider: (): Promise<string> => Promise.resolve("provider-key"),
      fetchFn: fake.fn,
    });
    await adapter.getSpot("BTC", "USD");
    expect(fake.lastHeaders?.["X-CMC_PRO_API_KEY"]).toBe("provider-key");
  });
});
