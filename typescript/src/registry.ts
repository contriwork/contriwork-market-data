/**
 * AdapterRegistry — maps a `market` string to an ordered adapter chain.
 * CONTRACT.md §4.
 */
import type { MarketDataAdapter } from "./adapter.js";

/** Market-to-chain registry. */
export class AdapterRegistry {
  private readonly chains = new Map<string, readonly MarketDataAdapter[]>();

  public constructor(
    chains?: Readonly<Record<string, readonly MarketDataAdapter[]>>,
  ) {
    if (chains !== undefined) {
      for (const [market, adapters] of Object.entries(chains)) {
        this.chains.set(market, [...adapters]);
      }
    }
  }

  /** Register or replace the adapter chain for a market. */
  public register(
    market: string,
    adapters: readonly MarketDataAdapter[],
  ): void {
    this.chains.set(market, [...adapters]);
  }

  /** Get the ordered adapter chain for a market; empty when unknown. */
  public chainFor(market: string): readonly MarketDataAdapter[] {
    return this.chains.get(market) ?? [];
  }

  /** All registered market strings. */
  public markets(): readonly string[] {
    return [...this.chains.keys()];
  }
}
