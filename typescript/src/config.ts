/** Configuration types — mirror of CONTRACT.md §8. */

/** Rate-limit fall-through strategy. */
export type RateLimitStrategy = "bubble" | "fallthrough";

/** TTL cache configuration. */
export interface CacheConfig {
  readonly enabled: boolean;
  readonly spotTtlSeconds: number;
  readonly ohlcvTtlSeconds: number;
  readonly orderBookTtlSeconds: number;
  readonly maxEntries: number;
}

/** Rate-limiter configuration. */
export interface RateLimitConfig {
  readonly enabled: boolean;
  readonly strategy: RateLimitStrategy;
  readonly maxRetryAttempts: number;
  readonly initialBackoffSeconds: number;
  readonly maxBackoffSeconds: number;
  readonly jitter: boolean;
}

/** Streaming configuration. */
export interface StreamingConfig {
  readonly defaultPollingIntervalSeconds: number;
  readonly maxReconnectAttempts: number;
  readonly reconnectBackoffSeconds: number;
}

/** Orchestrator-wide configuration. */
export interface ClientConfig {
  readonly cache: CacheConfig;
  readonly rateLimit: RateLimitConfig;
  readonly streaming: StreamingConfig;
}

/** Default cache configuration (disabled, invariant TTLs). */
export const DEFAULT_CACHE_CONFIG: CacheConfig = {
  enabled: false,
  spotTtlSeconds: 5,
  ohlcvTtlSeconds: 60,
  orderBookTtlSeconds: 1,
  maxEntries: 10_000,
};

/** Default rate-limit configuration. */
export const DEFAULT_RATE_LIMIT_CONFIG: RateLimitConfig = {
  enabled: true,
  strategy: "fallthrough",
  maxRetryAttempts: 3,
  initialBackoffSeconds: 0.5,
  maxBackoffSeconds: 30.0,
  jitter: true,
};

/** Default streaming configuration. */
export const DEFAULT_STREAMING_CONFIG: StreamingConfig = {
  defaultPollingIntervalSeconds: 4.0,
  maxReconnectAttempts: 5,
  reconnectBackoffSeconds: 2.0,
};

/** Build a fully-defaulted client configuration. */
export function defaultClientConfig(): ClientConfig {
  return {
    cache: DEFAULT_CACHE_CONFIG,
    rateLimit: DEFAULT_RATE_LIMIT_CONFIG,
    streaming: DEFAULT_STREAMING_CONFIG,
  };
}
