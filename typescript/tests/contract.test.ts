import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

/**
 * Contract test runner — one of three (Python / C# / TypeScript) that MUST
 * produce identical results against the shared fixture. Do not add cases
 * here; add them to contract-tests/test_cases.json.
 */

interface ContractCase {
  name: string;
  description?: string;
  input: unknown;
  expected_output?: unknown;
  expected_error?: { code: string; message_contains?: string | null } | null;
  tags?: string[];
  skip_languages?: string[];
}

interface Fixture {
  schema_version: number;
  contract_revision: string;
  cases: ContractCase[];
}

const here = fileURLToPath(new URL(".", import.meta.url));
const fixturePath = resolve(here, "../../contract-tests/test_cases.json");
const raw = readFileSync(fixturePath, "utf-8");
const fixture = JSON.parse(raw) as Fixture;

describe("contract fixture", () => {
  it("is well-formed", () => {
    expect(fixture.schema_version).toBe(1);
    expect(Array.isArray(fixture.cases)).toBe(true);
    expect(fixture.contract_revision).toBeDefined();
  });
});

const runnable = fixture.cases.filter(
  (c) => !(c.skip_languages ?? []).includes("typescript"),
);

describe.each(runnable)("case $name", (c) => {
  it.skip("placeholder — replace once CONTRACT.md methods exist", () => {
    expect(c).toBeDefined();
  });
});
