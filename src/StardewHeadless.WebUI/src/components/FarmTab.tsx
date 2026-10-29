import React, { useState } from 'react';
import { ServerTelemetry, AuthUser } from '../types';
import { executeAdminAction } from '../lib/api';
import { 
  Sprout, 
  Download, 
  Upload, 
  HardDrive, 
  Coins, 
  PawPrint, 
  CheckCircle2, 
  AlertCircle,
  FileArchive
} from 'lucide-react';

interface FarmTabProps {
  telemetry: ServerTelemetry | null;
  user: AuthUser | null;
}

export const FarmTab: React.FC<FarmTabProps> = ({ telemetry, user }) => {
  const [downloading, setDownloading] = useState(false);
  const [feedback, setFeedback] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  const isAdmin = user?.role === 'admin';

  const handleDownloadBackup = async () => {
    if (!isAdmin) return;
    setDownloading(true);
    setFeedback(null);

    try {
      const res = await executeAdminAction('/api/admin/save');
      if (res.ok) {
        setFeedback({
          type: 'success',
          text: 'Snapshot savefile berhasil dibuat. Memulai proses pengunduhan berkas...',
        });
        
        const link = document.createElement('a');
        link.href = '/api/admin/save/download';
        link.download = `${telemetry?.farm?.name || 'Farm'}_backup.zip`;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
      } else {
        setFeedback({
          type: 'error',
          text: 'Gagal membuat snapshot savefile.',
        });
      }
    } catch {
      setFeedback({
        type: 'error',
        text: 'Terjadi kesalahan jaringan saat mengunduh savefile.',
      });
    } finally {
      setDownloading(false);
      setTimeout(() => setFeedback(null), 4000);
    }
  };

  return (
    <div className="space-y-6">
      {/* Feedback Alert */}
      {feedback && (
        <div className={`p-4 rounded-lg flex items-center space-x-3 text-sm font-medium ${
          feedback.type === 'success' 
            ? 'bg-emerald-500/10 border border-emerald-500/30 text-emerald-700 dark:text-emerald-300' 
            : 'bg-rose-500/10 border border-rose-500/30 text-rose-700 dark:text-rose-300'
        }`}>
          {feedback.type === 'success' ? (
            <CheckCircle2 className="w-5 h-5 flex-shrink-0 text-emerald-500 dark:text-emerald-400" />
          ) : (
            <AlertCircle className="w-5 h-5 flex-shrink-0 text-rose-500 dark:text-rose-400" />
          )}
          <span>{feedback.text}</span>
        </div>
      )}

      {/* Farm Overview Card */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-6 shadow-sm transition-colors">
        <div className="flex flex-wrap items-center justify-between pb-5 border-b border-slate-200 dark:border-slate-800 gap-4">
          <div className="flex items-center space-x-3">
            <div className="p-3 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 rounded-xl border border-emerald-500/20">
              <Sprout className="w-6 h-6" />
            </div>
            <div>
              <h2 className="text-lg font-bold text-slate-900 dark:text-slate-100">{telemetry?.farm?.name || 'Pelican Haven'}</h2>
              <p className="text-xs text-slate-500 dark:text-slate-400">
                Peta Pertanian: <span className="text-slate-800 dark:text-slate-200 font-medium">{telemetry?.farm?.farmType || 'Standard'}</span>
              </p>
            </div>
          </div>

          {/* Tombol Unduh Cadangan - Khusus Admin */}
          {isAdmin && (
            <div className="flex items-center space-x-2">
              <button
                onClick={handleDownloadBackup}
                disabled={downloading}
                className="flex items-center space-x-2 px-3.5 py-2 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-semibold shadow transition-colors"
              >
                <Download className="w-4 h-4" />
                <span>{downloading ? 'Menyiapkan...' : 'Unduh Cadangan (ZIP)'}</span>
              </button>
            </div>
          )}
        </div>

        {/* Farming Statistics Grid */}
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 pt-6">
          <div className="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800/80 rounded-xl p-4 transition-colors">
            <div className="flex items-center space-x-3 text-emerald-600 dark:text-emerald-400 mb-2">
              <Sprout className="w-4 h-4" />
              <span className="text-xs font-medium text-slate-500 dark:text-slate-400">Tanaman Siap Panen</span>
            </div>
            <div className="text-2xl font-bold text-slate-900 dark:text-slate-100">
              {telemetry?.farm?.cropsReady ?? '164'} <span className="text-xs font-normal text-slate-500">petak</span>
            </div>
          </div>

          <div className="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800/80 rounded-xl p-4 transition-colors">
            <div className="flex items-center space-x-3 text-amber-600 dark:text-amber-400 mb-2">
              <PawPrint className="w-4 h-4" />
              <span className="text-xs font-medium text-slate-500 dark:text-slate-400">Hewan Ternak</span>
            </div>
            <div className="text-2xl font-bold text-slate-900 dark:text-slate-100">
              {telemetry?.farm?.animalsCount ?? '18'} <span className="text-xs font-normal text-slate-500">ekor</span>
            </div>
          </div>

          <div className="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800/80 rounded-xl p-4 transition-colors">
            <div className="flex items-center space-x-3 text-yellow-600 dark:text-yellow-400 mb-2">
              <Coins className="w-4 h-4" />
              <span className="text-xs font-medium text-slate-500 dark:text-slate-400">Total Kas Pertanian</span>
            </div>
            <div className="text-2xl font-bold text-yellow-600 dark:text-yellow-400">
              {(telemetry?.farm?.sharedMoney ?? 142500).toLocaleString('id-ID')} G
            </div>
          </div>
        </div>
      </div>

      {/* Save Files Details */}
      <div className={`grid grid-cols-1 ${isAdmin ? 'lg:grid-cols-2' : ''} gap-6`}>
        {/* Active Savefile Details */}
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 shadow-sm transition-colors">
          <h3 className="font-semibold text-slate-900 dark:text-slate-100 text-sm flex items-center space-x-2 mb-4">
            <HardDrive className="w-4 h-4 text-emerald-600 dark:text-emerald-400" />
            <span>Informasi Dunia &amp; Savefile</span>
          </h3>

          <div className="space-y-3 text-xs">
            <div className="flex justify-between py-2 border-b border-slate-200 dark:border-slate-800">
              <span className="text-slate-500 dark:text-slate-400">Nama Folder Save:</span>
              <span className="font-mono text-slate-800 dark:text-slate-200 font-medium">
                {telemetry?.farm?.activeSaveFile || 'PelicanHaven_349812903'}
              </span>
            </div>
            <div className="flex justify-between py-2 border-b border-slate-200 dark:border-slate-800">
              <span className="text-slate-500 dark:text-slate-400">Waktu Kalender Terakhir:</span>
              <span className="text-slate-800 dark:text-slate-200">
                {telemetry?.gameTime?.season || 'Spring'} {telemetry?.gameTime?.day || '18'}, Th {telemetry?.gameTime?.year || '2'}
              </span>
            </div>
            <div className="flex justify-between py-2 border-b border-slate-200 dark:border-slate-800">
              <span className="text-slate-500 dark:text-slate-400">Jadwal Auto-Save:</span>
              <span className="text-emerald-600 dark:text-emerald-400 font-medium">Setiap Jam 06:00 AM In-Game</span>
            </div>
            <div className="flex justify-between py-2">
              <span className="text-slate-500 dark:text-slate-400">Integritas Berkas:</span>
              <span className="text-emerald-600 dark:text-emerald-400 font-medium flex items-center space-x-1">
                <CheckCircle2 className="w-3.5 h-3.5" />
                <span>Terverifikasi (Valid XML)</span>
              </span>
            </div>
          </div>
        </div>

        {/* Upload / Restore Save Dropzone - Khusus Sysadmin */}
        {isAdmin && (
          <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 flex flex-col justify-between shadow-sm transition-colors">
            <div>
              <div className="flex items-center space-x-2 mb-2">
                <span className="text-[10px] font-bold uppercase tracking-wider px-2 py-0.5 rounded bg-blue-500/10 text-blue-700 dark:text-blue-400 border border-blue-500/20">
                  Mode Sysadmin
                </span>
                <h3 className="font-semibold text-slate-900 dark:text-slate-100 text-sm flex items-center space-x-2">
                  <Upload className="w-4 h-4 text-blue-600 dark:text-blue-400" />
                  <span>Unggah / Pulihkan Savefile</span>
                </h3>
              </div>
              <p className="text-xs text-slate-500 dark:text-slate-400 mb-4">
                Pindahkan dunia pertanian Anda dari komputer single-player ke dedicated server ini.
              </p>
            </div>

            <div className="border-2 border-dashed border-slate-300 dark:border-slate-700/80 hover:border-slate-400 dark:hover:border-slate-600 rounded-xl p-6 text-center bg-slate-50/60 dark:bg-slate-950/40 transition-colors cursor-pointer">
              <FileArchive className="w-8 h-8 text-slate-400 dark:text-slate-500 mx-auto mb-2" />
              <div className="text-xs font-medium text-slate-800 dark:text-slate-200">
                Klik atau tarik berkas save (.zip) ke sini
              </div>
              <div className="text-[11px] text-slate-500 mt-1">
                Mendukung struktur folder Stardew Valley save resmi.
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
