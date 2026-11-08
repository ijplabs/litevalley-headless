import React, { useState, useEffect } from 'react';
import {
  ServerTelemetry,
  PlayerData,
  AuthUser,
  TreasuryData,
  PlayerOtpInfo,
} from '../types';
import {
  executeAdminAction,
  formatUptime,
  fetchPlayerOtp,
  respecPlayerProfession,
  unlinkPlayerCharacter,
} from '../lib/api';
import {
  Cpu,
  HardDrive,
  Users,
  Clock,
  Save,
  Sun,
  CloudRain,
  CloudLightning,
  Wind,
  Coins,
  Calendar,
  Sprout,
  AlertCircle,
  CheckCircle2,
  KeyRound,
  Copy,
  Check,
  Download,
  Megaphone,
  ShieldBan,
  ShieldCheck,
  Trash2,
  RefreshCw,
  Unlink,
  UserPlus,
  Sparkles,
} from 'lucide-react';

interface OverviewTabProps {
  telemetry: ServerTelemetry | null;
  players: PlayerData[];
  treasury: TreasuryData | null;
  user: AuthUser | null;
  onRefresh: () => void;
  onOpenAuth: (mode?: 'login' | 'register') => void;
}

const SEASON_LABELS: Record<string, string> = {
  spring: 'Musim Semi (Spring)',
  summer: 'Musim Panas (Summer)',
  fall: 'Musim Gugur (Fall)',
  winter: 'Musim Dingin (Winter)',
};

const PROFESSION_LABELS: Record<string, string> = {
  farmer: 'Petani (Farmer)',
  rancher: 'Peternak (Rancher)',
  miner: 'Penambang (Miner)',
  fisherman: 'Nelayan (Fisherman)',
  forager: 'Peramu (Forager)',
};

