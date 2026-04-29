import { defineConfig } from "tsup";

// Declaration emission is done by a separate `tsc --emitDeclarationOnly`
// step in the `build:types` npm script, not by tsup. tsup's built-in DTS
// mode goes through rollup-plugin-dts, which internally injects a
// deprecated `baseUrl` into its TypeScript compiler options and trips
// TS5101 on TypeScript >= 6. Running `tsc` directly uses our tsconfig.json
// as the sole source of truth and sidesteps the injection entirely.
export default defineConfig({
  entry: ["src/index.ts"],
  format: ["esm", "cjs"],
  dts: false,
  clean: true,
  sourcemap: true,
  minify: false,
  splitting: false,
  target: "node24",
  platform: "node",
});
