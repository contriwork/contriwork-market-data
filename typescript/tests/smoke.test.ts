import { describe, expect, it } from "vitest";
import type { MarketDataPort } from "../src/index.js";

describe("smoke", () => {
  it("exports MarketDataPort as a type", () => {
    const shape: MarketDataPort = {
      example: async (input: string) => input,
    };
    expect(typeof shape.example).toBe("function");
  });
});
