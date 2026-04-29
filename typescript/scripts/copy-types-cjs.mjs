// Mirror every `.d.ts` / `.d.ts.map` emitted by `tsc --emitDeclarationOnly`
// to a `.d.cts` / `.d.cts.map` sibling so that the CJS half of our exports
// map resolves types under NodeNext without falling back to the ESM copy.
//
// Why this file instead of a tsup `dts: true` build: tsup's declaration
// generator (rollup-plugin-dts) injects a deprecated `baseUrl` into its
// internal TypeScript options and trips TS5101 on TypeScript >= 6. Running
// `tsc` directly keeps our `tsconfig.json` as the single source of truth,
// and this script duplicates the output for the CJS export.

import { copyFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";

const DIST = "dist";

/** @param {string} dir */
function walk(dir) {
  for (const name of readdirSync(dir)) {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) {
      walk(path);
      continue;
    }
    if (path.endsWith(".d.ts")) {
      copyFileSync(path, path.slice(0, -".d.ts".length) + ".d.cts");
    } else if (path.endsWith(".d.ts.map")) {
      copyFileSync(path, path.slice(0, -".d.ts.map".length) + ".d.cts.map");
    }
  }
}

walk(DIST);
