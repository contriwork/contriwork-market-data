import { describe, expect, it } from "vitest";
import {
  AdapterRegistry,
  Interval,
  MarketDataClient,
  defaultClientConfig,
} from "../src/index.js";
import { InMemoryAdapter } from "../src/adapters/index.js";

describe("smoke", () => {
  it("exposes the four-member core surface", () => {
    expect(Object.values(Interval)).toHaveLength(9);
  });

  it("constructs a client with defaults", () => {
    const client = new MarketDataClient(
      new AdapterRegistry(),
      defaultClientConfig(),
    );
    expect(typeof client.getSpot).toBe("function");
    expect(typeof client.getOhlcv).toBe("function");
    expect(typeof client.getOrderBook).toBe("function");
    expect(typeof client.subscribeTicker).toBe("function");
  });

  it("constructs an InMemoryAdapter", () => {
    const adapter = new InMemoryAdapter({ adapterId: "test" });
    expect(adapter.adapterId).toBe("test");
  });
});
