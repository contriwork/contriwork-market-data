import { describe, expect, it } from "vitest";
import type { PackageNamePort } from "../src/index.js";

describe("smoke", () => {
  it("exports PackageNamePort as a type", () => {
    const shape: PackageNamePort = {
      example: async (input: string) => input,
    };
    expect(typeof shape.example).toBe("function");
  });
});
