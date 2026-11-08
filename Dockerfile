# ====================================================================
# Stage 1: Build Rust Native Engines (litevalley-save-engine & litevalley-broadcaster)
# Menggunakan Debian Bullseye (glibc 2.31) agar 100% kompatibel dengan
# runtime Ubuntu 22.04 Jammy (glibc 2.35) baik untuk biner CLI maupun FFI (.so)
# ====================================================================
FROM rust:1.85-slim-bullseye AS rust-build

# 1a. Build litevalley-save-engine (CLI binary + liblitevalley_save_engine.so)
WORKDIR /usr/src/litevalley-save-engine
COPY src/litevalley-save-engine/Cargo.toml src/litevalley-save-engine/Cargo.lock ./
COPY src/litevalley-save-engine/src ./src
RUN cargo build --release

# 1b. Build litevalley-broadcaster (SSE & 2D Radar Hub)
WORKDIR /usr/src/litevalley-broadcaster
COPY src/litevalley-broadcaster/Cargo.toml src/litevalley-broadcaster/Cargo.lock ./
COPY src/litevalley-broadcaster/src ./src
RUN cargo build --release

# ====================================================================
# Stage 2: Build Web UI Control Panel (Vite + React + Tailwind)
# ====================================================================
FROM oven/bun:1.4.4-alpine AS webui-build
WORKDIR /app
COPY src/StardewHeadless.WebUI/package.json src/StardewHeadless.WebUI/bun.lock* ./src/StardewHeadless.WebUI/
RUN cd src/StardewHeadless.WebUI && bun install
COPY src/StardewHeadless.WebUI/ ./src/StardewHeadless.WebUI/
RUN mkdir -p src/StardewHeadless.Server/wwwroot
RUN cd src/StardewHeadless.WebUI && bun run build

# ====================================================================
# Stage 3: Build Headless Server Daemon (C# .NET 6, 0-GPU)
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
# Runtime Stage (ASP.NET Core 6.0 Linux Jammy) - 100% Clean Room (Zero-Piracy)
# ====================================================================
FROM mcr.microsoft.com/dotnet/aspnet:6.0-jammy AS runtime
WORKDIR /app

LABEL org.opencontainers.image.source="https://github.com/ijplabs/litevalley-headless"
LABEL org.opencontainers.image.description="LiteValley Headless Dedicated Server for Stardew Valley (0-GPU Clean-Room Daemon with Rust Save Engine & Broadcaster)"
LABEL org.opencontainers.image.licenses="MIT"

# Instal dependensi dasar sistem untuk MonoGame headless & healthcheck
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates \
    libasound2 \
    libpulse0 \
    libsdl2-2.0-0 \
    tzdata \
    curl \
    libicu70 \
    && rm -rf /var/lib/apt/lists/*

# Buat direktori data volume server dan symlink save Stardew Valley ke persistent volume
RUN mkdir -p /data/game /data/saves /data/config /data/logs /data/mods \
    && mkdir -p /root/.config/StardewValley \
    && ln -sfn /data/saves /root/.config/StardewValley/Saves

# Salin biner native Rust (CLI + FFI cdylib .so)
COPY --from=rust-build /usr/src/litevalley-save-engine/target/release/litevalley-save-engine /usr/local/bin/litevalley-save-engine
COPY --from=rust-build /usr/src/litevalley-save-engine/target/release/liblitevalley_save_engine.so /app/liblitevalley_save_engine.so
COPY --from=rust-build /usr/src/litevalley-save-engine/target/release/liblitevalley_save_engine.so /usr/lib/liblitevalley_save_engine.so
COPY --from=rust-build /usr/src/litevalley-broadcaster/target/release/litevalley-broadcaster /usr/local/bin/litevalley-broadcaster
RUN chmod +x /usr/local/bin/litevalley-save-engine /usr/local/bin/litevalley-broadcaster /app/liblitevalley_save_engine.so /usr/lib/liblitevalley_save_engine.so

# Salin binari terkompilasi server (TANPA menyalin biner atau aset game berhak cipta)
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
    BROADCASTER_INGEST_PORT=4040 \
    BROADCASTER_WEB_PORT=4041 \
    REDIS_HOST=redis \
    REDIS_PORT=6379 \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0 \
    LITEVALLEY_SAVE_ENGINE_BIN=/usr/local/bin/litevalley-save-engine \
    LITEVALLEY_BROADCASTER_BIN=/usr/local/bin/litevalley-broadcaster

# Expose Netcode UDP Port (24642), Kestrel Web/API TCP Port (8080), & Broadcaster Web Port (4041)
EXPOSE 24642/udp
EXPOSE 8080/tcp
EXPOSE 4041/tcp

HEALTHCHECK --interval=20s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:8080/api/health || exit 1

ENTRYPOINT ["dotnet", "StardewHeadless.Server.dll"]
CMD ["--game-dir", "/data/game", "--save-dir", "/data/saves", "--config-dir", "/data/config"]
