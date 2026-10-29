import React, { useState, useEffect, useRef } from 'react';
import { AuthUser } from '../types';
import { Terminal, Send, Trash2, ArrowDown, Search, Filter } from 'lucide-react';

interface ConsoleTabProps {
  user: AuthUser | null;
}

export const ConsoleTab: React.FC<ConsoleTabProps> = ({ user }) => {
  const [logs, setLogs] = useState<string[]>([
    '[13:40:02 INFO SMAPI] Synchronizing game clock: Spring 18, Year 2, 01:40 PM',
    '[13:40:05 TRACE SMAPI] Farmer \'Pelican Mayor\' entered location \'Cellar\'. Loaded 48 casks.',
    '[13:40:12 TRACE SMAPI] Farmer \'Elliott_Manor\' caught fish \'Eel\' (Quality: Gold, Weight: 32lb).',
    '[13:40:22 INFO GriefGuard] Enforcing chest write filter: 14 player chests checked. 0 violations.',
    '[13:40:30 WARN SMAPI] Multiplayer packet backlog slightly elevated for client \'Abigail\' (54ms). Compensated.',
    '[13:40:48 INFO Automate] Machine cluster in \'Shed #2\' processed 24 Truffle Oil and 18 Ancient Fruit Wine.',
    '[13:41:00 INFO Server] Periodic save snapshot created: \'PelicanHaven_Year2_Spring18.tar.gz\' (Checksum OK).'
  ]);
  const [inputCmd, setInputCmd] = useState('');
  const [filterLevel, setFilterLevel] = useState<'ALL' | 'INFO' | 'WARN' | 'ERROR' | 'TRACE'>('ALL');
  const [searchQuery, setSearchQuery] = useState('');
  const [autoScroll, setAutoScroll] = useState(true);

  const terminalEndRef = useRef<HTMLDivElement>(null);

  // Auto-scroll logic
  useEffect(() => {
    if (autoScroll) {
      terminalEndRef.current?.scrollIntoView({ behavior: 'smooth' });
    }
  }, [logs, autoScroll]);

  const handleSendCommand = (e: React.FormEvent) => {
    e.preventDefault();
    if (!inputCmd.trim()) return;

    const cmd = inputCmd.trim();
    const time = new Date().toLocaleTimeString();

    setLogs((prev) => [
      ...prev,
      `[${time} USER] >> ${cmd}`,
      `[${time} EXEC] Command executed: ${cmd}`
    ]);
    setInputCmd('');
  };

  const handleInject = (cmd: string) => {
    const time = new Date().toLocaleTimeString();
    setLogs((prev) => [
      ...prev,
      `[${time} QUICK] >> ${cmd}`,
      `[${time} EXEC] Command acknowledged: ${cmd}`
    ]);
  };

  const clearLogs = () => setLogs([]);

  const filteredLogs = logs.filter((log) => {
    if (filterLevel !== 'ALL' && !log.includes(` ${filterLevel} `)) {
      return false;
    }
    if (searchQuery && !log.toLowerCase().includes(searchQuery.toLowerCase())) {
      return false;
    }
    return true;
  });

  const getLogColor = (log: string) => {
    if (log.includes(' ERROR ')) return 'text-rose-400 font-semibold';
    if (log.includes(' WARN ')) return 'text-amber-400';
    if (log.includes(' TRACE ')) return 'text-slate-500';
    if (log.includes(' USER ') || log.includes(' QUICK ')) return 'text-cyan-400 font-medium';
    return 'text-slate-300';
  };

  return (
    <div className="space-y-4">
      {/* Console Controls & Filters Header */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl p-3 sm:p-4 flex flex-wrap items-center justify-between gap-3">
        {/* Search & Filter */}
        <div className="flex flex-wrap items-center gap-2 flex-grow sm:flex-grow-0">
          <div className="relative">
            <Search className="w-3.5 h-3.5 text-slate-400 absolute left-3 top-2.5" />
            <input
              type="text"
              placeholder="Cari dalam log..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="bg-slate-950 border border-slate-800 text-xs text-slate-200 pl-8 pr-3 py-1.5 rounded-lg focus:outline-none focus:border-slate-700 w-44 sm:w-60"
            />
          </div>

          <div className="flex items-center space-x-1.5 bg-slate-950 border border-slate-800 rounded-lg px-2 py-1">
            <Filter className="w-3 h-3 text-slate-400" />
            <select
              value={filterLevel}
              onChange={(e) => setFilterLevel(e.target.value as any)}
              className="bg-transparent text-xs text-slate-300 focus:outline-none cursor-pointer"
            >
              <option value="ALL" className="bg-slate-900">Semua Level</option>
              <option value="INFO" className="bg-slate-900">INFO Saja</option>
              <option value="WARN" className="bg-slate-900">WARN Saja</option>
              <option value="ERROR" className="bg-slate-900">ERROR Saja</option>
              <option value="TRACE" className="bg-slate-900">TRACE Saja</option>
            </select>
          </div>
        </div>

        {/* Action Toggles */}
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
            onClick={clearLogs}
            className="flex items-center space-x-1 px-2.5 py-1.5 rounded-lg text-xs font-medium bg-slate-800 hover:bg-slate-700 border border-slate-700 text-slate-300 hover:text-white transition-colors"
          >
            <Trash2 className="w-3 h-3" />
            <span>Bersihkan</span>
          </button>
        </div>
      </div>

      {/* Terminal Display */}
      <div className="bg-slate-950 border border-slate-800 rounded-xl overflow-hidden shadow-2xl flex flex-col h-[520px]">
        {/* Terminal Title Bar */}
        <div className="bg-slate-900/90 px-4 py-2 border-b border-slate-800 flex items-center justify-between">
          <div className="flex items-center space-x-2">
            <Terminal className="w-4 h-4 text-emerald-400" />
            <span className="text-xs font-mono font-medium text-slate-300">server-daemon.stdout</span>
          </div>
          <div className="flex items-center space-x-1.5">
            <span className="w-2.5 h-2.5 rounded-full bg-slate-700"></span>
            <span className="w-2.5 h-2.5 rounded-full bg-slate-700"></span>
            <span className="w-2.5 h-2.5 rounded-full bg-slate-700"></span>
          </div>
        </div>

        {/* Terminal Log Lines */}
        <div className="p-4 overflow-y-auto flex-1 font-mono text-xs space-y-1">
          {filteredLogs.length === 0 ? (
            <div className="text-slate-600 italic py-8 text-center">
              Tidak ada log yang sesuai dengan filter yang dipilih.
            </div>
          ) : (
            filteredLogs.map((log, idx) => (
              <div key={idx} className={`leading-relaxed break-all ${getLogColor(log)}`}>
                {log}
              </div>
            ))
          )}
          <div ref={terminalEndRef} />
        </div>

        {/* Quick Commands Bar */}
        <div className="bg-slate-900/50 px-4 py-2 border-t border-slate-800/80 flex items-center space-x-2 overflow-x-auto">
          <span className="text-[11px] text-slate-400 uppercase font-bold flex-shrink-0">Pintasan:</span>
          <button
            onClick={() => handleInject('save')}
            className="text-[11px] font-mono px-2 py-0.5 rounded bg-slate-800 hover:bg-slate-700 text-emerald-400 border border-slate-700 whitespace-nowrap"
          >
            save
          </button>
          <button
            onClick={() => handleInject('world_info')}
            className="text-[11px] font-mono px-2 py-0.5 rounded bg-slate-800 hover:bg-slate-700 text-slate-300 border border-slate-700 whitespace-nowrap"
          >
            world_info
          </button>
          <button
            onClick={() => handleInject('weather sunny')}
            className="text-[11px] font-mono px-2 py-0.5 rounded bg-slate-800 hover:bg-slate-700 text-amber-400 border border-slate-700 whitespace-nowrap"
          >
            weather sunny
          </button>
          <button
            onClick={() => handleInject('time 0600')}
            className="text-[11px] font-mono px-2 py-0.5 rounded bg-slate-800 hover:bg-slate-700 text-blue-400 border border-slate-700 whitespace-nowrap"
          >
            time 0600
          </button>
        </div>

        {/* Command Dispatch Form */}
        <form onSubmit={handleSendCommand} className="border-t border-slate-800 bg-slate-900 p-2.5 flex items-center space-x-2">
          <span className="text-emerald-400 font-mono text-xs pl-2 font-bold">&gt;</span>
          <input
            type="text"
            value={inputCmd}
            onChange={(e) => setInputCmd(e.target.value)}
            placeholder="Ketik perintah SMAPI / game server di sini..."
            className="flex-1 bg-transparent text-xs font-mono text-slate-100 placeholder-slate-500 focus:outline-none"
          />
          <button
            type="submit"
            className="p-1.5 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white transition-colors"
            title="Kirim Perintah"
          >
            <Send className="w-3.5 h-3.5" />
          </button>
        </form>
      </div>
    </div>
  );
};
