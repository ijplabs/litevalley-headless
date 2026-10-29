# ====================================================================
# Stage 1: Build Web UI Control Panel (Vite + React + Tailwind)
# ====================================================================
FROM oven/bun:1.4.4-alpine AS webui-build
WORKDIR /app
COPY src/StardewHeadless.WebUI/package.json src/StardewHeadless.WebUI/bun.lock* ./src/StardewHeadless.WebUI/
RUN cd src/StardewHeadless.WebUI && bun install
COPY src/StardewHeadless.WebUI/ ./src/StardewHeadless.WebUI/
RUN mkdir -p src/StardewHeadless.Server/wwwroot
RUN cd src/StardewHeadless.WebUI && bun run build

# ====================================================================
# Stage 2: Build Headless Server Daemon (C# .NET 6, 0-GPU)
# ====================================================================
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /app

# Salin file proyek, referensi stardew assemblies, dan restore dependencies
COPY src/StardewHeadless.Protocol/StardewHeadless.Protocol.csproj src/StardewHeadless.Protocol/
COPY src/StardewHeadless.Server/StardewHeadless.Server.csproj src/StardewHeadless.Server/
COPY ref-assemblies/ ref-assemblies/
RUN dotnet restore src/StardewHeadless.Server/StardewHeadless.Server.csproj

# Salin seluruh source code
COPY src/StardewHeadless.Protocol/ src/StardewHeadless.Protocol/
COPY src/StardewHeadless.Server/ src/StardewHeadless.Server/

# Salin aset Web UI terkompilasi ke dalam wwwroot
COPY --from=webui-build /app/src/StardewHeadless.Server/wwwroot/ src/StardewHeadless.Server/wwwroot/

RUN dotnet publish src/StardewHeadless.Server/StardewHeadless.Server.csproj -c Release -o /out/server

# ====================================================================
# Stage 3: Runtime Stage (ASP.NET Core 6.0 Linux Jammy) - 100% Clean Room (Zero-Piracy)
# ====================================================================
FROM mcr.microsoft.com/dotnet/aspnet:6.0-jammy AS runtime
WORKDIR /app

LABEL org.opencontainers.image.source="https://github.com/ijplabs/litevalley-headless"
LABEL org.opencontainers.image.description="LiteValley Headless Dedicated Server for Stardew Valley (0-GPU Clean-Room Daemon)"
LABEL org.opencontainers.image.licenses="MIT"

# Instal dependensi dasar sistem untuk MonoGame headless & healthcheck
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates \
    libasound2 \
    libpulse0 \
    libsdl2-2.0-0 \
    tzdata \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Buat direktori data volume server dan symlink save Stardew Valley ke persistent volume
RUN mkdir -p /data/game /data/saves /data/config /data/logs /data/mods \
    && mkdir -p /root/.config/StardewValley \
    && ln -sfn /data/saves /root/.config/StardewValley/Saves

# Salin binari terkompilasi server (TANPA menyalin biner atau aset game asli)
COPY --from=build /out/server /app/

# Environment variables default
ENV SDL_VIDEODRIVER=dummy \
    SDL_AUDIODRIVER=dummy \
    STARDEW_GAME_DIR=/data/game \
    STARDEW_SAVE_DIR=/data/saves \
    STARDEW_CONFIG_DIR=/data/config \
    STARDEW_LOG_DIR=/data/logs \
    MODS_PATH=/data/mods \
    PORT=24642 \
    WEB_PORT=8080 \
    REDIS_HOST=redis \
    REDIS_PORT=6379 \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

# Expose Netcode UDP Port (24642) & Kestrel Web/API TCP Port (8080)
EXPOSE 24642/udp
EXPOSE 8080/tcp

HEALTHCHECK --interval=20s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:8080/api/health || exit 1

ENTRYPOINT ["dotnet", "StardewHeadless.Server.dll"]
CMD ["--game-dir", "/data/game", "--save-dir", "/data/saves", "--config-dir", "/data/config"]
