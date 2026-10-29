# 🌾 LiteValley Headless

> **Pure C# .NET Headless Dedicated Server for Stardew Valley 1.6+ with Embedded Web UI Control Panel.**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Docker](https://img.shields.io/badge/Docker-Compatible-blue.svg?logo=docker)](https://www.docker.com/)
[![.NET 6](https://img.shields.io/badge/.NET-6.0-purple.svg?logo=dotnet)](https://dotnet.microsoft.com/)
[![React 18](https://img.shields.io/badge/WebUI-React_18_%2B_Tailwind-61dafb.svg?logo=react)](https://react.dev/)

**LiteValley Headless** adalah solusi dedicated server mandiri (*self-hosted*) untuk Stardew Valley Multiplayer tanpa memerlukan tampilan GUI grafis (0-GPU / headless). Dilengkapi dengan Web UI Control Panel modern yang langsung tersemat (*embedded*), manajemen pemain, anti-griefing, dan telemetri real-time.

---

## ✨ Fitur Utama

- 🚀 **100% Headless (0-GPU):** Berjalan mulus di Linux VPS ekonomis, Docker Desktop, Portainer, CasaOS, Unraid, atau TrueNAS dengan konsumsi RAM rendah (<300 MB).
- 🎮 **Protokol Netcode Native:** Kompatibel penuh dengan netcode Lidgren UDP resmi Stardew Valley 1.6+ di port `24642`. Pemain dapat langsung bergabung melalui menu *Join LAN Game*.
- 🌐 **Embedded Web UI Control Panel:** Antarmuka web modern bertenaga React 18 & Tailwind CSS langsung disajikan oleh server Kestrel tanpa butuh container frontend tambahan.
- 🌓 **Dark & Light Mode:** Dukungan tema gelap (*slate dark*) dan tema terang (*warm rustic*) dengan transisi halus dan persistensi preferensi lokal.
- 🔒 **Dual-Mode Access Control:**
  - **Client Mode (Pemain):** Menampilkan status server online, kalender game (Musim, Hari, Tahun), ramalan cuaca, kas bersama, dan daftar petani yang sedang aktif. Metrik teknis CPU/RAM dan tab konsol disembunyikan demi privasi.
  - **Sysadmin Mode:** Masukkan PIN/Password admin untuk membuka kontrol penuh: tombol kontrol server (Mulai, Hentikan, Restart, Paksa Simpan), kick pemain, live console terminal, serta pengaturan server.
- 🛡️ **Zero-Piracy & Steam Genuine Token:** Mendukung pengunduhan berkas biner game asli langsung dari Steam CDN menggunakan `STEAM_REFRESH_TOKEN` resmi melalui sidecar `sdvd/steam-service`.
- 🧩 **Ekosistem Mod Terpisah:** Klien opsional dapat menggunakan mod SMAPI resmi [**LiteValley Bridge**](https://github.com/ijplabs/litevalley-bridge) untuk sinkronisasi chat dua arah dan status HUD in-game.

---

## 🏗️ Arsitektur All-In-One

```text
[ Pemain Stardew Valley ] ──(Lidgren UDP: 24642)──┐
                                                    │
[ Web Browser ] ───────(HTTP: 5000)────────────────┼─► [ stardew-server ]
                                                    │   ├── C# .NET 6 Headless Daemon
                                                    │   ├── Embedded WebUI (wwwroot)
                                                    │   └── SQLite Persistent DB
                                                    │
                                                    ├─► [ redis:7-alpine ]
                                                    │   └── Event Pub/Sub & Cache
                                                    │
                                                    └─► [ steam-downloader ]
                                                        └── Unduh data game resmi via SteamKit2
```

---

## ⚡ Panduan Cepat (Quick Start 3 Langkah)

### Prasyarat
- [Docker](https://docs.docker.com/engine/install/) & [Docker Compose](https://docs.docker.com/compose/)
- Akun Steam yang memiliki lisensi game **Stardew Valley**

---

### Langkah 1: Kloning Repositori & Salin Konfigurasi

```bash
git clone https://github.com/ijplabs/litevalley-headless.git
cd litevalley-headless
cp .env.example .env
```

---

### Langkah 2: Dapatkan `STEAM_REFRESH_TOKEN` (Hanya Sekali)

Jalankan dua perintah resmi berikut di terminal host untuk menghubungkan akun Steam Anda secara aman:

1. **Login Interaktif (Setup Session):**
   ```bash
   docker run --rm -it -v litevalley_steam_session:/data/steam-session sdvd/steam-service:preview setup
   ```
   *Masukkan Username, Password, dan kode 5-digit Steam Guard (2FA) saat diminta.*

2. **Ekspor Refresh Token:**
   ```bash
   docker run --rm -v litevalley_steam_session:/data/steam-session sdvd/steam-service:preview export-token
   ```
   *Salin string token yang dicetak, lalu buka file `.env` dan tempelkan ke variabel:*
   ```env
   STEAM_REFRESH_TOKEN=string_token_anda_di_sini
   ```

---

### Langkah 3: Jalankan Server!

```bash
docker compose up -d
```

Pantau proses inisialisasi server dengan:
```bash
docker compose logs -f stardew-server
```

Setelah server online:
- **Web Control Panel:** Buka `http://IP-SERVER-ANDA:5000` di peramban web.
- **Masuk Sysadmin:** Klik tombol **Admin Login** di pojok kanan atas dan masukkan kata sandi yang dikonfigurasi di `ADMIN_PASSWORD` (default: `ChangeMe123!`).

---

## 🎮 Cara Pemain Bergabung ke Server

1. Buka game **Stardew Valley** (PC v1.6+).
2. Pilih menu **Co-op** -> **Join LAN Game**.
3. Masukkan alamat IP server Anda: `<IP-SERVER>:24642`.
4. Pilih kabin yang tersedia dan selamat bertani bersama! 🚜🌾

> **Tips:** Pemain juga dapat memasang mod SMAPI pendamping [**LiteValley Bridge**](https://github.com/ijplabs/litevalley-bridge) untuk fitur obrolan lintas-platform dan teleportasi cepat.

---

## ⚙️ Variabel Lingkungan (.env)

| Variabel | Default | Keterangan |
| :--- | :--- | :--- |
| `PORT` | `24642` | Port game Netcode Lidgren UDP (Buka di Firewall/NAT) |
| `WEB_PORT` | `5000` | Port HTTP Web UI Control Panel |
| `ADMIN_USERNAME` | `admin` | Username login sysadmin Web UI |
| `ADMIN_PASSWORD` | `ChangeMe123!` | Password login sysadmin Web UI |
| `SERVER_NAME` | `LiteValley Server` | Nama server yang tampil di Web UI |
| `FARM_NAME` | `LiteFarm` | Nama lahan pertanian |
| `FARM_TYPE` | `0` | Tipe peta lahan (0=Standard, 1=River, 2=Forest, dsb.) |
| `MAX_PLAYERS` | `10` | Batas maksimum pemain bersamaan |
| `STEAM_REFRESH_TOKEN`| *(kosong)* | Token autentikasi Steam resmi untuk mengunduh aset game |
| `REDIS_PASSWORD` | `litevalley_...` | Sandi internal koneksi Redis |
| `TZ` | `UTC` | Zona waktu kontainer (contoh: `Asia/Jakarta`) |

---

## 💻 Pengembangan Lokal (Local Development)

Jika Anda ingin mengompilasi dan memodifikasi server secara langsung dari kode sumber:

### Prasyarat
- [.NET 6.0 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)
- [Bun](https://bun.sh/) (atau Node.js 18+)

### 1. Bangun Web UI
```bash
cd src/StardewHeadless.WebUI
bun install
bun run build
cd ../..
```
Aset hasil build akan otomatis terdistribusi ke `src/StardewHeadless.Server/wwwroot/`.

### 2. Bangun C# Backend
```bash
dotnet restore LiteValley.Headless.sln
dotnet build LiteValley.Headless.sln -c Release
```

---

## 📜 Kepatuhan Hukum & Etika (Legal Compliance)

LiteValley menghormati karya intelektual ConcernedApe (Eric Barone). Proyek ini **sama sekali tidak mendistribusikan file proprietary game**. Seluruh berkas resmi diunduh langsung dari server Valve Steam menggunakan akun berlisensi sah dari operator server.

Baca rincian selengkapnya di [LEGAL_COMPLIANCE.md](LEGAL_COMPLIANCE.md).

---

## 📄 Lisensi

Didistribusikan di bawah Lisensi [MIT](LICENSE). Dibuat dengan bangga oleh [IJPLabs](https://github.com/ijplabs).
