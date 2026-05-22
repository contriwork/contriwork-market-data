/**
 * TTL cache with LRU eviction — CONTRACT.md §6. Not safe for concurrent
 * mutation; the orchestrator drives it from a single logical flow.
 */
import type { Clock } from "../clock.js";

interface Entry<TValue> {
  readonly value: TValue;
  readonly expiresAt: number;
}

/** Generic TTL + LRU cache. */
export class TtlCache<TValue> {
  private readonly maxEntries: number;
  private readonly clock: Clock;
  private readonly store = new Map<string, Entry<TValue>>();

  public constructor(maxEntries: number, clock: Clock) {
    if (maxEntries < 1) {
      throw new Error("maxEntries must be >= 1");
    }
    this.maxEntries = maxEntries;
    this.clock = clock;
  }

  /** Current entry count. */
  public get size(): number {
    return this.store.size;
  }

  /** Read a live (unexpired) value, or `undefined` on miss. */
  public get(key: string): TValue | undefined {
    const entry = this.store.get(key);
    if (entry === undefined) {
      return undefined;
    }
    if (this.clock.monotonic() >= entry.expiresAt) {
      this.store.delete(key);
      return undefined;
    }
    // Promote to most-recently-used.
    this.store.delete(key);
    this.store.set(key, entry);
    return entry.value;
  }

  /** Store a value with a TTL. A non-positive TTL is a no-op. */
  public set(key: string, value: TValue, ttlSeconds: number): void {
    if (ttlSeconds <= 0) {
      return;
    }
    const expiresAt = this.clock.monotonic() + ttlSeconds;
    if (this.store.has(key)) {
      this.store.delete(key);
    } else {
      while (this.store.size >= this.maxEntries) {
        const oldest = this.store.keys().next().value;
        if (oldest === undefined) {
          break;
        }
        this.store.delete(oldest);
      }
    }
    this.store.set(key, { value, expiresAt });
  }
}
