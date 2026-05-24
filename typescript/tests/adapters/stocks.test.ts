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
  AlphaVantageAdapter,
  FinnhubAdapter,
  IEXCloudAdapter,
  PolygonIOAdapter,
  TiingoAdapter,
} from "../../src/adapters/index.js";
import { FakeFetch } from "./fake-fetch.js";

describe("AlphaVantageAdapter", () => {
  it("returns a global quote", async () => {
    const fake = new FakeFetch().respondTo("function=GLOBAL_QUOTE", {
      "Global Quote": {
        "05. price": "199.99",
        "03. high": "200.10",
        "04. low": "198.50",
        "06. volume": "1234567",
        "08. previous close": "199.50",
        "10. change percent": "0.24%",
      },
    });
    const adapter = new AlphaVantageAdapter({
      apiKey: "test",
      fetchFn: fake.fn,
    });
    const spot = await adapter.getSpot("AAPL", "USD");
    expect(spot.last.equals(new Decimal("199.99"))).toBe(true);
    expect(spot.change24hPct?.equals(new Decimal("0.24"))).toBe(true);
  });

  it("returns a currency exchange rate", async () => {
    const fake = new FakeFetch().respondTo("function=CURRENCY_EXCHANGE_RATE", {
      "Realtime Currency Exchange Rate": {
        "5. Exchange Rate": "65000.0",
        "8. Bid Price": "64990.0",
        "9. Ask Price": "65010.0",
      },
    });
    const adapter = new AlphaVantageAdapter({
      apiKey: "test",
      fetchFn: fake.fn,
    });
    const spot = await adapter.getSpot("BTC", "USD");
    expect(spot.last.equals(new Decimal("65000.0"))).toBe(true);
  });

  it("maps a throttle note to RateLimited", async () => {
    const fake = new FakeFetch().respondTo("function=GLOBAL_QUOTE", {
      Note: "Thank you for using Alpha Vantage! Our standard API rate limit is 5 calls per minute.",
    });
    const adapter = new AlphaVantageAdapter({
      apiKey: "test",
      fetchFn: fake.fn,
    });
    await expect(adapter.getSpot("AAPL", "USD")).rejects.toBeInstanceOf(
      RateLimitedError,
    );
  });

  it("requires a credential", async () => {
    const adapter = new AlphaVantageAdapter();
    await expect(adapter.getSpot("AAPL", "USD")).rejects.toBeInstanceOf(
      MissingCredentialsError,
    );
  });

  it("returns daily candles", async () => {
    const fake = new FakeFetch().respondTo("function=TIME_SERIES_DAILY", {
      "Time Series (Daily)": {
        "2026-04-30": {
          "1. open": "199.0",
          "2. high": "200.0",
          "3. low": "198.0",
          "4. close": "199.5",
          "5. volume": "1234567",
        },
        "2026-04-29": {
          "1. open": "198.0",
          "2. high": "199.0",
          "3. low": "197.0",
          "4. close": "198.5",
          "5. volume": "1100000",
        },
      },
    });
    const adapter = new AlphaVantageAdapter({
      apiKey: "test",
      fetchFn: fake.fn,
    });
    const candles = await adapter.getOhlcv("AAPL", Interval.D1, undefined, 100);
    expect(candles).toHaveLength(2);
    expect(candles[0]?.timestamp.getTime()).toBeLessThan(
      candles[1]?.timestamp.getTime() as number,
    );
  });
});

describe("FinnhubAdapter", () => {
  it("returns spot", async () => {
    const fake = new FakeFetch().respondTo("/api/v1/quote", {
      c: 199.99,
      h: 200.1,
      l: 198.5,
      pc: 199.5,
      dp: 0.24,
      t: 1714492800,
    });
    const adapter = new FinnhubAdapter({ apiKey: "test", fetchFn: fake.fn });
    const spot = await adapter.getSpot("AAPL", "USD");
    expect(spot.last.equals(new Decimal("199.99"))).toBe(true);
  });

  it("maps c=0 to SymbolNotFound", async () => {
    const fake = new FakeFetch().respondTo("/api/v1/quote", { c: 0, t: 0 });
    const adapter = new FinnhubAdapter({ apiKey: "test", fetchFn: fake.fn });
    await expect(adapter.getSpot("ZZZZ", "USD")).rejects.toBeInstanceOf(
      SymbolNotFoundError,
    );
  });

  it("returns candles", async () => {
    const fake = new FakeFetch().respondTo("/api/v1/stock/candle", {
      s: "ok",
      c: [199.5, 199.8],
      h: [200, 200.2],
      l: [199, 199.4],
      o: [199, 199.5],
      t: [1714492800, 1714492860],
      v: [1000, 1500],
    });
    const adapter = new FinnhubAdapter({ apiKey: "test", fetchFn: fake.fn });
    const candles = await adapter.getOhlcv("AAPL", Interval.M1, undefined, 10);
    expect(candles).toHaveLength(2);
  });
});

