import type { FetchLike } from "../../src/adapters/index.js";

interface Route {
  readonly match: string;
  readonly status: number;
  readonly body: unknown;
}

/** A scriptable {@link FetchLike} for adapter tests. */
export class FakeFetch {
  private readonly routes: Route[] = [];

  /** Last URL the fake served. */
  public lastUrl?: string;

  /** Last headers the fake saw. */
  public lastHeaders?: Record<string, string>;

  /** Register a JSON response for any URL containing `match`. */
  public respondTo(match: string, body: unknown, status = 200): this {
    this.routes.push({ match, status, body });
    return this;
  }

  /** The {@link FetchLike} bound to this instance. */
  public get fn(): FetchLike {
    return (url, init) => {
      this.lastUrl = url;
      this.lastHeaders = init?.headers;
      for (const route of this.routes) {
        if (url.includes(route.match)) {
          return Promise.resolve({
            ok: route.status >= 200 && route.status < 300,
            status: route.status,
            json: () => Promise.resolve(route.body),
          });
        }
      }
      return Promise.resolve({
        ok: false,
        status: 404,
        json: () => Promise.resolve({ error: "not found" }),
      });
    };
  }
}
