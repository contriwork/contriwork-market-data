/**
 * Clock abstraction — injectable for deterministic tests. Production uses
 * `SystemClock`; tests use `ManualClock` whose `sleep` advances the manual
 * clock instead of waiting on the wall clock.
 */

/** Time source. */
export interface Clock {
  /** Current UTC time. */
  now(): Date;
  /** Monotonic seconds, suitable for measuring elapsed durations. */
  monotonic(): number;
  /** Asynchronously wait for the given duration. */
  sleep(seconds: number, signal?: AbortSignal): Promise<void>;
}

/** Wall-clock implementation backed by the runtime. */
export class SystemClock implements Clock {
  private readonly origin = performance.now();

  public now(): Date {
    return new Date();
  }

  public monotonic(): number {
    return (performance.now() - this.origin) / 1000;
  }

  public sleep(seconds: number, signal?: AbortSignal): Promise<void> {
    if (seconds <= 0) {
      return Promise.resolve();
    }
    return new Promise((resolve, reject) => {
      const timer = setTimeout(resolve, seconds * 1000);
      if (signal !== undefined) {
        signal.addEventListener(
          "abort",
          () => {
            clearTimeout(timer);
            reject(new DOMException("aborted", "AbortError"));
          },
          { once: true },
        );
      }
    });
  }
}

/** Test clock — the caller advances time explicitly. */
export class ManualClock implements Clock {
  private monotonicSeconds: number;
  private current: Date;

  public constructor(epochSeconds = 0) {
    this.monotonicSeconds = epochSeconds;
    this.current = new Date(epochSeconds * 1000);
  }

  public now(): Date {
    return this.current;
  }

  public monotonic(): number {
    return this.monotonicSeconds;
  }

  /** Advance the monotonic clock by `seconds`. */
  public advance(seconds: number): void {
    if (seconds < 0) {
      throw new Error("advance must be >= 0");
    }
    this.monotonicSeconds += seconds;
  }

  /** Set the wall-clock value returned by `now()`. */
  public setNow(value: Date): void {
    this.current = value;
  }

  public sleep(seconds: number, signal?: AbortSignal): Promise<void> {
    if (signal?.aborted === true) {
      return Promise.reject(new DOMException("aborted", "AbortError"));
    }
    this.advance(Math.max(0, seconds));
    return Promise.resolve();
  }
}