export const OverviewTab: React.FC<OverviewTabProps> = ({
  telemetry,
  players,
  treasury,
  user,
  onRefresh,
  onOpenAuth,
}) => {
  const [loadingAction, setLoadingAction] = useState<string | null>(null);
  const [notification, setNotification] = useState<{ type: 'success' | 'error'; message: string } | null>(null);
  const [broadcastText, setBroadcastText] = useState('');

  // State khusus kartu OTP Pemain
  const [otpInfo, setOtpInfo] = useState<PlayerOtpInfo | null>(null);
  const [copiedOtp, setCopiedOtp] = useState(false);
  const [selectedNewProf, setSelectedNewProf] = useState(user?.profession || 'farmer');
  const [unlinkConfirmInput, setUnlinkConfirmInput] = useState('');
  const [showUnlinkBox, setShowUnlinkBox] = useState(false);

  const isAdmin = user?.role === 'admin';
  const isPlayer = user?.role === 'player';

  const notify = (type: 'success' | 'error', message: string) => {
    setNotification({ type, message });
    setTimeout(() => setNotification(null), 4500);
  };

  // Muat & polling kode OTP untuk pemain yang sedang login
  useEffect(() => {
    if (!user) {
      setOtpInfo(null);
      return;
    }

    let active = true;
    const loadOtp = async () => {
      try {
        const info = await fetchPlayerOtp(user.username);
        if (active) {
          setOtpInfo(info);
          if (info.profession) setSelectedNewProf(info.profession.toLowerCase());
        }
      } catch {
        // Abaikan jika user adalah admin murni tanpa record tabel users
      }
    };

    loadOtp();
    const pollTimer = setInterval(loadOtp, 5000);
    const countdownTimer = setInterval(() => {
      setOtpInfo((prev) =>
        prev && prev.remainingSeconds > 1
          ? { ...prev, remainingSeconds: prev.remainingSeconds - 1 }
          : prev
      );
    }, 1000);

    return () => {
      active = false;
      clearInterval(pollTimer);
      clearInterval(countdownTimer);
    };
  }, [user]);

  const handleCopyOtp = () => {
    if (!otpInfo) return;
    navigator.clipboard.writeText(otpInfo.command);
    setCopiedOtp(true);
    setTimeout(() => setCopiedOtp(false), 2000);
  };

  const handlePlayerRespec = async () => {
    if (!user) return;
    setLoadingAction('respec');
    const res = await respecPlayerProfession(user.username, selectedNewProf);
    setLoadingAction(null);
    if (res.ok) {
      notify('success', res.message);
      onRefresh();
    } else {
      notify('error', res.message);
    }
  };

  const handlePlayerUnlink = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!user || !unlinkConfirmInput.trim()) return;
    setLoadingAction('unlink');
    const res = await unlinkPlayerCharacter(user.username, unlinkConfirmInput.trim());
    setLoadingAction(null);
    if (res.ok) {
      notify('success', res.message);
      setShowUnlinkBox(false);
      setUnlinkConfirmInput('');
      onRefresh();
    } else {
      notify('error', res.message);
    }
  };

  const handleAdminAction = async (label: string, endpoint: string, payload?: any) => {
    if (!isAdmin) return;
    setLoadingAction(label);
    const res = await executeAdminAction(endpoint, payload);
    setLoadingAction(null);
    if (res.ok) {
      notify('success', res.message || `Tindakan [${label}] berhasil dijalankan.`);
      onRefresh();
    } else {
      notify('error', res.message || `Gagal menjalankan [${label}].`);
    }
  };

  const handleSendBroadcast = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!broadcastText.trim()) return;
    const msg = broadcastText.trim();
    setBroadcastText('');
    await handleAdminAction('Broadcast Chat', '/api/admin/broadcast', { message: msg });
  };

  const seasonDisplay = telemetry?.season
    ? SEASON_LABELS[telemetry.season.toLowerCase()] || telemetry.season
    : 'Menunggu Data...';

  const onlinePlayersCount = telemetry?.onlinePlayers ?? players.filter((p) => p.isOnline).length;
  const maxPlayersCount = telemetry?.maxPlayers ?? 4;

  return (
    <div className="space-y-6">
      {/* Notifikasi */}
      {notification && (
        <div
          className={`p-4 rounded-xl flex items-center space-x-3 text-sm font-medium ${
            notification.type === 'success'
              ? 'bg-emerald-500/10 border border-emerald-500/30 text-emerald-700 dark:text-emerald-300'
              : 'bg-rose-500/10 border border-rose-500/30 text-rose-700 dark:text-rose-300'
          }`}
        >
          {notification.type === 'success' ? (
            <CheckCircle2 className="w-5 h-5 flex-shrink-0 text-emerald-500 dark:text-emerald-400" />
          ) : (
            <AlertCircle className="w-5 h-5 flex-shrink-0 text-rose-500 dark:text-rose-400" />
          )}
          <span>{notification.message}</span>
        </div>
      )}

      {/* Banner Tamu (Jika Belum Login) */}
      {!user && (
        <div className="bg-gradient-to-r from-emerald-50 via-white to-white dark:from-emerald-950/40 dark:via-slate-900 dark:to-slate-900 border border-emerald-200 dark:border-emerald-500/20 rounded-xl p-5 flex flex-wrap items-center justify-between gap-4 shadow-sm transition-colors">
          <div className="flex items-center space-x-3.5">
            <div className="w-11 h-11 rounded-xl bg-emerald-500/10 border border-emerald-500/30 flex items-center justify-center text-emerald-600 dark:text-emerald-400 flex-shrink-0">
              <Sprout className="w-6 h-6" />
            </div>
            <div>
              <div className="flex items-center space-x-2">
                <h2 className="font-bold text-slate-900 dark:text-slate-100 text-sm sm:text-base">
                  Kebun {telemetry?.farmName || 'LiteValley'}
                </h2>
                <span className="text-[10px] font-semibold px-2 py-0.5 rounded-full bg-emerald-500/10 text-emerald-700 dark:text-emerald-400 border border-emerald-500/30 flex items-center space-x-1">
                  <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 dark:bg-emerald-400 animate-pulse"></span>
                  <span>Siap Dimainkan</span>
                </span>
              </div>
              <p className="text-xs text-slate-600 dark:text-slate-400 mt-0.5">
                Daftar akun petani untuk memilih profesi awal dan mendapatkan kode autentikasi <code className="font-mono text-emerald-600 dark:text-emerald-400">!auth</code> di dalam game.
              </p>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <a
              href="/api/download/client-mod"
              className="flex items-center space-x-1.5 px-3 py-2 rounded-lg text-xs font-semibold bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 text-slate-800 dark:text-slate-200 border border-slate-200 dark:border-slate-700 transition-colors"
            >
              <Download className="w-3.5 h-3.5 text-emerald-500" />
              <span>Unduh Mod StardewBridge</span>
            </a>
            <button
              onClick={() => onOpenAuth('register')}
              className="flex items-center space-x-1.5 px-3.5 py-2 rounded-lg text-xs font-semibold bg-emerald-600 hover:bg-emerald-500 text-white shadow transition-colors"
            >
              <UserPlus className="w-3.5 h-3.5" />
              <span>Daftar &amp; Ambil Kode !auth</span>
            </button>
          </div>
        </div>
      )}

      {/* Kartu Identitas & Kode OTP !auth Pemain */}
      {(isPlayer || otpInfo) && (
        <div className="bg-white dark:bg-slate-900 border border-emerald-500/30 rounded-xl p-5 shadow-sm transition-colors">
          <div className="flex flex-wrap items-start justify-between gap-4 pb-4 border-b border-slate-200 dark:border-slate-800">
            <div className="flex items-center space-x-3">
              <div className="p-2.5 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 rounded-xl border border-emerald-500/20">
                <KeyRound className="w-5 h-5" />
              </div>
              <div>
                <div className="flex items-center space-x-2">
                  <h3 className="font-bold text-slate-900 dark:text-slate-100 text-sm">
                    Kartu Identitas Petani &amp; Kode Masuk In-Game (TOTP)
                  </h3>
                  <span
                    className={`text-[10px] font-bold px-2 py-0.5 rounded-full border ${
                      otpInfo?.isGameLinked
                        ? 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border-emerald-500/30'
                        : 'bg-amber-500/10 text-amber-600 dark:text-amber-400 border-amber-500/30'
                    }`}
                  >
                    {otpInfo?.isGameLinked
                      ? `Tertaut: ${otpInfo.linkedFarmerName || 'Karakter Aktif'}`
                      : 'Belum Tertaut ke Karakter Game'}
                  </span>
                </div>
                <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
                  Salin perintah di bawah lalu tempelkan ke kotak chat Stardew Valley saat berada di lobi kabin.
                </p>
              </div>
            </div>

            <a
              href="/api/download/client-mod"
              className="flex items-center space-x-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 text-slate-800 dark:text-slate-200 border border-slate-200 dark:border-slate-700 transition-colors"
            >
              <Download className="w-3.5 h-3.5 text-emerald-500" />
              <span>Unduh StardewBridge v0.3.0</span>
            </a>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4 pt-4">
            {/* Kotak Perintah !auth */}
            <div className="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800 rounded-xl p-4 flex items-center justify-between">
              <div>
                <div className="text-[11px] font-medium text-slate-500 dark:text-slate-400">
                  Perintah Chat In-Game (Berlaku {otpInfo?.remainingSeconds ?? 30} detik)
                </div>
                <div className="text-xl font-mono font-bold text-emerald-600 dark:text-emerald-400 tracking-wider mt-1">
                  {otpInfo?.command || '!auth ------'}
                </div>
              </div>
              <button
                onClick={handleCopyOtp}
                disabled={!otpInfo}
                className="flex items-center space-x-1.5 px-3.5 py-2 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-semibold shadow transition-colors"
              >
                {copiedOtp ? <Check className="w-4 h-4" /> : <Copy className="w-4 h-4" />}
                <span>{copiedOtp ? 'Tersalin!' : 'Salin Perintah'}</span>
              </button>
            </div>

            {/* Pengaturan Profesi & Unlink Karakter */}
            <div className="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800 rounded-xl p-4 flex flex-col justify-between gap-3">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div>
                  <div className="text-[11px] font-medium text-slate-500 dark:text-slate-400">
                    Profesi Spesialisasi Saat Ini
                  </div>
                  <div className="text-xs font-bold text-slate-900 dark:text-slate-100 uppercase mt-0.5">
                    {PROFESSION_LABELS[(otpInfo?.profession || user?.profession || 'farmer').toLowerCase()] ||
                      otpInfo?.profession ||
                      'Farmer'}
                  </div>
                </div>

                <div className="flex items-center space-x-2">
                  <select
                    value={selectedNewProf}
                    onChange={(e) => setSelectedNewProf(e.target.value)}
                    className="bg-white dark:bg-slate-900 border border-slate-300 dark:border-slate-700 rounded-lg px-2.5 py-1.5 text-xs text-slate-900 dark:text-slate-100"
                  >
                    <option value="farmer">Farmer</option>
                    <option value="rancher">Rancher</option>
                    <option value="miner">Miner</option>
                    <option value="fisherman">Fisherman</option>
                    <option value="forager">Forager</option>
                  </select>
                  <button
                    onClick={handlePlayerRespec}
                    disabled={loadingAction === 'respec'}
                    className="flex items-center space-x-1 px-3 py-1.5 rounded-lg bg-blue-600 hover:bg-blue-500 text-white text-xs font-semibold transition-colors"
                  >
                    <RefreshCw className={`w-3.5 h-3.5 ${loadingAction === 'respec' ? 'animate-spin' : ''}`} />
                    <span>Ubah Profesi</span>
                  </button>
                  {otpInfo?.isGameLinked && (
                    <button
                      type="button"
                      onClick={() => setShowUnlinkBox(!showUnlinkBox)}
                      className="p-1.5 rounded-lg bg-rose-500/10 hover:bg-rose-500/20 text-rose-600 dark:text-rose-400 border border-rose-500/20"
                      title="Putuskan Tautan Karakter Game"
                    >
                      <Unlink className="w-3.5 h-3.5" />
                    </button>
                  )}
                </div>
              </div>

              {showUnlinkBox && (
                <form onSubmit={handlePlayerUnlink} className="flex items-center space-x-2 pt-2 border-t border-slate-200 dark:border-slate-800">
                  <input
                    type="password"
                    value={unlinkConfirmInput}
                    onChange={(e) => setUnlinkConfirmInput(e.target.value)}
                    placeholder="Masukkan password atau kode OTP untuk konfirmasi unlink"
                    className="flex-1 bg-white dark:bg-slate-900 border border-slate-300 dark:border-slate-700 rounded-lg px-2.5 py-1 text-xs"
                    required
                  />
                  <button
                    type="submit"
                    disabled={loadingAction === 'unlink'}
                    className="px-3 py-1 rounded-lg bg-rose-600 hover:bg-rose-500 text-white text-xs font-semibold"
                  >
                    Konfirmasi Unlink
                  </button>
                </form>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Kontrol Operasional Server (KHUSUS SYSADMIN) */}
      {isAdmin && (
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 space-y-4 shadow-sm transition-colors">
          <div className="flex flex-wrap items-center justify-between gap-4">
            <div>
              <div className="flex items-center space-x-2">
                <span className="text-[10px] font-bold uppercase tracking-wider px-2 py-0.5 rounded bg-emerald-500/10 text-emerald-700 dark:text-emerald-400 border border-emerald-500/20">
                  Mode Sysadmin
                </span>
                <h2 className="text-sm font-semibold text-slate-900 dark:text-slate-200">
                  Kontrol Operasional &amp; Pengumuman Dunia
                </h2>
              </div>
              <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
                Simpan paksa progres permainan atau kirim pengumuman langsung ke layar chat seluruh petani online
              </p>
            </div>

            <div className="flex flex-wrap items-center gap-2">
              <button
                onClick={() => handleAdminAction('Simpan Dunia', '/api/admin/save')}
                disabled={loadingAction !== null}
                className="flex items-center space-x-1.5 px-3.5 py-2 rounded-lg text-xs font-semibold bg-emerald-600 hover:bg-emerald-500 text-white transition-colors shadow"
              >
                <Save className="w-3.5 h-3.5" />
                <span>{loadingAction === 'Simpan Dunia' ? 'Menyimpan...' : 'Simpan Dunia Sekarang'}</span>
              </button>
            </div>
          </div>

          {/* Form Broadcast Pengumuman In-Game */}
          <form onSubmit={handleSendBroadcast} className="flex items-center gap-2 pt-2 border-t border-slate-200 dark:border-slate-800">
            <div className="relative flex-1">
              <Megaphone className="w-4 h-4 text-slate-400 absolute left-3 top-2.5" />
              <input
                type="text"
                value={broadcastText}
                onChange={(e) => setBroadcastText(e.target.value)}
                placeholder="Ketik pesan pengumuman [SERVER] untuk seluruh pemain di dalam game..."
                className="w-full bg-slate-50 dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded-lg pl-9 pr-3 py-2 text-xs text-slate-900 dark:text-slate-100 placeholder-slate-400 focus:outline-none focus:border-emerald-500"
              />
            </div>
            <button
              type="submit"
              disabled={!broadcastText.trim() || loadingAction !== null}
              className="px-4 py-2 rounded-lg bg-blue-600 hover:bg-blue-500 disabled:opacity-50 text-white text-xs font-semibold transition-colors shadow-sm"
            >
              Kirim Broadcast
            </button>
          </form>
        </div>
      )}

      {/* Kartu Metrik Mesin Server Riil (0% Dummy Data) */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        {/* Tick Rate / FPS */}
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm transition-colors">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-slate-500 dark:text-slate-400">Kecepatan Simulasi</span>
            <div className="p-2 bg-blue-500/10 rounded-lg text-blue-600 dark:text-blue-400 border border-blue-500/20">
              <Cpu className="w-4 h-4" />
            </div>
          </div>
          <div className="mt-2 text-2xl font-bold text-slate-900 dark:text-slate-100">
            {telemetry ? `${telemetry.fps.toFixed(0)} FPS` : '—'}
          </div>
          <div className="mt-2 text-xs text-slate-500 dark:text-slate-400">
            {telemetry?.isPaused ? 'Simulasi sedang dijeda (0 pemain aktif)' : 'Game loop berjalan stabil (60 Hz)'}
          </div>
        </div>

        {/* RAM */}
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm transition-colors">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-slate-500 dark:text-slate-400">Memori Kerja (RAM)</span>
            <div className="p-2 bg-purple-500/10 rounded-lg text-purple-600 dark:text-purple-400 border border-purple-500/20">
              <HardDrive className="w-4 h-4" />
            </div>
          </div>
          <div className="mt-2 text-2xl font-bold text-slate-900 dark:text-slate-100">
            {telemetry ? `${telemetry.memoryMB}` : '—'}{' '}
            <span className="text-sm font-normal text-slate-500 dark:text-slate-400">MB</span>
          </div>
          <div className="mt-2 text-xs text-slate-500 dark:text-slate-400">
            Working Set .NET 6 + Rust Native Core
          </div>
        </div>

        {/* Kapasitas Pemain */}
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm transition-colors">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-slate-500 dark:text-slate-400">Slot Multipemain</span>
            <div className="p-2 bg-emerald-500/10 rounded-lg text-emerald-600 dark:text-emerald-400 border border-emerald-500/20">
              <Users className="w-4 h-4" />
            </div>
          </div>
          <div className="mt-2 text-2xl font-bold text-slate-900 dark:text-slate-100">
            {onlinePlayersCount} / {maxPlayersCount}
          </div>
          <div className="mt-2 text-xs text-emerald-600 dark:text-emerald-400">
            {players.length} akun petani terdaftar di database
          </div>
        </div>

        {/* Uptime */}
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm transition-colors">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium text-slate-500 dark:text-slate-400">Waktu Aktif (Uptime)</span>
            <div className="p-2 bg-amber-500/10 rounded-lg text-amber-600 dark:text-amber-400 border border-amber-500/20">
              <Clock className="w-4 h-4" />
            </div>
          </div>
          <div className="mt-2 text-2xl font-bold text-slate-900 dark:text-slate-100">
            {telemetry ? formatUptime(telemetry.uptimeSeconds) : '—'}
          </div>
          <div className="mt-2 text-xs text-slate-500 dark:text-slate-400">
            UDP Game :24642 • Web API :8080
          </div>
        </div>
      </div>

      {/* Kartu Status Dunia Pertanian Riil */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-6 shadow-sm transition-colors">
        <div className="flex flex-wrap items-center justify-between pb-4 border-b border-slate-200 dark:border-slate-800 gap-2">
          <div>
            <h3 className="font-semibold text-slate-900 dark:text-slate-100">Status Dunia Pertanian</h3>
            <p className="text-xs text-slate-500 dark:text-slate-400">
              Data langsung dari memori simulasi Stardew Valley 1.6
            </p>
          </div>
          <span className="text-xs font-mono bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-300 px-2.5 py-1 rounded-md border border-slate-200 dark:border-slate-700">
            Kebun: {telemetry?.farmName || 'Memuat...'} • Silo: {telemetry?.siloCount ?? 0}
          </span>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6 pt-5">
          {/* Kalender Game */}
          <div className="flex items-start space-x-3">
            <div className="p-2.5 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 rounded-lg border border-emerald-500/20">
              <Calendar className="w-5 h-5" />
            </div>
            <div>
              <div className="text-xs text-slate-500 dark:text-slate-400 font-medium">Kalender Game</div>
              <div className="text-base font-bold text-slate-900 dark:text-slate-100 mt-0.5 capitalize">
                Hari ke-{telemetry?.dayOfMonth ?? 1}, {seasonDisplay}
              </div>
              <div className="text-xs text-slate-500 dark:text-slate-400">
                Tahun ke-{telemetry?.year ?? 1}
              </div>
            </div>
          </div>

          {/* Waktu In-Game */}
          <div className="flex items-start space-x-3">
            <div className="p-2.5 bg-amber-500/10 text-amber-600 dark:text-amber-400 rounded-lg border border-amber-500/20">
              <Clock className="w-5 h-5" />
            </div>
            <div>
              <div className="text-xs text-slate-500 dark:text-slate-400 font-medium">Jam In-Game</div>
              <div className="text-base font-bold text-slate-900 dark:text-slate-100 mt-0.5">
                {telemetry?.formattedTime || '06:00 AM'}
              </div>
              <div className="text-xs text-slate-500 dark:text-slate-400">
                {telemetry?.isPaused ? 'Waktu Dijeda (Menunggu Pemain)' : 'Waktu Berjalan Aktif'}
              </div>
            </div>
          </div>

          {/* Cuaca & Keberuntungan */}
          <div className="flex items-start space-x-3">
            <div className="p-2.5 bg-blue-500/10 text-blue-600 dark:text-blue-400 rounded-lg border border-blue-500/20">
              {telemetry?.weather === 'Rain' ? (
                <CloudRain className="w-5 h-5" />
              ) : telemetry?.weather === 'Storm' ? (
                <CloudLightning className="w-5 h-5" />
              ) : telemetry?.weather === 'Windy' ? (
                <Wind className="w-5 h-5" />
              ) : (
                <Sun className="w-5 h-5" />
              )}
            </div>
            <div>
              <div className="text-xs text-slate-500 dark:text-slate-400 font-medium">Cuaca &amp; Hoki Harian</div>
              <div className="text-base font-bold text-slate-900 dark:text-slate-100 mt-0.5">
                {telemetry?.weather || 'Sunny'}
              </div>
              <div className="text-xs text-slate-500 dark:text-slate-400 flex items-center space-x-1">
                <Sparkles className="w-3 h-3 text-amber-400" />
                <span>Luck: {telemetry?.dailyLuck ?? 0}</span>
              </div>
            </div>
          </div>

          {/* Kas Desa (Village Treasury) */}
          <div className="flex items-start space-x-3">
            <div className="p-2.5 bg-yellow-500/10 text-yellow-600 dark:text-yellow-400 rounded-lg border border-yellow-500/20">
              <Coins className="w-5 h-5" />
            </div>
            <div>
              <div className="text-xs text-slate-500 dark:text-slate-400 font-medium">Brankas Kas Desa</div>
              <div className="text-base font-bold text-yellow-600 dark:text-yellow-400 mt-0.5">
                {(treasury?.balance ?? 0).toLocaleString('id-ID')} G
              </div>
              <div className="text-xs text-slate-500 dark:text-slate-400">
                Pajak Ekspor Desa: {((treasury?.taxRate ?? 0.05) * 100).toFixed(0)}%
              </div>
            </div>
          </div>
        </div>

        {telemetry?.fortuneQuote && (
          <div className="mt-5 pt-4 border-t border-slate-200 dark:border-slate-800 text-xs italic text-slate-600 dark:text-slate-400">
            🔮 Ramalan Roh Hari Ini: {telemetry.fortuneQuote}
          </div>
        )}
      </div>

      {/* Tabel Pemain Terdaftar & Kabin */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden shadow-sm transition-colors">
        <div className="p-4 sm:p-5 border-b border-slate-200 dark:border-slate-800 flex items-center justify-between">
          <div>
            <h3 className="font-semibold text-slate-900 dark:text-slate-100">Daftar Warga &amp; Petani Terdaftar</h3>
            <p className="text-xs text-slate-500 dark:text-slate-400">
              Status kehadiran pemain, alokasi kabin, profesi spesialisasi, dan dompet pribadi
            </p>
          </div>
          <span className="text-xs font-semibold px-2.5 py-1 rounded-full bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-300 border border-slate-200 dark:border-slate-700">
            {onlinePlayersCount} Online / {players.length} Terdaftar
          </span>
        </div>

        {players.length === 0 ? (
          <div className="p-8 text-center text-slate-500 dark:text-slate-400 text-sm">
            Belum ada akun petani yang terdaftar di server ini.
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 dark:bg-slate-950/60 text-xs uppercase font-medium text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800">
                <tr>
                  <th className="py-3 px-4">Username Petani</th>
                  <th className="py-3 px-4">Status</th>
                  <th className="py-3 px-4">Alokasi Kabin</th>
                  <th className="py-3 px-4">Profesi</th>
                  <th className="py-3 px-4">Dompet</th>
                  {isAdmin && <th className="py-3 px-4 text-right">Moderasi Sysadmin</th>}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-200 dark:divide-slate-800/60 font-sans">
                {players.map((p) => (
                  <tr key={p.id} className="hover:bg-slate-50 dark:hover:bg-slate-800/30 transition-colors">
                    <td className="py-3 px-4">
                      <div className="flex items-center space-x-2">
                        <span
                          className={`w-2 h-2 rounded-full ${
                            p.isBanned
                              ? 'bg-rose-500'
                              : p.isOnline
                              ? 'bg-emerald-500 animate-pulse'
                              : 'bg-slate-400'
                          }`}
                        />
                        <span className="font-medium text-slate-900 dark:text-slate-200">{p.username}</span>
                      </div>
                    </td>
                    <td className="py-3 px-4">
                      {p.isBanned ? (
                        <span className="text-[11px] font-semibold px-2 py-0.5 rounded bg-rose-500/10 text-rose-600 dark:text-rose-400 border border-rose-500/20">
                          Diblokir (Banned)
                        </span>
                      ) : p.isOnline ? (
                        <span className="text-[11px] font-semibold px-2 py-0.5 rounded bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border border-emerald-500/20">
                          Sedang Online
                        </span>
                      ) : (
                        <span className="text-[11px] font-medium px-2 py-0.5 rounded bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-400">
                          Offline
                        </span>
                      )}
                    </td>
                    <td className="py-3 px-4 text-xs text-slate-700 dark:text-slate-300 font-mono">
                      {p.cabinName || 'Kabin Komunal'}
                    </td>
                    <td className="py-3 px-4 text-xs capitalize text-slate-700 dark:text-slate-300">
                      {PROFESSION_LABELS[(p.profession || '').toLowerCase()] || p.profession || 'Unassigned'}
                    </td>
                    <td className="py-3 px-4 text-xs font-mono text-yellow-600 dark:text-yellow-400 font-semibold">
                      {(p.money ?? 0).toLocaleString('id-ID')} G
                    </td>
                    {isAdmin && (
                      <td className="py-3 px-4 text-right">
                        <div className="flex items-center justify-end space-x-1.5">
                          <select
                            defaultValue={(p.profession || 'farmer').toLowerCase()}
                            onChange={(e) =>
                              handleAdminAction(
                                `Ubah Profesi ${p.username}`,
                                `/api/admin/players/${p.id}/change-profession`,
                                { profession: e.target.value }
                              )
                            }
                            className="bg-slate-50 dark:bg-slate-950 border border-slate-300 dark:border-slate-700 rounded px-2 py-1 text-[11px] text-slate-800 dark:text-slate-200"
                          >
                            <option value="farmer">Farmer</option>
                            <option value="rancher">Rancher</option>
                            <option value="miner">Miner</option>
                            <option value="fisherman">Fisherman</option>
                            <option value="forager">Forager</option>
                          </select>

                          {p.isBanned ? (
                            <button
                              onClick={() =>
                                handleAdminAction(`Unban ${p.username}`, `/api/admin/players/${p.id}/unban`)
                              }
                              className="flex items-center space-x-1 text-[11px] text-emerald-600 dark:text-emerald-400 font-medium px-2 py-1 rounded bg-emerald-500/10 hover:bg-emerald-500/20 border border-emerald-500/20"
                              title="Cabut Blokir (Unban)"
                            >
                              <ShieldCheck className="w-3 h-3" />
                              <span>Unban</span>
                            </button>
                          ) : (
                            <button
                              onClick={() =>
                                handleAdminAction(`Ban ${p.username}`, `/api/admin/players/${p.id}/ban`, {
                                  reason: 'Pelanggaran aturan server',
                                  banIp: true,
                                  banHwid: true,
                                })
                              }
                              className="flex items-center space-x-1 text-[11px] text-amber-600 dark:text-amber-400 font-medium px-2 py-1 rounded bg-amber-500/10 hover:bg-amber-500/20 border border-amber-500/20"
                              title="Blokir Pemain & Kick"
                            >
                              <ShieldBan className="w-3 h-3" />
                              <span>Ban</span>
                            </button>
                          )}

                          <button
                            onClick={() =>
                              handleAdminAction(`Purge ${p.username}`, `/api/admin/players/${p.id}/purge`, {
                                reason: 'Pembersihan akun oleh Sysadmin',
                              })
                            }
                            className="flex items-center space-x-1 text-[11px] text-rose-600 dark:text-rose-400 font-medium px-2 py-1 rounded bg-rose-500/10 hover:bg-rose-500/20 border border-rose-500/20"
                            title="Hapus Pemain & Bebaskan Kabin"
                          >
                            <Trash2 className="w-3 h-3" />
                            <span>Purge</span>
                          </button>
                        </div>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
};
