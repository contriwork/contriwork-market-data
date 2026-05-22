import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { Decimal } from "decimal.js";
import {
  AdapterRegistry,
  type Candle,
  type Capability,
  type ClientConfig,
  Interval,
  MarketDataClient,
  type MarketDataError,
  type OrderBook,
  type QuoteCurrencySupport,
  type SpotPrice,
  type Ticker,
  defaultClientConfig,
  EMPTY_EXTRA,
} from "../src/index.js";
import { ManualClock } from "../src/clock.js";
import type { MarketDataAdapter } from "../src/adapter.js";
import {
  InMemoryAdapter,
  type InMemoryFailModeSpec,
  type InMemorySymbolData,
} from "../src/adapters/index.js";

/**
 * Cross-language contract conformance runner — one of three (Python /
 * C# / TypeScript) that load the shared fixture and MUST produce identical
 * results for every case.
 */

interface Fixture {
  schema_version: number;
  contract_revision: string;
  cases: Record<string, unknown>[];
}

const here = fileURLToPath(new URL(".", import.meta.url));
const fixturePath = resolve(here, "../../contract-tests/test_cases.json");
const fixture = JSON.parse(readFileSync(fixturePath, "utf-8")) as Fixture;

describe("contract fixture", () => {
  it("is well-formed", () => {
    expect(fixture.schema_version).toBe(1);
    expect(fixture.contract_revision).toBe("v1");
    expect(fixture.cases.length).toBeGreaterThan(0);
  });
});

const runnable = fixture.cases.filter(
  (c) =>
    !((c["skip_languages"] as string[] | undefined) ?? []).includes(
      "typescript",
    ),
);

function asRecord(value: unknown): Record<string, unknown> {
  return value as Record<string, unknown>;
}

function dec(value: unknown): Decimal {
  return new Decimal(value as Decimal.Value);
}

function buildCapability(spec: Record<string, unknown>): Capability {
  const intervals =
    spec["supported_intervals"] !== undefined
      ? (spec["supported_intervals"] as string[]).map((v) => v as Interval)
      : Object.values(Interval);
  let quotes: QuoteCurrencySupport = "ANY";
  if (Array.isArray(spec["supported_quote_currencies"])) {
    quotes = spec["supported_quote_currencies"] as string[];
  }
  return {
    supportedMarkets: ["*"],
    supportedIntervals: intervals,
    supportedQuoteCurrencies: quotes,
    supportsOrderBook:
      (spec["supports_order_book"] as boolean | undefined) ?? true,
    supportsNativeStreaming:
      (spec["supports_native_streaming"] as boolean | undefined) ?? false,
    rateLimitPerMinute:
      (spec["rate_limit_per_minute"] as number | undefined) ?? 9999,
    requiresAuth: (spec["requires_auth"] as boolean | undefined) ?? false,
  };
}

function buildSymbolData(el: Record<string, unknown>): InMemorySymbolData {
  const result: {
    spot?: SpotPrice;
    ohlcv?: Map<Interval, readonly Candle[]>;
    orderBook?: OrderBook;
    tickerStream?: Ticker[];
  } = {};

  if (el["spot"] !== undefined) {
    const s = asRecord(el["spot"]);
    result.spot = {
      symbol: "PLACEHOLDER",
      last: dec(s["last"]),
      quoteCurrency: (s["quote_currency"] as string | undefined) ?? "USD",
      timestamp: new Date(0),
      sourceAdapter: "PLACEHOLDER",
      extra: EMPTY_EXTRA,
      ...(s["volume_24h"] !== undefined
        ? { volume24h: dec(s["volume_24h"]) }
        : {}),
    };
  }

  if (el["ohlcv"] !== undefined) {
    const ohlcv = new Map<Interval, readonly Candle[]>();
    for (const [intervalName, rows] of Object.entries(asRecord(el["ohlcv"]))) {
      const interval = intervalName as Interval;
      const candles = (rows as Record<string, unknown>[]).map(
        (c): Candle => ({
          timestamp: new Date(c["timestamp"] as string),
          open: dec(c["open"]),
          high: dec(c["high"]),
          low: dec(c["low"]),
          close: dec(c["close"]),
          volume: dec(c["volume"]),
          extra: EMPTY_EXTRA,
        }),
      );
      ohlcv.set(interval, candles);
    }
    result.ohlcv = ohlcv;
  }

  if (el["order_book"] !== undefined) {
    const b = asRecord(el["order_book"]);
    result.orderBook = {
      symbol: "PLACEHOLDER",
      sourceAdapter: "PLACEHOLDER",
      timestamp: new Date(0),
      extra: EMPTY_EXTRA,
      bids: (b["bids"] as unknown[][]).map((p) => ({
        price: dec(p[0]),
        size: dec(p[1]),
      })),
      asks: (b["asks"] as unknown[][]).map((p) => ({
        price: dec(p[0]),
        size: dec(p[1]),
      })),
    };
  }

  if (el["ticker_stream"] !== undefined) {
    result.tickerStream = (
      el["ticker_stream"] as Record<string, unknown>[]
    ).map(
      (t): Ticker => ({
        symbol: "PLACEHOLDER",
        price: dec(t["price"]),
        quoteCurrency: (t["quote_currency"] as string | undefined) ?? "USD",
        timestamp: new Date(t["timestamp"] as string),
        sourceAdapter: "PLACEHOLDER",
        extra: EMPTY_EXTRA,
      }),
    );
  }

  return result;
}

