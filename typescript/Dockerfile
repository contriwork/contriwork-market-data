# syntax=docker/dockerfile:1.10
# hadolint global ignore=DL3018

# ---- build stage ----------------------------------------------------------
FROM node:24-bookworm-slim@sha256:03eae3ef7e88a9de535496fb488d67e02b9d96a063a8967bae657744ecd513f2 AS build

WORKDIR /app

RUN corepack enable && corepack prepare pnpm@9.15.0 --activate

COPY typescript/package.json typescript/pnpm-lock.yaml* ./
RUN --mount=type=cache,target=/root/.local/share/pnpm/store \
    pnpm install --frozen-lockfile --ignore-scripts

COPY typescript/tsconfig.json typescript/tsup.config.ts typescript/eslint.config.js typescript/vitest.config.ts ./
COPY typescript/src ./src

RUN pnpm build \
 && pnpm prune --prod --ignore-scripts

# ---- runtime stage --------------------------------------------------------
FROM node:24-alpine@sha256:d1b3b4da11eefd5941e7f0b9cf17783fc99d9c6fc34884a665f40a06dbdfc94f AS runtime

ENV NODE_ENV=production

WORKDIR /app

COPY --from=build --chown=node:node /app/dist ./dist
COPY --from=build --chown=node:node /app/node_modules ./node_modules
COPY --from=build --chown=node:node /app/package.json ./package.json

USER node

# Consumer overrides CMD. Image exposes no ports by default.
# Run with read-only filesystem: `docker run --read-only --tmpfs /tmp`.

HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
  CMD node -e "require('./dist/index.cjs')" || exit 1

CMD ["node", "--enable-source-maps", "dist/index.cjs"]
