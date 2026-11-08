import React, { useState, useEffect, useRef } from 'react';
import {
  ServerTelemetry,
  AuthUser,
  WorldSummary,
  TreasuryData,
  CommunityProject,
  SaveInspectMeta,
} from '../types';
import {
  executeAdminAction,
  downloadSaveBackupZip,
  uploadSavegameZip,
  fetchSaveInspection,
} from '../lib/api';
import {
  Sprout,
  Download,
  Upload,
  HardDrive,
  Coins,
  PawPrint,
  CheckCircle2,
  AlertCircle,
  FileArchive,
  Trees,
  Cog,
  Hammer,
  PlusCircle,
  RotateCcw,
} from 'lucide-react';

interface FarmTabProps {
  telemetry: ServerTelemetry | null;
  worldSummary: WorldSummary | null;
  treasury: TreasuryData | null;
  projects: CommunityProject[];
  user: AuthUser | null;
  onRefresh: () => void;
}

const FARM_TYPES = [
  { id: 0, name: 'Standard Farm (Lahan Klasik Luas)' },
  { id: 1, name: 'Riverland Farm (Kepulauan & Perikanan)' },
  { id: 2, name: 'Forest Farm (Hutan Kayu Keras & Foraging)' },
  { id: 3, name: 'Hill-top Farm (Perbukitan Tambang Mineral)' },
  { id: 4, name: 'Wilderness Farm (Lahan Liar & Monster Malam)' },
  { id: 5, name: 'Four Corners Farm (4 Kuadran Multipemain)' },
  { id: 6, name: 'Beach Farm (Pesisir Samudra & Peti Harta)' },
  { id: 7, name: 'Meadowlands Farm (Padang Rumput Biru Peternakan 1.6)' },
];