function buildAdapters(
  setup: Record<string, unknown>,
  clock: ManualClock,
): Map<string, InMemoryAdapter> {
  const adapters = new Map<string, InMemoryAdapter>();
  for (const specRaw of setup["adapters"] as Record<string, unknown>[]) {
    const id = specRaw["id"] as string;
    const data = new Map<string, InMemorySymbolData>();
    if (specRaw["data"] !== undefined) {
      for (const [symbol, symbolData] of Object.entries(
        asRecord(specRaw["data"]),
      )) {
        data.set(symbol, buildSymbolData(asRecord(symbolData)));
      }
    }
    const failModes = (
      (specRaw["fail_modes"] as Record<string, unknown>[] | undefined) ?? []
    ).map(
      (fm): InMemoryFailModeSpec => ({
        symbol: fm["symbol"] as string,
        code: fm["code"] as string,
        ...(fm["fail_first_n"] !== undefined
          ? { failFirstN: fm["fail_first_n"] as number }
          : {}),
      }),
    );
    adapters.set(
      id,
      new InMemoryAdapter({
        adapterId: id,
        data,
        capability: buildCapability(specRaw),
        failModes,
        clock,
        ...(specRaw["api_key"] != null
          ? { apiKey: specRaw["api_key"] as string }
          : {}),
      }),
    );
  }
  return adapters;
}

function buildConfig(setup: Record<string, unknown>): ClientConfig {
  const base = defaultClientConfig();
  const cache = setup["cache"] as Record<string, unknown> | undefined;
  const rl = setup["rate_limit"] as Record<string, unknown> | undefined;
  const stream = setup["streaming"] as Record<string, unknown> | undefined;
  return {
    cache: {
      ...base.cache,
      ...(cache?.["enabled"] !== undefined
        ? { enabled: cache["enabled"] as boolean }
        : {}),
      ...(cache?.["spot_ttl_s"] !== undefined
        ? { spotTtlSeconds: cache["spot_ttl_s"] as number }
        : {}),
      ...(cache?.["ohlcv_ttl_s"] !== undefined
        ? { ohlcvTtlSeconds: cache["ohlcv_ttl_s"] as number }
        : {}),
      ...(cache?.["order_book_ttl_s"] !== undefined
        ? { orderBookTtlSeconds: cache["order_book_ttl_s"] as number }
        : {}),
    },
    rateLimit: {
      ...base.rateLimit,
      ...(rl?.["enabled"] !== undefined
        ? { enabled: rl["enabled"] as boolean }
        : {}),
      ...(rl?.["strategy"] !== undefined
        ? { strategy: rl["strategy"] as "bubble" | "fallthrough" }
        : {}),
      ...(rl?.["max_retry_attempts"] !== undefined
        ? { maxRetryAttempts: rl["max_retry_attempts"] as number }
        : {}),
      ...(rl?.["initial_backoff_s"] !== undefined
        ? { initialBackoffSeconds: rl["initial_backoff_s"] as number }
        : {}),
      ...(rl?.["jitter"] !== undefined
        ? { jitter: rl["jitter"] as boolean }
        : {}),
    },
    streaming: {
      ...base.streaming,
      ...(stream?.["default_polling_interval_s"] !== undefined
        ? {
            defaultPollingIntervalSeconds: stream[
              "default_polling_interval_s"
            ] as number,
          }
        : {}),
    },
  };
}

