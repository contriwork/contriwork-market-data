import { describe, expect, it } from "vitest";
import { ManualClock } from "../src/clock.js";
import { DEFAULT_RATE_LIMIT_CONFIG } from "../src/config.js";
import { RateLimitedError } from "../src/errors.js";
import { TtlCache } from "../src/internal/cache.js";
import { TokenBucket, runWithRetry } from "../src/internal/rate-limit.js";

describe("TtlCache", () => {
  it("hits within TTL", () => {
    const clock = new ManualClock();
    const cache = new TtlCache<string>(4, clock);
    cache.set("a", "alpha", 5);
    clock.advance(3);
    expect(cache.get("a")).toBe("alpha");
  });

  it("misses after TTL", () => {
    const clock = new ManualClock();
    const cache = new TtlCache<string>(4, clock);
    cache.set("a", "alpha", 5);
    clock.advance(6);
    expect(cache.get("a")).toBeUndefined();
    expect(cache.size).toBe(0);
  });

  it("evicts least-recently-used", () => {
    const clock = new ManualClock();
    const cache = new TtlCache<string>(2, clock);
    cache.set("a", "1", 100);
    cache.set("b", "2", 100);
    cache.get("a"); // promote a
    cache.set("c", "3", 100); // evict b
    expect(cache.get("a")).toBe("1");
    expect(cache.get("b")).toBeUndefined();
    expect(cache.get("c")).toBe("3");
  });

  it("ignores non-positive TTL", () => {
    const clock = new ManualClock();
    const cache = new TtlCache<string>(4, clock);
    cache.set("a", "alpha", 0);
    expect(cache.get("a")).toBeUndefined();
  });
});

describe("TokenBucket", () => {
  it("drains and refills", () => {
    const clock = new ManualClock();
    const bucket = new TokenBucket(2, 1, clock);
    expect(bucket.tryAcquire()).toBe(true);
    expect(bucket.tryAcquire()).toBe(true);
    expect(bucket.tryAcquire()).toBe(false);
    clock.advance(1.5);
    expect(bucket.tryAcquire()).toBe(true);
  });
});

describe("runWithRetry", () => {
  it("succeeds on the second attempt", async () => {
    const clock = new ManualClock();
    const config = {
      ...DEFAULT_RATE_LIMIT_CONFIG,
      maxRetryAttempts: 3,
      initialBackoffSeconds: 0.001,
      jitter: false,
    };
    let calls = 0;
    const result = await runWithRetry(
      () => {
        calls += 1;
        if (calls === 1) {
          return Promise.reject(new RateLimitedError("first"));
        }
        return Promise.resolve("ok");
      },
      config,
      clock,
      undefined,
    );
    expect(result).toBe("ok");
    expect(calls).toBe(2);
  });

  it("bubbles when attempts exhausted", async () => {
    const clock = new ManualClock();
    const config = {
      ...DEFAULT_RATE_LIMIT_CONFIG,
      maxRetryAttempts: 2,
      initialBackoffSeconds: 0.001,
      jitter: false,
    };
    let calls = 0;
    await expect(
      runWithRetry<string>(
        () => {
          calls += 1;
          return Promise.reject(new RateLimitedError("always"));
        },
        config,
        clock,
        undefined,
      ),
    ).rejects.toBeInstanceOf(RateLimitedError);
    expect(calls).toBe(3);
  });
});
