/**
 * Shared HTTP plumbing for REST-based adapters. Built on the global `fetch`
 * (Node >= 24); a `FetchLike` can be injected for testing.
 */
import { AdapterUnavailableError, RateLimitedError } from "../errors.js";

/** The subset of `fetch` the adapters use; injectable for tests. */
export type FetchLike = (
  url: string,
  init?: { headers?: Record<string, string>; signal?: AbortSignal },
) => Promise<{
  readonly ok: boolean;
  readonly status: number;
  json(): Promise<unknown>;
}>;

/** Build a query-string suffix from a key/value bag (skips `undefined`). */
export function queryString(
  parameters: Record<string, string | number | undefined>,
): string {
  const pairs: string[] = [];
  for (const [key, value] of Object.entries(parameters)) {
    if (value !== undefined) {
      pairs.push(
        `${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`,
      );
    }
  }
  return pairs.length > 0 ? `?${pairs.join("&")}` : "";
}

/**
 * Issue an HTTP GET and return the parsed JSON. Maps HTTP 429 to
 * {@link RateLimitedError} and other non-2xx + network/parse failures to
 * {@link AdapterUnavailableError}.
 *
 * @param fetchFn - The fetch implementation.
 * @param adapterId - Adapter id for error context.
 * @param url - Absolute URL.
 * @param headers - Optional request headers.
 * @param signal - Cancellation signal.
 * @returns The parsed JSON value.
 */
export async function getJson(
  fetchFn: FetchLike,
  adapterId: string,
  url: string,
  headers?: Record<string, string>,
  signal?: AbortSignal,
): Promise<unknown> {
  let response;
  try {
    response = await fetchFn(url, {
      ...(headers !== undefined ? { headers } : {}),
      ...(signal !== undefined ? { signal } : {}),
    });
  } catch (err) {
    const reason = err instanceof Error ? err.message : String(err);
    throw new AdapterUnavailableError(
      `adapter ${adapterId} network error: ${reason}`,
      adapterId,
    );
  }

  if (response.status === 429) {
    throw new RateLimitedError(
      `adapter ${adapterId} returned HTTP 429`,
      adapterId,
    );
  }
  if (!response.ok) {
    throw new AdapterUnavailableError(
      `adapter ${adapterId} returned HTTP ${String(response.status)}`,
      adapterId,
    );
  }

  try {
    return await response.json();
  } catch {
    throw new AdapterUnavailableError(
      `adapter ${adapterId} returned non-JSON body`,
      adapterId,
    );
  }
}

/** The default fetch implementation (global `fetch`). */
export const defaultFetch: FetchLike = (url, init) =>
  fetch(url, init as RequestInit) as unknown as ReturnType<FetchLike>;
