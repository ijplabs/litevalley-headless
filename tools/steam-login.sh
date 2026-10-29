#!/usr/bin/env bash
# ====================================================================
# LiteValley - Steam Service Token Manager (sdvd/steam-service:preview)
# ====================================================================

set -e

ACTION="${1:-setup}"

case "$ACTION" in
    setup)
        echo "=========================================================="
        echo "   LiteValley - Steam Setup (Interactive Login & 2FA)"
        echo "=========================================================="
        echo "Menjalankan container otentikasi Steam resmi..."
        echo ""
        docker run --rm -it -v litevalley_steam_session:/data/steam-session sdvd/steam-service:preview setup
        echo ""
        echo "=========================================================="
        echo "Sesi tersimpan! Untuk melihat STEAM_REFRESH_TOKEN, jalankan:"
        echo "./tools/steam-login.sh export-token"
        echo "=========================================================="
        ;;
    export-token)
        echo "=========================================================="
        echo "   LiteValley - STEAM_REFRESH_TOKEN Export"
        echo "=========================================================="
        docker run --rm -v litevalley_steam_session:/data/steam-session sdvd/steam-service:preview export-token
        echo ""
        echo "Salin token di atas dan masukkan ke .env:"
        echo "STEAM_REFRESH_TOKEN=<token>"
        echo "=========================================================="
        ;;
    *)
        echo "Penggunaan: $0 [setup | export-token]"
        exit 1
        ;;
esac
