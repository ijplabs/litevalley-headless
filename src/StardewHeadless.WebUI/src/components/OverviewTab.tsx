import React, { useState } from 'react';
import { ServerTelemetry, PlayerData, AuthUser } from '../types';
import { executeAdminAction } from '../lib/api';
import { 
  Cpu, 
  HardDrive, 
  Wifi, 
  Clock, 
  Play, 
  Square, 
  RotateCw, 
  Save, 
  Sun, 
  CloudRain, 
  Coins, 
  Calendar, 
  Sprout,
  AlertCircle,
  CheckCircle2
} from 'lucide-react';

interface OverviewTabProps {
  telemetry: ServerTelemetry | null;
  players: PlayerData[];
  user: AuthUser | null;
  onRefresh: () => void;
}

export const OverviewTab: React.FC<OverviewTabProps> = ({
  telemetry,
  players,
  user,
  onRefresh,
}) => {
  const [loadingAction, setLoadingAction] = useState<string | null>(null);
  const [notification, setNotification] = useState<{ type: 'success' | 'error'; message: string } | null>(null);

  const isAdmin = user?.role === 'admin';
  const isOnline = telemetry?.status !== 'stopped';

  const handleAction = async (action: string, endpoint: string, payload?: any) => {
    if (!isAdmin) return;

    setLoadingAction(action);
    setNotification(null);

    const res = await executeAdminAction(endpoint, payload);
    setLoadingAction(null);

    if (res.ok) {
      setNotification({ type: 'success', message: `Perintah [${action}] berhasil dieksekusi.` });
      onRefresh();
    } else {
      setNotification({ type: 'error', message: `Gagal menjalankan [${action}]: ${res.message}` });
    }

    setTimeout(() => setNotification(null), 4000);
  };

  return (
    <div className="space-y-6">
      {/* Notifications */}
      {notification && (
        <div className={`p-4 rounded-lg flex items-center space-x-3 text-sm font-medium ${
          notification.type === 'success' 
            ? 'bg-emerald-500/10 border border-emerald-500/30 text-emerald-700 dark:text-emerald-300' 
            : 'bg-rose-500/10 border border-rose-500/30 text-rose-700 dark:text-rose-300'
        }`}>
          {notification.type === 'success' ? (
            <CheckCircle2 className="w-5 h-5 flex-shrink-0 text-emerald-500 dark:text-emerald-400" />
          ) : (
            <AlertCircle className="w-5 h-5 flex-shrink-0 text-rose-500 dark:text-rose-400" />
          )}
          <span>{notification.message}</span>
        </div>
      )}

      {/* Client Welcome Banner (Hanya Tampil Jika Belum/Bukan Admin) */}
      {!isAdmin && (
        <div className="bg-gradient-to-r from-emerald-50 via-white to-white dark:from-emerald-950/40 dark:via-slate-900 dark:to-slate-900 border border-emerald-200 dark:border-emerald-500/20 rounded-xl p-5 flex flex-wrap items-center justify-between gap-4 shadow-sm transition-colors">
          <div className="flex items-center space-x-3.5">
            <div className="w-10 h-10 rounded-xl bg-emerald-500/10 border border-emerald-500/30 flex items-center justify-center text-emerald-600 dark:text-emerald-400 flex-shrink-0">
              <Sprout className="w-5 h-5" />
            </div>
            <div>
              <div className="flex items-center space-x-2">
                <h2 className="font-bold text-slate-900 dark:text-slate-100 text-sm">{telemetry?.farm?.name || 'Pelican Haven'}</h2>
                <span className="text-[10px] font-semibold px-2 py-0.5 rounded-full bg-emerald-500/10 text-emerald-700 dark:text-emerald-400 border border-emerald-500/30 flex items-center space-x-1">
                  <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 dark:bg-emerald-400 animate-pulse"></span>
                  <span>Server Siap Dimainkan</span>
                </span>
              </div>
              <p className="text-xs text-slate-600 dark:text-slate-400 mt-0.5">
                Buka Stardew Valley (PC) dengan mod StardewBridge untuk masuk dan bermain bersama.
              </p>
            </div>
          </div>

          <div className="flex items-center space-x-2 text-xs text-slate-600 dark:text-slate-400 bg-slate-100 dark:bg-slate-950/60 px-3 py-1.5 rounded-lg border border-slate-200 dark:border-slate-800">
            <span>Pemain di Server:</span>
            <span className="font-bold text-emerald-600 dark:text-emerald-400">{players.length} Sedang Online</span>
          </div>
        </div>
      )}

      {/* Kontrol Operasional Server (KHUSUS SYSADMIN) */}
      {isAdmin && (
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 flex flex-wrap items-center justify-between gap-4 shadow-sm transition-colors">
          <div>
            <div className="flex items-center space-x-2">
              <span className="text-[10px] font-bold uppercase tracking-wider px-2 py-0.5 rounded bg-emerald-500/10 text-emerald-700 dark:text-emerald-400 border border-emerald-500/20">
                Mode Sysadmin
              </span>
              <h2 className="text-sm font-semibold text-slate-900 dark:text-slate-200">Kontrol Operasional Server</h2>
            </div>
            <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">Kelola status siklus hidup simulasi dedicated server</p>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <button
              onClick={() => handleAction('Start Server', '/api/admin/world/start')}
              disabled={isOnline || loadingAction !== null}
              className={`flex items-center space-x-1.5 px-3 py-2 rounded-lg text-xs font-semibold transition-colors ${
                !isOnline
                  ? 'bg-emerald-600 hover:bg-emerald-500 text-white shadow'
                  : 'bg-slate-100 dark:bg-slate-800 text-slate-400 dark:text-slate-500 cursor-not-allowed border border-slate-200 dark:border-slate-800'
              }`}
            >
              <Play className="w-3.5 h-3.5" />
              <span>Mulai</span>
            </button>

            <button
              onClick={() => handleAction('Stop Server', '/api/admin/world/stop')}
              disabled={!isOnline || loadingAction !== null}
              className={`flex items-center space-x-1.5 px-3 py-2 rounded-lg text-xs font-semibold transition-colors ${
                isOnline
                  ? 'bg-rose-600 hover:bg-rose-500 text-white shadow'
                  : 'bg-slate-100 dark:bg-slate-800 text-slate-400 dark:text-slate-500 cursor-not-allowed border border-slate-200 dark:border-slate-800'
              }`}
            >
              <Square className="w-3.5 h-3.5" />
              <span>Berhenti</span>
            </button>

            <button
              onClick={() => handleAction('Restart Server', '/api/admin/world/restart')}
              disabled={loadingAction !== null}
              className="flex items-center space-x-1.5 px-3 py-2 rounded-lg text-xs font-semibold bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 text-slate-800 dark:text-slate-200 border border-slate-300 dark:border-slate-700 transition-colors shadow-sm"
            >
              <RotateCw className={`w-3.5 h-3.5 ${loadingAction === 'Restart Server' ? 'animate-spin' : ''}`} />
              <span>Restart</span>
            </button>

            <button
              onClick={() => handleAction('Save Snapshot', '/api/admin/save')}
              disabled={!isOnline || loadingAction !== null}
              className="flex items-center space-x-1.5 px-3 py-2 rounded-lg text-xs font-semibold bg-blue-600 hover:bg-blue-500 text-white transition-colors shadow"
            >
              <Save className="w-3.5 h-3.5" />
              <span>Simpan Dunia</span>
            </button>
          </div>
        </div>
      )}

      {/* Kartu Hardware / Telemetri Teknis Server (KHUSUS SYSADMIN) */}
      {isAdmin && (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
          {/* CPU */}
          <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm transition-colors">
            <div className="flex items-center justify-between">
              <span className="text-xs font-medium text-slate-500 dark:text-slate-400">Penggunaan CPU</span>
              <div className="p-2 bg-blue-500/10 rounded-lg text-blue-600 dark:text-blue-400 border border-blue-500/20">
                <Cpu className="w-4 h-4" />
              </div>
            </div>
            <div className="mt-2 text-2xl font-bold text-slate-900 dark:text-slate-100">
              {telemetry?.cpuPercent ?? '3.8'}%
            </div>
            <div className="mt-2 w-full bg-slate-100 dark:bg-slate-800 h-1.5 rounded-full overflow-hidden">
              <div 
                className="bg-blue-500 h-full rounded-full transition-all" 
                style={{ width: `${Math.min(100, (telemetry?.cpuPercent || 4))}%` }}
              />
            </div>
          </div>

          {/* RAM */}
          <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm transition-colors">
            <div className="flex items-center justify-between">
              <span className="text-xs font-medium text-slate-500 dark:text-slate-400">Memori RAM</span>
              <div className="p-2 bg-purple-500/10 rounded-lg text-purple-600 dark:text-purple-400 border border-purple-500/20">
                <HardDrive className="w-4 h-4" />
              </div>
            </div>
            <div className="mt-2 text-2xl font-bold text-slate-900 dark:text-slate-100">
              {telemetry?.ramUsageMb ?? '480'} <span className="text-sm font-normal text-slate-500 dark:text-slate-400">MB</span>
            </div>
            <div className="mt-2 text-xs text-slate-500 dark:text-slate-400 flex justify-between">
              <span>Alokasi Native .NET</span>
              <span>Maks: {telemetry?.maxRamMb ?? '2048'} MB</span>
            </div>
          </div>

          {/* UDP Port */}
          <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm transition-colors">
            <div className="flex items-center justify-between">
              <span className="text-xs font-medium text-slate-500 dark:text-slate-400">Port Jaringan Game</span>
              <div className="p-2 bg-emerald-500/10 rounded-lg text-emerald-600 dark:text-emerald-400 border border-emerald-500/20">
                <Wifi className="w-4 h-4" />
              </div>
            </div>
            <div className="mt-2 text-2xl font-bold text-slate-900 dark:text-slate-100">
              :{telemetry?.port ?? '24642'}
            </div>
            <div className="mt-2 text-xs text-emerald-600 dark:text-emerald-400 flex items-center space-x-1">
              <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 dark:bg-emerald-400 inline-block"></span>
              <span>Lidgren UDP Socket Ready</span>
            </div>
          </div>

          {/* Uptime */}
          <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-4 shadow-sm transition-colors">
            <div className="flex items-center justify-between">
              <span className="text-xs font-medium text-slate-500 dark:text-slate-400">Server Uptime</span>
              <div className="p-2 bg-amber-500/10 rounded-lg text-amber-600 dark:text-amber-400 border border-amber-500/20">
                <Clock className="w-4 h-4" />
              </div>
            </div>
            <div className="mt-2 text-2xl font-bold text-slate-900 dark:text-slate-100">
              {telemetry?.uptime ?? '3d 14h 22m'}
            </div>
            <div className="mt-2 text-xs text-slate-500 dark:text-slate-400">
              Node Kestrel Port :{telemetry?.webPort ?? '5000'}
            </div>
          </div>
        </div>
      )}

      {/* Kartu Status Simulasi Game (SANGAT RELEVAN UNTUK CLIENT & ADMIN) */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-6 shadow-sm transition-colors">
        <div className="flex items-center justify-between pb-4 border-b border-slate-200 dark:border-slate-800">
          <div>
            <h3 className="font-semibold text-slate-900 dark:text-slate-100">Status Dunia Pertanian</h3>
            <p className="text-xs text-slate-500 dark:text-slate-400">Kondisi waktu, iklim, dan ekonomi in-game saat ini</p>
          </div>
          <span className="text-xs font-mono bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-300 px-2.5 py-1 rounded-md border border-slate-200 dark:border-slate-700">
            Farm: {telemetry?.farm?.name || 'Pelican Haven'} ({telemetry?.farm?.farmType || 'Standard'})
          </span>
        </div>

        <div className="grid grid-cols-2 md:grid-cols-4 gap-6 pt-5">
          {/* In-game Date */}
          <div className="flex items-start space-x-3">
            <div className="p-2.5 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 rounded-lg border border-emerald-500/20">
              <Calendar className="w-5 h-5" />
            </div>
            <div>
              <div className="text-xs text-slate-500 dark:text-slate-400 font-medium">Kalender Game</div>
              <div className="text-base font-bold text-slate-900 dark:text-slate-100 mt-0.5">
                {telemetry?.gameTime?.season || 'Spring'} {telemetry?.gameTime?.day || '18'}
              </div>
              <div className="text-xs text-slate-500 dark:text-slate-400">Tahun ke-{telemetry?.gameTime?.year || '2'}</div>
            </div>
          </div>

          {/* Clock Time */}
          <div className="flex items-start space-x-3">
            <div className="p-2.5 bg-amber-500/10 text-amber-600 dark:text-amber-400 rounded-lg border border-amber-500/20">
              <Clock className="w-5 h-5" />
            </div>
            <div>
              <div className="text-xs text-slate-500 dark:text-slate-400 font-medium">Waktu In-Game</div>
              <div className="text-base font-bold text-slate-900 dark:text-slate-100 mt-0.5">
                {telemetry?.gameTime?.timeOfDay || '02:40 PM'}
              </div>
              <div className="text-xs text-slate-500 dark:text-slate-400">Siklus 10 Menit Normal</div>
            </div>
          </div>

          {/* Weather */}
          <div className="flex items-start space-x-3">
            <div className="p-2.5 bg-blue-500/10 text-blue-600 dark:text-blue-400 rounded-lg border border-blue-500/20">
              {telemetry?.gameTime?.weather === 'Rainy' ? (
                <CloudRain className="w-5 h-5" />
              ) : (
                <Sun className="w-5 h-5" />
              )}
            </div>
            <div>
              <div className="text-xs text-slate-500 dark:text-slate-400 font-medium">Cuaca Hari Ini</div>
              <div className="text-base font-bold text-slate-900 dark:text-slate-100 mt-0.5">
                {telemetry?.gameTime?.weather || 'Sunny'}
              </div>
              <div className="text-xs text-slate-500 dark:text-slate-400">
                Besok: {telemetry?.gameTime?.weatherTomorrow || 'Sunny'}
              </div>
            </div>
          </div>

          {/* Shared Treasury */}
          <div className="flex items-start space-x-3">
            <div className="p-2.5 bg-yellow-500/10 text-yellow-600 dark:text-yellow-400 rounded-lg border border-yellow-500/20">
              <Coins className="w-5 h-5" />
            </div>
            <div>
              <div className="text-xs text-slate-500 dark:text-slate-400 font-medium">Kas Bersama</div>
              <div className="text-base font-bold text-yellow-600 dark:text-yellow-400 mt-0.5">
                {(telemetry?.farm?.sharedMoney ?? 142500).toLocaleString('id-ID')} G
              </div>
              <div className="text-xs text-slate-500 dark:text-slate-400">Dompet Kolektif</div>
            </div>
          </div>
        </div>
      </div>

      {/* Tabel Pemain Terhubung */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden shadow-sm transition-colors">
        <div className="p-4 sm:p-5 border-b border-slate-200 dark:border-slate-800 flex items-center justify-between">
          <div>
            <h3 className="font-semibold text-slate-900 dark:text-slate-100">Daftar Pemain Terhubung</h3>
            <p className="text-xs text-slate-500 dark:text-slate-400">Pemain yang saat ini aktif di dalam dunia pertanian</p>
          </div>
          <span className="text-xs font-semibold px-2.5 py-1 rounded-full bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-300 border border-slate-200 dark:border-slate-700">
            {players.length} Pemain Online
          </span>
        </div>

        {players.length === 0 ? (
          <div className="p-8 text-center text-slate-500 dark:text-slate-400 text-sm">
            Tidak ada pemain yang sedang terhubung saat ini.
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 dark:bg-slate-950/60 text-xs uppercase font-medium text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800">
                <tr>
                  <th className="py-3 px-4">Nama Pemain</th>
                  <th className="py-3 px-4">Lokasi In-Game</th>
                  <th className="py-3 px-4">Profesi / Keahlian</th>
                  <th className="py-3 px-4">Latensi</th>
                  {isAdmin && <th className="py-3 px-4 text-right">Aksi</th>}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-200 dark:divide-slate-800/60 font-sans">
                {players.map((p) => (
                  <tr key={p.id} className="hover:bg-slate-50 dark:hover:bg-slate-800/30 transition-colors">
                    <td className="py-3 px-4">
                      <div className="flex items-center space-x-2">
                        <span className="w-2 h-2 rounded-full bg-emerald-500 dark:bg-emerald-400"></span>
                        <span className="font-medium text-slate-900 dark:text-slate-200">{p.name}</span>
                        {p.isHost && (
                          <span className="text-[10px] bg-emerald-500/10 text-emerald-700 dark:text-emerald-400 border border-emerald-500/20 px-1.5 py-0.5 rounded font-bold uppercase">
                            Host
                          </span>
                        )}
                      </div>
                    </td>
                    <td className="py-3 px-4 text-slate-700 dark:text-slate-300">
                      {p.location || 'Farmhouse'}
                    </td>
                    <td className="py-3 px-4 text-slate-500 dark:text-slate-400">
                      {p.characterProfession || 'Farming Lvl 10'}
                    </td>
                    <td className="py-3 px-4">
                      <span className="text-xs font-mono text-emerald-600 dark:text-emerald-400">
                        {p.pingMs ?? 16} ms
                      </span>
                    </td>
                    {isAdmin && (
                      <td className="py-3 px-4 text-right">
                        <button
                          onClick={() => handleAction(`Kick ${p.name}`, `/api/admin/players/${p.id}/purge`)}
                          className="text-xs text-rose-600 dark:text-rose-400 hover:text-rose-700 dark:hover:text-rose-300 font-medium px-2 py-1 rounded bg-rose-500/10 hover:bg-rose-500/20 border border-rose-500/20 transition-colors"
                        >
                          Kick
                        </button>
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
