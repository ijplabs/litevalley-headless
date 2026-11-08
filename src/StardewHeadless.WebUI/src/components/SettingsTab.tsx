import React, { useState, useEffect, useRef } from 'react';
import {
  ServerTelemetry,
  AuthUser,
  AdminModsResponse,
  ModPolicyConfig,
} from '../types';
import {
  fetchAdminMods,
  fetchAdminModPolicy,
  updateAdminModPolicy,
  uploadAdminModZip,
  executeAdminAction,
} from '../lib/api';
import {
  ShieldCheck,
  Globe,
  Heart,
  Puzzle,
  Upload,
  Trash2,
  Power,
  CheckCircle2,
  AlertCircle,
  FolderOpen,
} from 'lucide-react';

interface SettingsTabProps {
  telemetry: ServerTelemetry | null;
  user: AuthUser | null;
}

export const SettingsTab: React.FC<SettingsTabProps> = ({ user }) => {
  const [modsData, setModsData] = useState<AdminModsResponse | null>(null);
  const [policy, setPolicy] = useState<ModPolicyConfig | null>(null);
  const [uploadingMod, setUploadingMod] = useState(false);
  const [feedback, setFeedback] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  const modFileInputRef = useRef<HTMLInputElement>(null);

  const isAdmin = user?.role === 'admin';

  const notify = (type: 'success' | 'error', text: string) => {
    setFeedback({ type, text });
    setTimeout(() => setFeedback(null), 4500);
  };

  const loadModsAndPolicy = async () => {
    if (!isAdmin) return;
    try {
      const [m, p] = await Promise.all([
        fetchAdminMods().catch(() => null),
        fetchAdminModPolicy().catch(() => null),
      ]);
      if (m) setModsData(m);
      if (p) setPolicy(p);
    } catch {}
  };

  useEffect(() => {
    loadModsAndPolicy();
  }, [isAdmin]);

  const handleUploadMod = async (file?: File | null) => {
    if (!file) return;
    if (!file.name.toLowerCase().endsWith('.zip')) {
      notify('error', 'Harap pilih paket mod SMAPI berformat .zip.');
      return;
    }

    setUploadingMod(true);
    try {
      const res = await uploadAdminModZip(file);
      if (res.ok) {
        notify('success', res.message);
        await loadModsAndPolicy();
      } else {
        notify('error', res.message);
      }
    } finally {
      setUploadingMod(false);
      if (modFileInputRef.current) modFileInputRef.current.value = '';
    }
  };

  const handleToggleMod = async (uniqueId: string, enable: boolean) => {
    const res = await executeAdminAction(
      `/api/admin/mods/${encodeURIComponent(uniqueId)}/toggle`,
      { enable }
    );
    if (res.ok) {
      notify('success', res.message || `Status mod ${uniqueId} diperbarui.`);
      await loadModsAndPolicy();
    } else {
      notify('error', res.message || 'Gagal mengubah status mod.');
    }
  };

  const handleDeleteMod = async (uniqueId: string) => {
    const res = await executeAdminAction(
      `/api/admin/mods/${encodeURIComponent(uniqueId)}`,
      undefined,
      'DELETE'
    );
    if (res.ok) {
      notify('success', res.message || `Mod ${uniqueId} dihapus.`);
      await loadModsAndPolicy();
    } else {
      notify('error', res.message || 'Gagal menghapus mod.');
    }
  };

  const handleTogglePolicy = async (allowAllMods: boolean) => {
    const res = await updateAdminModPolicy(allowAllMods);
    if (res.ok) {
      notify('success', res.message);
      await loadModsAndPolicy();
    } else {
      notify('error', res.message);
    }
  };

  return (
    <div className="space-y-6">
      {feedback && (
        <div
          className={`p-4 rounded-xl flex items-center space-x-3 text-sm font-medium ${
            feedback.type === 'success'
              ? 'bg-emerald-500/10 border border-emerald-500/30 text-emerald-700 dark:text-emerald-300'
              : 'bg-rose-500/10 border border-rose-500/30 text-rose-700 dark:text-rose-300'
          }`}
        >
          {feedback.type === 'success' ? (
            <CheckCircle2 className="w-5 h-5 flex-shrink-0 text-emerald-500 dark:text-emerald-400" />
          ) : (
            <AlertCircle className="w-5 h-5 flex-shrink-0 text-rose-500 dark:text-rose-400" />
          )}
          <span>{feedback.text}</span>
        </div>
      )}

      {/* Manajer Mod SMAPI Server & Kebijakan Whitelist */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-6 shadow-sm transition-colors space-y-5">
        <div className="flex flex-wrap items-center justify-between gap-4 pb-4 border-b border-slate-200 dark:border-slate-800">
          <div className="flex items-center space-x-3">
            <div className="p-2.5 bg-purple-500/10 text-purple-600 dark:text-purple-400 rounded-xl border border-purple-500/20">
              <Puzzle className="w-5 h-5" />
            </div>
            <div>
              <h3 className="font-bold text-slate-900 dark:text-slate-100 text-sm">
                Manajer Mod SMAPI &amp; Kebijakan Keamanan Mod Client
              </h3>
              <p className="text-xs text-slate-500 dark:text-slate-400">
                Direktori: <code className="font-mono">{modsData?.modsDirectory || '/data/mods'}</code> •{' '}
                {modsData?.activeCount ?? 0} Aktif / {modsData?.total ?? 0} Terpasang
              </p>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <input
              ref={modFileInputRef}
              type="file"
              accept=".zip,application/zip"
              className="hidden"
              onChange={(e) => handleUploadMod(e.target.files?.[0])}
            />
            <button
              type="button"
              onClick={() => !uploadingMod && modFileInputRef.current?.click()}
              disabled={uploadingMod}
              className="flex items-center space-x-1.5 px-3.5 py-2 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-semibold shadow transition-colors"
            >
              <Upload className="w-4 h-4" />
              <span>{uploadingMod ? 'Mengunggah Mod...' : 'Unggah Mod SMAPI (.zip)'}</span>
            </button>
          </div>
        </div>

        {/* Mode Kebijakan Mod Client */}
        <div className="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800 rounded-xl p-4 flex flex-wrap items-center justify-between gap-4">
          <div>
            <div className="text-xs font-bold text-slate-900 dark:text-slate-100">
              Mode Verifikasi Mod Client:{' '}
              <span
                className={
                  policy?.allowAllMods
                    ? 'text-amber-600 dark:text-amber-400'
                    : 'text-emerald-600 dark:text-emerald-400'
                }
              >
                {policy?.allowAllMods ? 'Permissive (Izinkan Mod Visual Client)' : 'Strict Whitelist (Ketat)'}
              </span>
            </div>
            <p className="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">
              {policy?.warning ||
                'Hanya mod yang disetujui server yang diizinkan masuk. Mod cheat pada daftar hitam selalu diblokir.'}
            </p>
          </div>

          <button
            type="button"
            onClick={() => handleTogglePolicy(!policy?.allowAllMods)}
            className="px-3 py-1.5 rounded-lg bg-slate-200 dark:bg-slate-800 hover:bg-slate-300 dark:hover:bg-slate-700 text-xs font-semibold text-slate-800 dark:text-slate-200 transition-colors"
          >
            Ganti ke {policy?.allowAllMods ? 'Strict Whitelist' : 'Mode Permissive'}
          </button>
        </div>

        {/* Daftar Mod Terpasang */}
        {!modsData?.mods || modsData.mods.length === 0 ? (
          <div className="py-8 text-center text-xs text-slate-500 dark:text-slate-400 border border-dashed border-slate-200 dark:border-slate-800 rounded-xl">
            Belum ada mod SMAPI tambahan yang dipasang di <code className="font-mono">/data/mods</code>.
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead className="bg-slate-50 dark:bg-slate-950/60 uppercase font-medium text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-800">
                <tr>
                  <th className="py-2.5 px-3">Nama Mod &amp; UniqueID</th>
                  <th className="py-2.5 px-3">Versi</th>
                  <th className="py-2.5 px-3">Kategori</th>
                  <th className="py-2.5 px-3">Status</th>
                  <th className="py-2.5 px-3 text-right">Aksi</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-200 dark:divide-slate-800">
                {modsData.mods.map((m) => (
                  <tr key={m.uniqueId}>
                    <td className="py-2.5 px-3">
                      <div className="font-semibold text-slate-900 dark:text-slate-100">{m.name}</div>
                      <div className="font-mono text-[11px] text-slate-500">{m.uniqueId}</div>
                    </td>
                    <td className="py-2.5 px-3 font-mono text-slate-700 dark:text-slate-300">{m.version}</td>
                    <td className="py-2.5 px-3 text-slate-600 dark:text-slate-400">{m.category}</td>
                    <td className="py-2.5 px-3">
                      <span
                        className={`px-2 py-0.5 rounded text-[10px] font-bold ${
                          m.isEnabled
                            ? 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400'
                            : 'bg-slate-200 dark:bg-slate-800 text-slate-500'
                        }`}
                      >
                        {m.isEnabled ? 'Aktif' : 'Nonaktif'}
                      </span>
                    </td>
                    <td className="py-2.5 px-3 text-right">
                      <div className="flex items-center justify-end space-x-1.5">
                        <button
                          type="button"
                          onClick={() => handleToggleMod(m.uniqueId, !m.isEnabled)}
                          className="p-1.5 rounded bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 text-slate-700 dark:text-slate-300"
                          title={m.isEnabled ? 'Nonaktifkan Mod' : 'Aktifkan Mod'}
                        >
                          <Power className="w-3.5 h-3.5" />
                        </button>
                        <button
                          type="button"
                          onClick={() => handleDeleteMod(m.uniqueId)}
                          className="p-1.5 rounded bg-rose-500/10 hover:bg-rose-500/20 text-rose-600 dark:text-rose-400"
                          title="Hapus Mod"
                        >
                          <Trash2 className="w-3.5 h-3.5" />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* License & Zero-Piracy Compliance Banner */}
      <div className="bg-emerald-50 dark:bg-emerald-950/30 border border-emerald-200 dark:border-emerald-500/30 rounded-xl p-5 transition-colors">
        <div className="flex items-start space-x-3.5">
          <div className="p-2.5 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 rounded-lg border border-emerald-500/20 flex-shrink-0">
            <ShieldCheck className="w-6 h-6" />
          </div>
          <div>
            <div className="flex items-center space-x-2">
              <h3 className="font-bold text-emerald-800 dark:text-emerald-300 text-sm">
                Kepatuhan Lisensi Asli (100% Clean-Room Zero-Piracy)
              </h3>
              <span className="text-[10px] font-bold uppercase px-2 py-0.5 rounded bg-emerald-500/20 text-emerald-800 dark:text-emerald-300">
                BYOG (Bring Your Own Game)
              </span>
            </div>
            <p className="text-xs text-slate-700 dark:text-slate-300 mt-1 leading-relaxed">
              Image Docker <code className="font-mono">ghcr.io/ijplabs/litevalley-headless</code> sama sekali tidak menyertakan biner atau aset berhak cipta Stardew Valley (AppID <code className="font-mono">413150</code>). Server membaca instalasi resmi milik operator yang di-<em>mount</em> secara <em>read-only</em> ke volume <code className="font-mono">/data/game:ro</code>.
            </p>
          </div>
        </div>
      </div>

      {/* Network & Volumes Info */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-6 shadow-sm transition-colors">
        <h3 className="font-bold text-slate-900 dark:text-slate-100 text-sm flex items-center space-x-2 mb-4">
          <Globe className="w-4 h-4 text-emerald-600 dark:text-emerald-400" />
          <span>Arsitektur Port &amp; Volume Kontainer</span>
        </h3>

        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 text-xs">
          <div className="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800 p-4 rounded-lg">
            <div className="text-slate-500 dark:text-slate-400 font-medium">Port Multiplayer Game (UDP)</div>
            <div className="text-xl font-mono font-bold text-slate-900 dark:text-slate-100 mt-1">
              :24642/udp
            </div>
            <p className="text-[11px] text-slate-500 mt-2">
              Protokol netcode Lidgren UDP untuk koneksi klien Stardew Valley 1.6+ &amp; StardewBridge.
            </p>
          </div>

          <div className="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800 p-4 rounded-lg">
            <div className="text-slate-500 dark:text-slate-400 font-medium">Port Web Control Panel (TCP)</div>
            <div className="text-xl font-mono font-bold text-slate-900 dark:text-slate-100 mt-1">
              :8080/tcp
            </div>
            <p className="text-[11px] text-slate-500 mt-2">
              Embedded Kestrel HTTP Server yang menyajikan dasbor mandiri ini beserta 35+ REST API.
            </p>
          </div>

          <div className="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800 p-4 rounded-lg">
            <div className="text-slate-500 dark:text-slate-400 font-medium">Rust 2D Radar Hub (TCP)</div>
            <div className="text-xl font-mono font-bold text-slate-900 dark:text-slate-100 mt-1">
              :4041/tcp
            </div>
            <p className="text-[11px] text-slate-500 mt-2">
              Mikroservis native <code className="font-mono">litevalley-broadcaster</code> untuk streaming SSE &amp; WebSocket peta.
            </p>
          </div>
        </div>

        <div className="mt-4 p-3.5 rounded-lg bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800 flex items-center space-x-3 text-xs">
          <FolderOpen className="w-4 h-4 text-blue-500 flex-shrink-0" />
          <div className="text-slate-600 dark:text-slate-400">
            <strong>Mount Volume Standar:</strong>{' '}
            <code className="font-mono">/data/game:ro</code> (Instalasi Game Resmi),{' '}
            <code className="font-mono">/data/saves</code> (Savegame XML),{' '}
            <code className="font-mono">/data/config</code> (Database SQLite),{' '}
            <code className="font-mono">/data/mods</code> (Mod SMAPI).
          </div>
        </div>
      </div>

      {/* Footer Tribute */}
      <div className="text-center py-4 border-t border-slate-200 dark:border-slate-800/60 text-xs text-slate-500 flex items-center justify-center space-x-1.5">
        <span>LiteValley Headless v0.3.0 • Dibuat dengan</span>
        <Heart className="w-3.5 h-3.5 text-rose-500 fill-rose-500 inline" />
        <span>sebagai penghormatan untuk Eric Barone (ConcernedApe) &amp; Komunitas SMAPI.</span>
      </div>
    </div>
  );
};
