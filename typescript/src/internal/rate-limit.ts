/**
 * Per-adapter token-bucket rate limiter + retry runner — CONTRACT.md §6.
 */
import type { Clock } from "../clock.js";
import type { RateLimitConfig } from "../config.js";
import { RateLimitedError } from "../errors.js";

/** Classic refill token bucket; not concurrency-safe. */
export class TokenBucket {
  private readonly capacity: number;
  private readonly refillPerSecond: number;
  private readonly clock: Clock;
  private tokens: number;
  private last: number;

  public constructor(capacity: number, refillPerSecond: number, clock: Clock) {
    if (capacity < 1) {
      throw new Error("capacity must be >= 1");
    }
    if (refillPerSecond < 0) {
      throw new Error("refillPerSecond must be >= 0");
    }
    this.capacity = capacity;
    this.refillPerSecond = refillPerSecond;
    this.clock = clock;
    this.tokens = capacity;
    this.last = clock.monotonic();
  }

  /** Take a token if available. */
  public tryAcquire(): boolean {
    this.refill();
    if (this.tokens + 1e-9 >= 1) {
      this.tokens -= 1;
      return true;
    }
    return false;
  }

  /** Seconds until a token is available (`Infinity` if it never refills). */
  public timeUntilAvailable(): number {
    this.refill();
    const deficit = 1 - this.tokens;
    if (deficit <= 0) {
      return 0;
    }
    return this.refillPerSecond <= 0
      ? Infinity
      : deficit / this.refillPerSecond;
  }

  private refill(): void {
    const now = this.clock.monotonic();
    const elapsed = Math.max(0, now - this.last);
    this.tokens = Math.min(
      this.capacity,
      this.tokens + elapsed * this.refillPerSecond,
    );
    this.last = now;
  }
}

/**
 * Invoke `operation` with rate-limit-aware retry. On `RateLimitedError`, sleep
 * with jittered exponential backoff and retry up to `maxRetryAttempts` times.
 *
 * @param operation - The async operation to run.
 * @param config - Rate-limit configuration.
 * @param clock - Time source.
 * @param bucket - Optional token bucket to queue on.
 * @param signal - Cancellation signal.
 * @returns The operation result.
 */
export async function runWithRetry<TResult>(
  operation: () => Promise<TResult>,
  config: RateLimitConfig,
  clock: Clock,
  bucket: TokenBucket | undefined,
  signal?: AbortSignal,
): Promise<TResult> {
  let attempts = 0;
  let backoff = Math.max(0, config.initialBackoffSeconds);
  for (;;) {
    if (bucket !== undefined) {
      let wait = bucket.timeUntilAvailable();
      if (wait > 0) {
        if (wait === Infinity) {
          wait = config.maxBackoffSeconds;
        }
        await clock.sleep(wait, signal);
      }
      bucket.tryAcquire();
    }

    try {
      return await operation();
    } catch (err) {
      if (
        !(err instanceof RateLimitedError) ||
        attempts >= config.maxRetryAttempts
      ) {
        throw err;
      }
      attempts += 1;
      let sleepSeconds = Math.min(backoff, config.maxBackoffSeconds);
      if (config.jitter) {
        sleepSeconds *= 0.5 + Math.random();
      }
      await clock.sleep(sleepSeconds, signal);
      backoff = Math.min(
        config.maxBackoffSeconds,
        Math.max(backoff, 0.001) * 2,
      );
    }
  }
}
