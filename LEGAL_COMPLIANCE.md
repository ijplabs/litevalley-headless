# 📜 Kepatuhan Lisensi & Penghormatan Kreator Game (Legal Compliance & Developer Tribute)

> **"Dedicated with immense gratitude and respect to Eric Barone (ConcernedApe) for creating Stardew Valley, an extraordinary masterpiece that brings joy, friendship, and peace to millions of farmers worldwide."**

---

## 🌾 1. Filosofi & Pernyataan Etika (Ethical Statement)

Proyek **LiteValley** (Headless Dedicated Server & [LiteValley Bridge](https://github.com/ijplabs/litevalley-bridge)) dibangun dengan komitmen teguh untuk **menjaga, menghormati, dan melindungi ekosistem resmi Stardew Valley**:

1. **Anti-Piracy (Kepatuhan Penuh Terhadap Hak Cipta):**
   * LiteValley **TIDAK PERNAH** membundel, meng-host, membagikan, atau mendistribusikan aset berhak cipta milik ConcernedApe (termasuk file `.xnb`, gambar, audio, dan biner asli game `Stardew Valley.dll`).
   * Repositori ini 100% *clean-room* dan hanya berisi kode integrasi C# headless runtime, protokol sinkronisasi netcode, dan antarmuka web monitoring buatan kami sendiri.

2. **Kewajiban Kepemilikan Game Resmi:**
   * Setiap operator server (Host / Sysadmin) dan setiap pemain yang bergabung ke server LiteValley **WAJIB memiliki salinan game Stardew Valley berlisensi sah** yang dibeli melalui platform resmi:
     * **Steam** (PC Windows / Linux / macOS)
     * **GOG.com** (PC DRM-Free)
     * **Humble Store** (PC)
     * **PC Game Pass / Microsoft Store**
   * Kami melarang keras penggunaan salinan bajakan atau repack ilegal dalam ekosistem ini.

---

## 🔑 2. Standar Pengadaan Berkas Server: Steam Resmi (Bukan Salinan Bajakan)

Server LiteValley **tidak menggunakan berkas game bajakan**. Untuk menyediakan dependensi game asli yang dibutuhkan oleh runtime server, sistem menyediakan metode resmi melalui **SteamKit2 / Steam Service (`sdvd/steam-service:preview`)**:

### Mengapa Menggunakan `STEAM_REFRESH_TOKEN`?
* **Aman & Transparan:** Kredensial Steam akun pemilik server diverifikasi langsung ke server resmi Valve.
* **Tanpa Simpan Password Mentah:** Refresh token Steam hanya memiliki izin untuk mengunduh paket depot game (*depot download*), dan **sama sekali tidak memiliki izin** untuk transaksi finansial (Steam Wallet), pengubahan kata sandi, maupun transfer inventaris.
* **Integritas Versi Asli:** Berkas yang diunduh langsung diverifikasi (*checksum validate*) oleh Valve CDN, memastikan server berjalan di versi Stardew Valley resmi terbaru (1.6+).

---

## 🛠️ 3. Panduan Operator: Mendapatkan `STEAM_REFRESH_TOKEN` via CLI

Untuk mengotentikasi akun Steam Anda dan menghasilkan token unduhan resmi:

### Langkah 1: Login Interaktif (Setup Session)
Jalankan perintah ini sekali di terminal host server/VPS Anda:

```bash
docker run --rm -it -v litevalley_steam_session:/data/steam-session sdvd/steam-service:preview setup
```
* Masukkan **Username** dan **Password** Steam Anda saat diminta.
* Masukkan kode 5-digit **Steam Guard (2FA)** dari aplikasi Steam Mobile atau Email Anda.
* Sesi terotentikasi akan tersimpan dengan aman di dalam volume Docker `litevalley_steam_session`.

### Langkah 2: Lihat & Ekspor Token (Get Token)
Jalankan perintah ini untuk melihat token unduhan yang dihasilkan:

```bash
docker run --rm -v litevalley_steam_session:/data/steam-session sdvd/steam-service:preview export-token
```
* Salin string token yang muncul di layar terminal.

### Langkah 3: Pasang Token ke Environment
Pasang token tersebut ke file `.env` di direktori proyek Anda:

```env
STEAM_REFRESH_TOKEN=isi_token_hasil_export_di_sini
```

Saat service dijalankan (`docker compose up -d`), container `steam-downloader` akan otomatis mengunduh berkas Stardew Valley resmi (AppID `413150`) ke volume `litevalley_game_data` secara hening (*headless*) tanpa meminta password atau kode Steam Guard lagi.

---

## 🤝 4. Dukungan Penuh Terhadap Ekosistem Modding SMAPI

LiteValley mematuhi pedoman komunitas modding yang dirintis oleh **Pathoschild** dan tim SMAPI:
* Seluruh fitur konektivitas klien diintegrasikan melalui mod client resmi [LiteValley Bridge](https://github.com/ijplabs/litevalley-bridge).
* Tidak ada manipulasi kode biner yang melanggar ketentuan lisensi modding resmi ConcernedApe.

---
*Dibuat dengan cinta untuk komunitas petani lembah. Selalu dukung kreator dengan membeli karya aslinya!* 🌱✨
