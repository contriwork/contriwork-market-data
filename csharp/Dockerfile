# syntax=docker/dockerfile:1.10

# ---- build stage ----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:dc8430e6024d454edadad1e160e1973be3cabbb7125998ef190d9e5c6adf7dbb AS build

WORKDIR /src

COPY VERSION /VERSION
COPY csharp/Directory.Build.props ./Directory.Build.props
COPY csharp/Contriwork.MarketData.sln ./
COPY csharp/src/ ./src/
COPY csharp/tests/ ./tests/

RUN dotnet restore \
 && dotnet publish src/Contriwork.MarketData/Contriwork.MarketData.csproj \
      -c Release \
      -o /out \
      --no-restore \
      /p:UseAppHost=false

# ---- runtime stage --------------------------------------------------------
# Chiseled runtime: non-root `app` user (uid 1654), no shell, minimal surface.
# .NET 10 publishes noble / resolute chiseled variants; jammy (Ubuntu 22.04)
# was NOT cut for .NET 10 — only .NET 8/9. Use noble (24.04 LTS) here.
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled-extra@sha256:e94f025683507f6723196a6953f51a520e2b9c6114b2f5a7114ff3fa065dfbd2 AS runtime

WORKDIR /app
COPY --from=build /out ./

# USER is already `app` in chiseled images; re-assert defensively.
USER app

# Consumer overrides ENTRYPOINT. Image exposes no ports by default.
# Run with a read-only filesystem: `docker run --read-only --tmpfs /tmp`.

HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
  CMD ["dotnet", "--info"]

ENTRYPOINT ["dotnet", "Contriwork.MarketData.dll"]