export const FarmTab: React.FC<FarmTabProps> = ({
  telemetry,
  worldSummary,
  treasury,
  projects,
  user,
  onRefresh,
}) => {
  const [downloading, setDownloading] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [saveInspect, setSaveInspect] = useState<SaveInspectMeta | null>(null);
  const [feedback, setFeedback] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  // State Buat Dunia Baru / Reset Dunia
  const [worldFormMode, setWorldFormMode] = useState<'none' | 'create' | 'reset'>('none');
  const [newFarmName, setNewFarmName] = useState('');
  const [confirmCurrentFarmName, setConfirmCurrentFarmName] = useState('');
  const [newFarmType, setNewFarmType] = useState(0);
  const [newStartingCabins, setNewStartingCabins] = useState(4);
  const [submittingWorld, setSubmittingWorld] = useState(false);

  const fileInputRef = useRef<HTMLInputElement>(null);
  const isAdmin = user?.role === 'admin';

  const showMessage = (type: 'success' | 'error', text: string) => {
    setFeedback({ type, text });
    setTimeout(() => setFeedback(null), 5000);
  };

  const loadSaveInspection = async () => {
    if (!isAdmin) return;
    try {
      const meta = await fetchSaveInspection();
      if (meta) setSaveInspect(meta);
    } catch {}
  };

  useEffect(() => {
    loadSaveInspection();
  }, [isAdmin]);

  const handleDownloadBackup = async () => {
    if (!isAdmin) return;
    setDownloading(true);
    setFeedback(null);

    try {
      await executeAdminAction('/api/admin/save');
      const dl = await downloadSaveBackupZip(telemetry?.farmName || 'LiteValley');
      if (dl.ok) {
        showMessage('success', dl.message);
      } else {
        showMessage('error', dl.message);
      }
    } catch {
      showMessage('error', 'Terjadi kesalahan jaringan saat mengunduh cadangan savegame.');
    } finally {
      setDownloading(false);
    }
  };

  const handleFileUpload = async (file?: File | null) => {
    if (!file || !isAdmin) return;
    if (!file.name.toLowerCase().endsWith('.zip')) {
      showMessage('error', 'Mohon pilih berkas arsip savegame berformat .zip.');
      return;
    }

    setUploading(true);
    setFeedback(null);
    try {
      const res = await uploadSavegameZip(file);
      if (res.ok) {
        showMessage('success', res.message);
        await loadSaveInspection();
        onRefresh();
      } else {
        showMessage('error', res.message);
      }
    } catch {
      showMessage('error', 'Gagal mengunggah berkas savegame.');
    } finally {
      setUploading(false);
      if (fileInputRef.current) fileInputRef.current.value = '';
    }
  };

  const handleWorldSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!isAdmin) return;

    setSubmittingWorld(true);
    setFeedback(null);

    try {
      if (worldFormMode === 'create') {
        const res = await executeAdminAction('/api/admin/world/create', {
          farmName: newFarmName.trim(),
          farmType: newFarmType,
          startingCabins: newStartingCabins,
        });
        if (res.ok) {
          showMessage('success', res.message || `Dunia baru '${newFarmName}' berhasil dibuat!`);
          setWorldFormMode('none');
          setNewFarmName('');
          await loadSaveInspection();
          onRefresh();
        } else {
          showMessage('error', res.message || 'Gagal membuat dunia baru.');
        }
      } else if (worldFormMode === 'reset') {
        const res = await executeAdminAction('/api/admin/world/reset', {
          confirmFarmName: confirmCurrentFarmName.trim(),
          newFarmName: newFarmName.trim() || telemetry?.farmName || 'Farm',
          newFarmType,
          newStartingCabins,
        });
        if (res.ok) {
          showMessage('success', res.message || 'Dunia berhasil diarsipkan dan di-reset!');
          setWorldFormMode('none');
          setConfirmCurrentFarmName('');
          setNewFarmName('');
          await loadSaveInspection();
          onRefresh();
        } else {
          showMessage('error', res.message || 'Gagal me-reset dunia.');
        }
      }
    } finally {
      setSubmittingWorld(false);
    }
  };

  return (
    <div className="space-y-6">
      {/* Feedback Alert */}
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

      {/* Farm Overview & Live Entity Counts */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-6 shadow-sm transition-colors">
        <div className="flex flex-wrap items-center justify-between pb-5 border-b border-slate-200 dark:border-slate-800 gap-4">
          <div className="flex items-center space-x-3">
            <div className="p-3 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 rounded-xl border border-emerald-500/20">
              <Sprout className="w-6 h-6" />
            </div>
            <div>
              <h2 className="text-lg font-bold text-slate-900 dark:text-slate-100">
                Kebun {telemetry?.farmName || saveInspect?.farm_name || 'LiteValley'}
              </h2>
              <p className="text-xs text-slate-500 dark:text-slate-400">
                Tipe Peta:{' '}
                <span className="text-slate-800 dark:text-slate-200 font-medium">
                  {saveInspect?.farm_type_name || 'Standard Farm'}
                </span>{' '}
                • Silo Terbangun: <span className="font-semibold">{telemetry?.siloCount ?? 0}</span>
              </p>
            </div>
          </div>

          {/* Tombol Aksi Dunia - Khusus Admin */}
          {isAdmin && (
            <div className="flex flex-wrap items-center gap-2">
              <button
                type="button"
                onClick={() => setWorldFormMode(worldFormMode === 'create' ? 'none' : 'create')}
                className="flex items-center space-x-1.5 px-3 py-2 rounded-lg bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 text-slate-800 dark:text-slate-200 border border-slate-200 dark:border-slate-700 text-xs font-semibold transition-colors"
              >
                <PlusCircle className="w-3.5 h-3.5 text-emerald-500" />
                <span>Buat Dunia Baru</span>
              </button>

              <button
                type="button"
                onClick={() => setWorldFormMode(worldFormMode === 'reset' ? 'none' : 'reset')}
                className="flex items-center space-x-1.5 px-3 py-2 rounded-lg bg-amber-500/10 hover:bg-amber-500/20 text-amber-700 dark:text-amber-300 border border-amber-500/20 text-xs font-semibold transition-colors"
              >
                <RotateCcw className="w-3.5 h-3.5" />
                <span>Arsipkan &amp; Reset Dunia</span>
              </button>

              <button
                type="button"
                onClick={handleDownloadBackup}
                disabled={downloading}
                className="flex items-center space-x-1.5 px-3.5 py-2 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-semibold shadow transition-colors"
              >
                <Download className="w-4 h-4" />
                <span>{downloading ? 'Menyiapkan ZIP...' : 'Unduh Cadangan (ZIP)'}</span>
              </button>
            </div>
          )}
        </div>

        {/* Form Buat Dunia Baru / Reset Dunia (Jika Dibuka oleh Admin) */}
        {isAdmin && worldFormMode !== 'none' && (
          <form
            onSubmit={handleWorldSubmit}
            className="mt-5 p-4 rounded-xl bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800 space-y-4"
          >
            <div className="flex items-center justify-between">
              <h4 className="text-xs font-bold uppercase tracking-wider text-slate-800 dark:text-slate-200">
                {worldFormMode === 'create'
                  ? 'Generator Dunia Pertanian Baru'
                  : `Arsipkan & Reset Dunia "${telemetry?.farmName || 'Farm'}"`}
              </h4>
              <button
                type="button"
                onClick={() => setWorldFormMode('none')}
                className="text-xs text-slate-500 hover:text-slate-800 dark:hover:text-white"
              >
                Tutup
              </button>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
              {worldFormMode === 'reset' && (
                <div>
                  <label className="block text-[11px] font-medium text-rose-600 dark:text-rose-400 mb-1">
                    Ketik Nama Kebun Saat Ini ({telemetry?.farmName || 'Farm'})
                  </label>
                  <input
                    type="text"
                    value={confirmCurrentFarmName}
                    onChange={(e) => setConfirmCurrentFarmName(e.target.value)}
                    placeholder={telemetry?.farmName || 'Farm'}
                    className="w-full bg-white dark:bg-slate-900 border border-rose-300 dark:border-rose-800 rounded-lg px-3 py-1.5 text-xs"
                    required
                  />
                </div>
              )}

              <div>
                <label className="block text-[11px] font-medium text-slate-600 dark:text-slate-400 mb-1">
                  Nama Kebun Baru
                </label>
                <input
                  type="text"
                  value={newFarmName}
                  onChange={(e) => setNewFarmName(e.target.value)}
                  placeholder="Contoh: LembahMakmur"
                  className="w-full bg-white dark:bg-slate-900 border border-slate-300 dark:border-slate-700 rounded-lg px-3 py-1.5 text-xs"
                  required={worldFormMode === 'create'}
                />
              </div>

              <div>
                <label className="block text-[11px] font-medium text-slate-600 dark:text-slate-400 mb-1">
                  Tipe Peta Pertanian
                </label>
                <select
                  value={newFarmType}
                  onChange={(e) => setNewFarmType(Number(e.target.value))}
                  className="w-full bg-white dark:bg-slate-900 border border-slate-300 dark:border-slate-700 rounded-lg px-2.5 py-1.5 text-xs"
                >
                  {FARM_TYPES.map((ft) => (
                    <option key={ft.id} value={ft.id}>
                      {ft.name}
                    </option>
                  ))}
                </select>
              </div>

              <div>
                <label className="block text-[11px] font-medium text-slate-600 dark:text-slate-400 mb-1">
                  Jumlah Kabin Awal (1 - 16)
                </label>
                <input
                  type="number"
                  min={1}
                  max={16}
                  value={newStartingCabins}
                  onChange={(e) => setNewStartingCabins(Number(e.target.value))}
                  className="w-full bg-white dark:bg-slate-900 border border-slate-300 dark:border-slate-700 rounded-lg px-3 py-1.5 text-xs"
                />
              </div>
            </div>

            <div className="flex justify-end">
              <button
                type="submit"
                disabled={submittingWorld}
                className="px-4 py-2 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-semibold shadow"
              >
                {submittingWorld
                  ? 'Memproses...'
                  : worldFormMode === 'create'
                  ? 'Buat & Aktifkan Dunia Baru'
                  : 'Arsipkan Dunia Lama & Reset'}
              </button>
            </div>
          </form>
        )}

        {/* Statistik Entitas Lahan Riil (WorldEntityScanner) */}
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 pt-6">
          <div className="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800/80 rounded-xl p-4 transition-colors">
            <div className="flex items-center space-x-2 text-emerald-600 dark:text-emerald-400 mb-1.5">
              <Sprout className="w-4 h-4" />
              <span className="text-xs font-medium text-slate-500 dark:text-slate-400">Tanaman Siap Panen</span>
            </div>
            <div className="text-2xl font-bold text-slate-900 dark:text-slate-100">
              {worldSummary?.readyToHarvestCrops ?? 0}{' '}
              <span className="text-xs font-normal text-slate-500">
                / {worldSummary?.totalCrops ?? 0} tanaman
              </span>
            </div>
            <div className="text-[11px] text-slate-500 mt-1">
              Disiram hari ini: {worldSummary?.wateredCropsToday ?? 0} petak
            </div>
          </div>

          <div className="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800/80 rounded-xl p-4 transition-colors">
            <div className="flex items-center space-x-2 text-amber-600 dark:text-amber-400 mb-1.5">
              <PawPrint className="w-4 h-4" />
              <span className="text-xs font-medium text-slate-500 dark:text-slate-400">Hewan Ternak</span>
            </div>
            <div className="text-2xl font-bold text-slate-900 dark:text-slate-100">
              {worldSummary?.totalAnimals ?? 0}{' '}
              <span className="text-xs font-normal text-slate-500">ekor</span>
            </div>
            <div className="text-[11px] text-slate-500 mt-1">
              Silo komunal aktif: {telemetry?.siloCount ?? 0} unit
            </div>
          </div>

          <div className="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800/80 rounded-xl p-4 transition-colors">
            <div className="flex items-center space-x-2 text-blue-600 dark:text-blue-400 mb-1.5">
              <Cog className="w-4 h-4" />
              <span className="text-xs font-medium text-slate-500 dark:text-slate-400">Mesin &amp; Peti Simpan</span>
            </div>
            <div className="text-2xl font-bold text-slate-900 dark:text-slate-100">
              {worldSummary?.totalMachines ?? 0}{' '}
              <span className="text-xs font-normal text-slate-500">
                mesin • {worldSummary?.totalChests ?? 0} peti
              </span>
            </div>
            <div className="text-[11px] text-slate-500 mt-1">
              Pohon tumbuh: {worldSummary?.totalTrees ?? 0} pohon
            </div>
          </div>

          <div className="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800/80 rounded-xl p-4 transition-colors">
            <div className="flex items-center space-x-2 text-yellow-600 dark:text-yellow-400 mb-1.5">
              <Coins className="w-4 h-4" />
              <span className="text-xs font-medium text-slate-500 dark:text-slate-400">Brankas Kas Desa</span>
            </div>
            <div className="text-2xl font-bold text-yellow-600 dark:text-yellow-400">
              {(treasury?.balance ?? 0).toLocaleString('id-ID')} G
            </div>
            <div className="text-[11px] text-slate-500 mt-1">
              Dompet Host: {(saveInspect?.host?.money ?? 0).toLocaleString('id-ID')} G
            </div>
          </div>
        </div>
      </div>

      {/* Proyek Komunitas & Restorasi Desa (GET /api/projects) */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-6 shadow-sm transition-colors">
        <div className="flex items-center justify-between pb-4 border-b border-slate-200 dark:border-slate-800">
          <div className="flex items-center space-x-2.5">
            <Hammer className="w-4 h-4 text-emerald-600 dark:text-emerald-400" />
            <div>
              <h3 className="font-semibold text-slate-900 dark:text-slate-100 text-sm">
                Status Infrastruktur &amp; Restorasi Desa
              </h3>
              <p className="text-xs text-slate-500 dark:text-slate-400">
                Kemajuan pembangunan fasilitas publik berdasarkan status dunia game saat ini
              </p>
            </div>
          </div>
          <span className="text-xs font-semibold text-emerald-600 dark:text-emerald-400">
            {projects.filter((p) => p.isCompleted).length} / {projects.length} Selesai
          </span>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-4 pt-4">
          {projects.map((proj) => (
            <div
              key={proj.id}
              className="p-4 rounded-xl bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800 flex flex-col justify-between"
            >
              <div>
                <div className="flex items-center justify-between gap-2">
                  <span className="font-semibold text-xs text-slate-900 dark:text-slate-100">
                    {proj.name}
                  </span>
                  <span
                    className={`text-[10px] font-bold px-2 py-0.5 rounded-full border ${
                      proj.isCompleted
                        ? 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border-emerald-500/30'
                        : 'bg-slate-200/70 dark:bg-slate-800 text-slate-600 dark:text-slate-400 border-slate-300 dark:border-slate-700'
                    }`}
                  >
                    {proj.isCompleted ? 'Aktif / Selesai' : 'Belum Selesai'}
                  </span>
                </div>
                <p className="text-[11px] text-slate-500 dark:text-slate-400 mt-1">
                  {proj.description}
                </p>
              </div>
              <div className="mt-3 pt-2 border-t border-slate-200/70 dark:border-slate-800/80 text-[11px] font-medium text-slate-700 dark:text-slate-300">
                {proj.statusText}
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Save Files Details & Rust Save Importer */}
      <div className={`grid grid-cols-1 ${isAdmin ? 'lg:grid-cols-2' : ''} gap-6`}>
        {/* Active Savefile Details */}
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 shadow-sm transition-colors">
          <h3 className="font-semibold text-slate-900 dark:text-slate-100 text-sm flex items-center space-x-2 mb-4">
            <HardDrive className="w-4 h-4 text-emerald-600 dark:text-emerald-400" />
            <span>Inspeksi Savefile Aktif (Rust Save Engine)</span>
          </h3>

          <div className="space-y-3 text-xs">
            <div className="flex justify-between py-2 border-b border-slate-200 dark:border-slate-800">
              <span className="text-slate-500 dark:text-slate-400">Folder Save Unik:</span>
              <span className="font-mono text-slate-800 dark:text-slate-200 font-medium">
                {saveInspect?.unique_id || telemetry?.farmName || 'Save Aktif'}
              </span>
            </div>
            <div className="flex justify-between py-2 border-b border-slate-200 dark:border-slate-800">
              <span className="text-slate-500 dark:text-slate-400">Kalender Save Terakhir:</span>
              <span className="text-slate-800 dark:text-slate-200 capitalize">
                {saveInspect?.in_game_date?.display ||
                  `Hari ${telemetry?.dayOfMonth ?? 1} ${telemetry?.season || 'Spring'}, Tahun ${telemetry?.year ?? 1}`}
              </span>
            </div>
            <div className="flex justify-between py-2 border-b border-slate-200 dark:border-slate-800">
              <span className="text-slate-500 dark:text-slate-400">Kabin Co-op Tersedia di XML:</span>
              <span className="text-slate-800 dark:text-slate-200 font-semibold">
                {saveInspect?.cabins_count ?? telemetry?.maxPlayers ?? 4} Kabin
              </span>
            </div>
            <div className="flex justify-between py-2">
              <span className="text-slate-500 dark:text-slate-400">Status Mesin Save:</span>
              <span className="text-emerald-600 dark:text-emerald-400 font-medium flex items-center space-x-1">
                <CheckCircle2 className="w-3.5 h-3.5" />
                <span>
                  {saveInspect?.source_type === 'directory_csharp_fallback'
                    ? 'Terverifikasi (.NET XML)'
                    : 'Terverifikasi (Rust Native SAX)'}
                </span>
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
                  <span>Unggah / Pulihkan Savefile (.zip)</span>
                </h3>
              </div>
              <p className="text-xs text-slate-500 dark:text-slate-400 mb-4">
                Impor arsip <code className="font-mono">.zip</code> hasil ekspor mod <strong>StardewBridge</strong> atau backup server. Dilengkapi proteksi <em>Anti-ZipSlip</em> &amp; injeksi kabin otomatis untuk savegame solo.
              </p>
            </div>

            <input
              ref={fileInputRef}
              type="file"
              accept=".zip,application/zip"
              className="hidden"
              onChange={(e) => handleFileUpload(e.target.files?.[0])}
            />

            <div
              onClick={() => !uploading && fileInputRef.current?.click()}
              onDragOver={(e) => e.preventDefault()}
              onDrop={(e) => {
                e.preventDefault();
                if (!uploading && e.dataTransfer.files?.[0]) {
                  handleFileUpload(e.dataTransfer.files[0]);
                }
              }}
              className={`border-2 border-dashed rounded-xl p-6 text-center transition-colors cursor-pointer ${
                uploading
                  ? 'border-blue-500/50 bg-blue-500/5 cursor-wait'
                  : 'border-slate-300 dark:border-slate-700/80 hover:border-emerald-500 dark:hover:border-emerald-500 bg-slate-50/60 dark:bg-slate-950/40'
              }`}
            >
              <FileArchive className="w-8 h-8 text-emerald-500 mx-auto mb-2" />
              <div className="text-xs font-semibold text-slate-800 dark:text-slate-200">
                {uploading
                  ? 'Mengimpor & Memvalidasi Savegame via Rust Engine...'
                  : 'Klik untuk memilih berkas save (.zip) atau tarik ke sini'}
              </div>
              <div className="text-[11px] text-slate-500 mt-1">
                Mendukung savegame Single-Player (otomatis disuntikkan kabin Co-op) maupun Multiplayer.
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