function buildClient(
  setup: Record<string, unknown>,
  adapters: Map<string, InMemoryAdapter>,
  clock: ManualClock,
): MarketDataClient {
  const registry = new AdapterRegistry();
  for (const [market, ids] of Object.entries(asRecord(setup["client_chain"]))) {
    const chain = (ids as string[]).map(
      (id) => adapters.get(id) as MarketDataAdapter,
    );
    registry.register(market, chain);
  }
  return new MarketDataClient(registry, buildConfig(setup), clock);
}

function invoke(
  client: MarketDataClient,
  method: string,
  args: Record<string, unknown>,
): Promise<unknown> {
  const symbol = args["symbol"] as string;
  const market = args["market"] as string;
  switch (method) {
    case "get_spot":
      return client.getSpot(
        symbol,
        market,
        (args["quote_currency"] as string | undefined) ?? "USD",
      );
    case "get_ohlcv":
      return client.getOhlcv(
        symbol,
        market,
        args["interval"] as Interval,
        args["since"] !== undefined
          ? new Date(args["since"] as string)
          : undefined,
        (args["limit"] as number | undefined) ?? 100,
      );
    case "get_order_book":
      return client.getOrderBook(
        symbol,
        market,
        (args["depth"] as number | undefined) ?? 20,
      );
    default:
      throw new Error(`unsupported method: ${method}`);
  }
}

async function consumeStream(
  client: MarketDataClient,
  args: Record<string, unknown>,
  yieldCount: number,
): Promise<Ticker[]> {
  const controller = new AbortController();
  const collected: Ticker[] = [];
  const stream = client.subscribeTicker(
    args["symbol"] as string,
    args["market"] as string,
    {
      pollingFallback:
        (args["polling_fallback"] as boolean | undefined) ?? true,
      pollingIntervalSeconds:
        (args["polling_interval_s"] as number | undefined) ?? 4.0,
      signal: controller.signal,
    },
  );
  for await (const ticker of stream) {
    collected.push(ticker);
    if (collected.length >= yieldCount) {
      controller.abort();
      break;
    }
  }
  return collected;
}

