# syntax=docker/dockerfile:1.10

# ---- build stage ----------------------------------------------------------
# TEMPLATE_USAGE.md §3: replace @sha256:TODO with an actual digest before release.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:TODO AS build

WORKDIR /src

COPY VERSION /VERSION
COPY csharp/Directory.Build.props ./Directory.Build.props
COPY csharp/Contriwork.PackageName.sln ./
COPY csharp/src/ ./src/
COPY csharp/tests/ ./tests/

RUN dotnet restore \
 && dotnet publish src/Contriwork.PackageName/Contriwork.PackageName.csproj \
      -c Release \
      -o /out \
      --no-restore \
      /p:UseAppHost=false

# ---- runtime stage --------------------------------------------------------
# Chiseled runtime: non-root `app` user (uid 1654), no shell, minimal surface.
# .NET 10 publishes noble / resolute chiseled variants; jammy (Ubuntu 22.04)
# was NOT cut for .NET 10 — only .NET 8/9. Use noble (24.04 LTS) here.
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled-extra@sha256:TODO AS runtime

WORKDIR /app
COPY --from=build /out ./

# USER is already `app` in chiseled images; re-assert defensively.
USER app

# Consumer overrides ENTRYPOINT. Image exposes no ports by default.
# Run with a read-only filesystem: `docker run --read-only --tmpfs /tmp`.

HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
  CMD ["dotnet", "--info"]

ENTRYPOINT ["dotnet", "Contriwork.PackageName.dll"]
