/** Lazy credential resolution — CONTRACT.md §6, SCOPE.md §6. */
import type { ApiKeyProvider } from "../adapter.js";
import { MissingCredentialsError } from "../errors.js";

/**
 * Resolve a credential. The provider callback wins over the static key so
 * callers can rotate credentials at runtime. When `required` is set and
 * nothing resolves, throws {@link MissingCredentialsError}.
 *
 * @param adapterId - Adapter id for error context.
 * @param apiKey - Static key, if any.
 * @param apiKeyProvider - Lazy provider, if any.
 * @param required - Whether a credential is mandatory.
 * @param signal - Cancellation signal for the provider call.
 * @returns The resolved credential, or `undefined` when optional and absent.
 */
export async function resolveApiKey(
  adapterId: string,
  apiKey: string | undefined,
  apiKeyProvider: ApiKeyProvider | undefined,
  required: boolean,
  signal?: AbortSignal,
): Promise<string | undefined> {
  const resolved =
    apiKeyProvider !== undefined ? await apiKeyProvider(signal) : apiKey;
  if (required && (resolved === undefined || resolved === "")) {
    throw new MissingCredentialsError(
      `adapter ${adapterId} requires authentication but no api key or ` +
        "provider resolved a usable value",
      adapterId,
    );
  }
  return resolved;
}
