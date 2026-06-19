# syntax=docker/dockerfile:1.10

# ---- build stage ----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:8a90a473da5205a16979de99d2fc20975e922c68304f5c79d564e666dc3982fc AS build

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
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled-extra@sha256:f1065ae889fa4328c919f09d296d144d09d51bf0320206744e37cbf29b474d25 AS runtime

WORKDIR /app
COPY --from=build /out ./

# USER is already `app` in chiseled images; re-assert defensively.
USER app

# Consumer overrides ENTRYPOINT. Image exposes no ports by default.
# Run with a read-only filesystem: `docker run --read-only --tmpfs /tmp`.

HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
  CMD ["dotnet", "--info"]

ENTRYPOINT ["dotnet", "Contriwork.MarketData.dll"]