function assertExpectedOutput(
  c: Record<string, unknown>,
  result: unknown,
  adapters: Map<string, InMemoryAdapter>,
): void {
  const expected = c["expected_output"] as
    | Record<string, unknown>
    | null
    | undefined;
  if (expected == null) {
    return;
  }
  const typeLabel = expected["type"] as string;

  if (typeLabel === "SpotPrice") {
    const spot = result as SpotPrice;
    const fields = asRecord(expected["fields"]);
    for (const [field, value] of Object.entries(fields)) {
      if (field === "last") {
        expect(spot.last.equals(new Decimal(value as Decimal.Value))).toBe(
          true,
        );
      } else if (field === "symbol") {
        expect(spot.symbol).toBe(value);
      } else if (field === "quote_currency") {
        expect(spot.quoteCurrency).toBe(value);
      } else if (field === "source_adapter") {
        expect(spot.sourceAdapter).toBe(value);
      }
    }
  } else if (typeLabel.startsWith("list[Candle]")) {
    const candles = result as Candle[];
    if (expected["length"] !== undefined) {
      expect(candles).toHaveLength(expected["length"] as number);
    }
    if (expected["ordered_ascending_by"] === "timestamp") {
      for (let i = 1; i < candles.length; i++) {
        expect(
          (candles[i] as Candle).timestamp.getTime(),
        ).toBeGreaterThanOrEqual(
          (candles[i - 1] as Candle).timestamp.getTime(),
        );
      }
    }
    if (expected["all_timestamps_at_or_after"] !== undefined) {
      const min = new Date(expected["all_timestamps_at_or_after"] as string);
      for (const candle of candles) {
        expect(candle.timestamp.getTime()).toBeGreaterThanOrEqual(
          min.getTime(),
        );
      }
    }
  } else if (typeLabel === "OrderBook") {
    const book = result as OrderBook;
    const fields =
      (expected["fields"] as Record<string, unknown> | undefined) ?? {};
    for (const [field, value] of Object.entries(fields)) {
      if (field === "symbol") {
        expect(book.symbol).toBe(value);
      } else if (field === "source_adapter") {
        expect(book.sourceAdapter).toBe(value);
      }
    }
    if (expected["bids_length"] !== undefined) {
      expect(book.bids).toHaveLength(expected["bids_length"] as number);
    }
    if (expected["asks_length"] !== undefined) {
      expect(book.asks).toHaveLength(expected["asks_length"] as number);
    }
    if (expected["bids_sorted_descending_by_price"] === true) {
      for (let i = 1; i < book.bids.length; i++) {
        expect(
          (book.bids[i - 1] as { price: Decimal }).price.gte(
            (book.bids[i] as { price: Decimal }).price,
          ),
        ).toBe(true);
      }
    }
    if (expected["asks_sorted_ascending_by_price"] === true) {
      for (let i = 1; i < book.asks.length; i++) {
        expect(
          (book.asks[i - 1] as { price: Decimal }).price.lte(
            (book.asks[i] as { price: Decimal }).price,
          ),
        ).toBe(true);
      }
    }
  } else if (typeLabel === "list[Ticker]") {
    const tickers = result as Ticker[];
    if (expected["length"] !== undefined) {
      expect(tickers).toHaveLength(expected["length"] as number);
    }
    for (const key of ["all_have_field", "all_have_field_2"]) {
      if (expected[key] !== undefined) {
        const parts = (expected[key] as string).split(":");
        const field = parts[0];
        const value = parts[1];
        for (const ticker of tickers) {
          if (field === "source_adapter") {
            expect(ticker.sourceAdapter).toBe(value);
          } else if (field === "price") {
            expect(ticker.price.equals(new Decimal(value as string))).toBe(
              true,
            );
          }
        }
      }
    }
  }

  const callCounts = expected["adapter_call_count"] as
    | Record<string, number>
    | undefined;
  if (callCounts !== undefined) {
    for (const [adapterId, count] of Object.entries(callCounts)) {
      const adapter = adapters.get(adapterId) as InMemoryAdapter;
      let total = 0;
      for (const v of adapter.callCountsView.values()) {
        total += v;
      }
      expect(total).toBe(count);
    }
  }
}

describe.each(runnable)("case $name", (rawCase) => {
  it("matches the fixture", async () => {
    const c = rawCase;
    const setup = asRecord(c["setup"]);
    const clockSpec = setup["clock"] as Record<string, unknown> | undefined;
    const clock = new ManualClock(
      (clockSpec?.["epoch_seconds"] as number | undefined) ?? 0,
    );
    const adapters = buildAdapters(setup, clock);
    const client = buildClient(setup, adapters, clock);
    const operation = asRecord(c["operation"]);
    const method = operation["method"] as string;
    const args = asRecord(operation["args"]);
    const expectedError = c["expected_error"] as {
      code: string;
      message_contains?: string | null;
    } | null;

    if (method === "subscribe_ticker") {
      const yieldCount = (operation["yield_count"] as number | undefined) ?? 1;
      if (expectedError != null) {
        await expect(
          consumeStream(client, args, yieldCount),
        ).rejects.toMatchObject({
          code: expectedError.code,
        });
        return;
      }
      const tickers = await consumeStream(client, args, yieldCount);
      assertExpectedOutput(c, tickers, adapters);
      return;
    }

    const repeat = (operation["repeat"] as number | undefined) ?? 1;
    const advance =
      (operation["advance_clock_between_calls_s"] as number | undefined) ?? 0;
    let last: unknown;
    for (let i = 0; i < repeat; i++) {
      if (expectedError != null) {
        let thrown: MarketDataError | undefined;
        try {
          await invoke(client, method, args);
        } catch (err) {
          thrown = err as MarketDataError;
        }
        expect(thrown?.code).toBe(expectedError.code);
        if (expectedError.message_contains != null) {
          expect(thrown?.message).toContain(expectedError.message_contains);
        }
        return;
      }
      last = await invoke(client, method, args);
      if (i < repeat - 1 && advance > 0) {
        clock.advance(advance);
      }
    }
    assertExpectedOutput(c, last, adapters);
  });
});
