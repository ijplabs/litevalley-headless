import React, { useState, useEffect, useRef } from 'react';
import { AuthUser, LogEntryItem, AntiCheatIncidentItem } from '../types';
import {
  fetchAdminLogs,
  fetchAntiCheatIncidents,
  executeAdminAction,
} from '../lib/api';
import {
  Terminal,
  Send,
  ArrowDown,
  Search,
  Filter,
  RefreshCw,
  ShieldAlert,
} from 'lucide-react';

interface ConsoleTabProps {
  user: AuthUser | null;
}

export const ConsoleTab: React.FC<ConsoleTabProps> = ({ user }) => {
  const [logs, setLogs] = useState<LogEntryItem[]>([]);
  const [categories, setCategories] = useState<string[]>([]);
  const [incidents, setIncidents] = useState<AntiCheatIncidentItem[]>([]);
  const [inputCmd, setInputCmd] = useState('');
  const [filterLevel, setFilterLevel] = useState<string>('ALL');
  const [filterCategory, setFilterCategory] = useState<string>('ALL');
  const [searchQuery, setSearchQuery] = useState('');
  const [autoScroll, setAutoScroll] = useState(true);
  const [loading, setLoading] = useState(false);

  const terminalEndRef = useRef<HTMLDivElement>(null);

  const loadLogsAndIncidents = async () => {
    if (user?.role !== 'admin') return;
    try {
      setLoading(true);
      const [logRes, incRes] = await Promise.all([
        fetchAdminLogs({
          limit: 200,
          level: filterLevel,
          category: filterCategory,
          search: searchQuery,
        }).catch(() => null),
        fetchAntiCheatIncidents(25).catch(() => []),
      ]);
      if (logRes) {
        setLogs(logRes.logs || []);
        if (logRes.categories?.length) setCategories(logRes.categories);
      }
      if (incRes) setIncidents(incRes);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadLogsAndIncidents();
    const timer = setInterval(loadLogsAndIncidents, 3500);
    return () => clearInterval(timer);
  }, [filterLevel, filterCategory, searchQuery, user]);

  useEffect(() => {
    if (autoScroll) {
      terminalEndRef.current?.scrollIntoView({ behavior: 'smooth' });
    }
  }, [logs, autoScroll]);

  const executeConsoleCommand = async (rawCmd: string) => {
    const cmd = rawCmd.trim();
    if (!cmd) return;

    const lower = cmd.toLowerCase();
    if (lower === 'save' || lower === 'force_save') {
      await executeAdminAction('/api/admin/save');
    } else if (lower.startsWith('say ') || lower.startsWith('broadcast ')) {
      const msg = cmd.replace(/^(say|broadcast)\s+/i, '').trim();
      if (msg) {
        await executeAdminAction('/api/admin/broadcast', { message: msg });
      }
    } else {
      await executeAdminAction('/api/admin/broadcast', { message: cmd });
    }

    await loadLogsAndIncidents();
  };

  const handleSendCommand = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!inputCmd.trim()) return;
    const cmd = inputCmd.trim();
    setInputCmd('');
    await executeConsoleCommand(cmd);
  };

  const getLogColor = (level: string) => {
    const u = (level || 'INFO').toUpperCase();
    if (u === 'ERROR' || u === 'FATAL') return 'text-rose-400 font-semibold';
    if (u === 'WARN' || u === 'WARNING') return 'text-amber-400';
    if (u === 'TRACE' || u === 'DEBUG') return 'text-slate-500';
    return 'text-slate-300';
  };

  const formatTime = (iso: string) => {
    try {
      return new Date(iso).toLocaleTimeString('id-ID', { hour12: false });
    } catch {
      return iso;
    }
  };

  return (
    <div className="space-y-6">
      {/* Console Controls & Filters Header */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl p-3 sm:p-4 flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2 flex-grow sm:flex-grow-0">
          <div className="relative">
            <Search className="w-3.5 h-3.5 text-slate-400 absolute left-3 top-2.5" />
            <input
              type="text"
              placeholder="Cari pesan log..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="bg-slate-950 border border-slate-800 text-xs text-slate-200 pl-8 pr-3 py-1.5 rounded-lg focus:outline-none focus:border-slate-700 w-44 sm:w-60"
            />
          </div>

          <div className="flex items-center space-x-1.5 bg-slate-950 border border-slate-800 rounded-lg px-2 py-1">
            <Filter className="w-3 h-3 text-slate-400" />
            <select
              value={filterLevel}
              onChange={(e) => setFilterLevel(e.target.value)}
              className="bg-transparent text-xs text-slate-300 focus:outline-none cursor-pointer"
            >
              <option value="ALL" className="bg-slate-900">Semua Level</option>
              <option value="INFO" className="bg-slate-900">INFO</option>
              <option value="WARN" className="bg-slate-900">WARN</option>
              <option value="ERROR" className="bg-slate-900">ERROR</option>
              <option value="TRACE" className="bg-slate-900">TRACE</option>
            </select>
          </div>

          <div className="flex items-center space-x-1.5 bg-slate-950 border border-slate-800 rounded-lg px-2 py-1">
            <select
              value={filterCategory}
              onChange={(e) => setFilterCategory(e.target.value)}
              className="bg-transparent text-xs text-slate-300 focus:outline-none cursor-pointer"
            >
              <option value="ALL" className="bg-slate-900">Semua Kategori</option>
              {categories.map((cat) => (
                <option key={cat} value={cat} className="bg-slate-900">
                  {cat}
                </option>
              ))}
            </select>
          </div>
        </div>

        <div className="flex items-center space-x-2">
          <button
            onClick={() => setAutoScroll(!autoScroll)}
            className={`flex items-center space-x-1 px-2.5 py-1.5 rounded-lg text-xs font-medium border transition-colors ${
              autoScroll
                ? 'bg-emerald-500/10 border-emerald-500/30 text-emerald-400'
                : 'bg-slate-800 border-slate-700 text-slate-400 hover:text-slate-200'
            }`}
          >
            <ArrowDown className="w-3 h-3" />
            <span>Auto-Scroll</span>
          </button>

          <button
            onClick={loadLogsAndIncidents}
            className="flex items-center space-x-1 px-2.5 py-1.5 rounded-lg text-xs font-medium bg-slate-800 hover:bg-slate-700 border border-slate-700 text-slate-300 hover:text-white transition-colors"
          >
            <RefreshCw className={`w-3 h-3 ${loading ? 'animate-spin' : ''}`} />
            <span>Segarkan</span>
          </button>
        </div>
      </div>

      {/* Terminal Display */}
      <div className="bg-slate-950 border border-slate-800 rounded-xl overflow-hidden shadow-2xl flex flex-col h-[480px]">
        <div className="bg-slate-900/90 px-4 py-2 border-b border-slate-800 flex items-center justify-between">
          <div className="flex items-center space-x-2">
            <Terminal className="w-4 h-4 text-emerald-400" />
            <span className="text-xs font-mono font-medium text-slate-300">
              structured-logger.jsonl ({logs.length} baris ditampilkan)
            </span>
          </div>
          <div className="flex items-center space-x-1.5">
            <span className="w-2.5 h-2.5 rounded-full bg-emerald-500/60"></span>
            <span className="w-2.5 h-2.5 rounded-full bg-amber-500/60"></span>
            <span className="w-2.5 h-2.5 rounded-full bg-rose-500/60"></span>
          </div>
        </div>

        {/* Terminal Log Lines */}
        <div className="p-4 overflow-y-auto flex-1 font-mono text-xs space-y-1">
          {logs.length === 0 ? (
            <div className="text-slate-500 italic py-8 text-center">
              Belum ada catatan log yang sesuai dengan filter saat ini.
            </div>
          ) : (
            logs.map((entry, idx) => (
              <div key={idx} className={`leading-relaxed break-all ${getLogColor(entry.level)}`}>
                <span className="text-slate-500">[{formatTime(entry.timestamp)}]</span>{' '}
                <span className="font-bold">[{entry.level}]</span>{' '}
                <span className="text-cyan-400">[{entry.category}]</span> {entry.message}
                {entry.exception && (
                  <div className="text-rose-400/90 pl-4 mt-0.5 whitespace-pre-wrap text-[11px]">
                    {entry.exception}
                  </div>
                )}
              </div>
            ))
          )}
          <div ref={terminalEndRef} />
        </div>

        {/* Quick Commands Bar */}
        <div className="bg-slate-900/50 px-4 py-2 border-t border-slate-800/80 flex items-center space-x-2 overflow-x-auto">
          <span className="text-[11px] text-slate-400 uppercase font-bold flex-shrink-0">Pintasan:</span>
          <button
            type="button"
            onClick={() => executeConsoleCommand('save')}
            className="text-[11px] font-mono px-2.5 py-0.5 rounded bg-slate-800 hover:bg-slate-700 text-emerald-400 border border-slate-700 whitespace-nowrap"
          >
            save (Simpan Dunia)
          </button>
          <button
            type="button"
            onClick={() => executeConsoleCommand('say Server akan melakukan penyimpanan berkala!')}
            className="text-[11px] font-mono px-2.5 py-0.5 rounded bg-slate-800 hover:bg-slate-700 text-amber-400 border border-slate-700 whitespace-nowrap"
          >
            broadcast: Peringatan Save
          </button>
          <button
            type="button"
            onClick={() => executeConsoleCommand('say Selamat datang di kebun komunal LiteValley!')}
            className="text-[11px] font-mono px-2.5 py-0.5 rounded bg-slate-800 hover:bg-slate-700 text-blue-400 border border-slate-700 whitespace-nowrap"
          >
            broadcast: Sambutan
          </button>
        </div>

        {/* Command Dispatch Form */}
        <form
          onSubmit={handleSendCommand}
          className="border-t border-slate-800 bg-slate-900 p-2.5 flex items-center space-x-2"
        >
          <span className="text-emerald-400 font-mono text-xs pl-2 font-bold">&gt;</span>
          <input
            type="text"
            value={inputCmd}
            onChange={(e) => setInputCmd(e.target.value)}
            placeholder="Ketik 'save' untuk menyimpan dunia atau 'say <pesan>' untuk pengumuman chat in-game..."
            className="flex-1 bg-transparent text-xs font-mono text-slate-100 placeholder-slate-500 focus:outline-none"
          />
          <button
            type="submit"
            className="p-1.5 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white transition-colors"
            title="Eksekusi Perintah"
          >
            <Send className="w-3.5 h-3.5" />
          </button>
        </form>
      </div>

      {/* Audit Insiden Anti-Cheat Terbaru */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-5 shadow-sm transition-colors">
        <div className="flex items-center justify-between pb-3 border-b border-slate-200 dark:border-slate-800">
          <div className="flex items-center space-x-2">
            <ShieldAlert className="w-4 h-4 text-amber-500" />
            <h3 className="font-semibold text-slate-900 dark:text-slate-100 text-sm">
              Audit Insiden Keamanan &amp; Anti-Cheat
            </h3>
          </div>
          <span className="text-xs text-slate-500 dark:text-slate-400">
            {incidents.length} insiden tercatat
          </span>
        </div>

        {incidents.length === 0 ? (
          <div className="py-6 text-center text-xs text-slate-500 dark:text-slate-400">
            Tidak ada pelanggaran anti-cheat yang terdeteksi. Seluruh aktivitas pemain bersih.
          </div>
        ) : (
          <div className="divide-y divide-slate-200 dark:divide-slate-800 pt-2 text-xs">
            {incidents.slice(0, 10).map((inc, i) => (
              <div key={inc.id ?? i} className="py-2.5 flex items-center justify-between gap-4">
                <div>
                  <span className="font-semibold text-slate-900 dark:text-slate-100">
                    {inc.farmerName || `Farmer #${inc.farmerId}`}
                  </span>{' '}
                  <span className="px-1.5 py-0.5 rounded bg-amber-500/10 text-amber-600 dark:text-amber-400 font-mono text-[10px]">
                    {inc.category}
                  </span>
                  <p className="text-slate-600 dark:text-slate-400 mt-0.5">{inc.description}</p>
                </div>
                <span className="text-[11px] font-mono text-rose-500 flex-shrink-0">
                  {inc.actionTaken}
                </span>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
};
