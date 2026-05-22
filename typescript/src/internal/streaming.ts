/**
 * Polling-emulation + streaming helpers — CONTRACT.md §3.4.
 */
import type { MarketDataAdapter } from "../adapter.js";
import type { Clock } from "../clock.js";
import {
  AdapterFeatureNotSupportedError,
  MarketDataError,
  StreamDisconnectedError,
} from "../errors.js";
import { EMPTY_EXTRA, type Ticker } from "../types.js";

/**
 * Yield a {@link Ticker} every `pollingIntervalSeconds` by re-invoking the
 * adapter's `getSpot`. After `maxConsecutiveFailures` back-to-back failures,
 * throws {@link StreamDisconnectedError}.
 */
export async function* pollTicker(
  adapter: MarketDataAdapter,
  symbol: string,
  quoteCurrency: string,
  pollingIntervalSeconds: number,
  clock: Clock,
  maxConsecutiveFailures: number,
  signal: AbortSignal | undefined,
): AsyncGenerator<Ticker> {
  let failures = 0;
  while (signal?.aborted !== true) {
    let spot;
    try {
      spot = await adapter.getSpot(symbol, quoteCurrency, signal);
      failures = 0;
    } catch (err) {
      failures += 1;
      if (failures >= maxConsecutiveFailures) {
        const code = err instanceof MarketDataError ? err.code : "UNKNOWN";
        throw new StreamDisconnectedError(
          `polling emulation exhausted after ${failures.toString()} consecutive failures (last code=${code})`,
          adapter.adapterId,
        );
      }
      spot = undefined;
    }

    if (spot !== undefined) {
      yield {
        symbol: spot.symbol,
        price: spot.last,
        quoteCurrency: spot.quoteCurrency,
        timestamp: spot.timestamp,
        sourceAdapter: spot.sourceAdapter,
        extra: EMPTY_EXTRA,
      };
    }

    await clock.sleep(pollingIntervalSeconds, signal);
  }
}

/**
 * An async stream that throws {@link AdapterFeatureNotSupportedError} on first
 * iteration — for adapters whose `supportsNativeStreaming` is false.
 */
// eslint-disable-next-line require-yield
export async function* streamingNotSupported(
  adapterId: string,
): AsyncGenerator<Ticker> {
  throw new AdapterFeatureNotSupportedError(
    `adapter ${adapterId} does not implement native streaming; ` +
      "use polling fallback via MarketDataClient.subscribeTicker",
    adapterId,
  );
}
