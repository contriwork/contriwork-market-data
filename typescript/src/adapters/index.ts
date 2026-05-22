/**
 * Concrete adapter implementations. v0.1.0 ships InMemory in this PR;
 * crypto adapters arrive in PR 9 and stocks adapters in PR 10.
 */
export {
  InMemoryAdapter,
  InMemoryFailMode,
  type InMemoryAdapterOptions,
  type InMemoryFailModeSpec,
  type InMemorySymbolData,
} from "./in-memory.js";