describe("IEXCloudAdapter", () => {
  it("returns spot", async () => {
    const fake = new FakeFetch().respondTo("/stable/stock/AAPL/quote", {
      latestPrice: 199.99,
      latestUpdate: 1714492800000,
      high: 200.1,
      low: 198.5,
      marketCap: 3_000_000_000_000,
    });
    const adapter = new IEXCloudAdapter({ apiKey: "test", fetchFn: fake.fn });
    const spot = await adapter.getSpot("AAPL", "USD");
    expect(spot.last.equals(new Decimal("199.99"))).toBe(true);
    expect(spot.marketCap?.equals(new Decimal("3000000000000"))).toBe(true);
  });

  it("maps 404 to SymbolNotFound", async () => {
    const fake = new FakeFetch();
    const adapter = new IEXCloudAdapter({ apiKey: "test", fetchFn: fake.fn });
    await expect(adapter.getSpot("ZZZZ", "USD")).rejects.toBeInstanceOf(
      SymbolNotFoundError,
    );
  });

  it("rejects order book", async () => {
    const adapter = new IEXCloudAdapter({ apiKey: "test" });
    await expect(adapter.getOrderBook("AAPL", 10)).rejects.toBeInstanceOf(
      AdapterFeatureNotSupportedError,
    );
  });
});

describe("PolygonIOAdapter", () => {
  it("returns spot from last trade", async () => {
    const fake = new FakeFetch().respondTo("/v2/last/trade/AAPL", {
      status: "OK",
      results: { p: 199.99, s: 100, t: 1714492800000000000 },
    });
    const adapter = new PolygonIOAdapter({ apiKey: "test", fetchFn: fake.fn });
    const spot = await adapter.getSpot("AAPL", "USD");
    expect(spot.last.equals(new Decimal("199.99"))).toBe(true);
  });

  it("returns aggregate candles", async () => {
    const fake = new FakeFetch().respondTo(
      "/v2/aggs/ticker/AAPL/range/1/day/",
      {
        status: "OK",
        results: [
          {
            t: 1714492800000,
            o: 199,
            h: 200,
            l: 198,
            c: 199.5,
            v: 1100000,
            n: 5000,
          },
          {
            t: 1714579200000,
            o: 199.5,
            h: 200.5,
            l: 199,
            c: 200,
            v: 1200000,
            n: 6000,
          },
        ],
      },
    );
    const adapter = new PolygonIOAdapter({ apiKey: "test", fetchFn: fake.fn });
    const candles = await adapter.getOhlcv("AAPL", Interval.D1, undefined, 10);
    expect(candles).toHaveLength(2);
    expect(candles[0]?.tradeCount).toBe(5000);
  });

  it("rejects order book", async () => {
    const adapter = new PolygonIOAdapter({ apiKey: "test" });
    await expect(adapter.getOrderBook("AAPL", 10)).rejects.toBeInstanceOf(
      AdapterFeatureNotSupportedError,
    );
  });
});

describe("TiingoAdapter", () => {
  it("returns spot and sends the token header", async () => {
    const fake = new FakeFetch().respondTo("/iex/AAPL", [
      {
        ticker: "AAPL",
        last: 199.99,
        bidPrice: 199.97,
        askPrice: 200.01,
        high: 200.1,
        low: 198.5,
        volume: 1234567,
        prevClose: 199.5,
        timestamp: "2026-04-30T16:00:00.000Z",
      },
    ]);
    const adapter = new TiingoAdapter({ apiKey: "test", fetchFn: fake.fn });
    const spot = await adapter.getSpot("AAPL", "USD");
    expect(spot.last.equals(new Decimal("199.99"))).toBe(true);
    expect(fake.lastHeaders?.["Authorization"]).toBe("Token test");
  });

  it("maps empty list to SymbolNotFound", async () => {
    const fake = new FakeFetch().respondTo("/iex/ZZZZ", []);
    const adapter = new TiingoAdapter({ apiKey: "test", fetchFn: fake.fn });
    await expect(adapter.getSpot("ZZZZ", "USD")).rejects.toBeInstanceOf(
      SymbolNotFoundError,
    );
  });

  it("rejects unsupported interval", async () => {
    const adapter = new TiingoAdapter({ apiKey: "test" });
    await expect(
      adapter.getOhlcv("AAPL", Interval.W1, undefined, 10),
    ).rejects.toBeInstanceOf(InvalidIntervalError);
  });
});
