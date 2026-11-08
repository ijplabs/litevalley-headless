import React from 'react';
import { AuthUser, ServerTelemetry } from '../types';
import { Theme } from '../lib/theme';
import { formatUptime } from '../lib/api';
import { Server, Users, Clock, LogOut, LogIn, UserPlus, Sun, Moon } from 'lucide-react';

interface HeaderProps {
  telemetry: ServerTelemetry | null;
  user: AuthUser | null;
  theme: Theme;
  onToggleTheme: () => void;
  onOpenAuth: (mode?: 'login' | 'register') => void;
  onLogout: () => void;
  onlinePlayersCount: number;
  registeredPlayersCount: number;
}

export const Header: React.FC<HeaderProps> = ({
  telemetry,
  user,
  theme,
  onToggleTheme,
  onOpenAuth,
  onLogout,
  onlinePlayersCount,
  registeredPlayersCount,
}) => {
  const isOnline = telemetry !== null;
  const maxPlayers = telemetry?.maxPlayers ?? 4;

  return (
    <header className="border-b border-slate-200 dark:border-slate-800 bg-white/80 dark:bg-slate-900/80 backdrop-blur sticky top-0 z-40 transition-colors">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 h-16 flex items-center justify-between">
        {/* Left: Brand & Status */}
        <div className="flex items-center space-x-4">
          <div className="flex items-center space-x-3">
            <div className="w-9 h-9 rounded-lg bg-emerald-500/10 border border-emerald-500/20 flex items-center justify-center text-emerald-500 dark:text-emerald-400">
              <Server className="w-5 h-5" />
            </div>
            <div>
              <div className="flex items-center space-x-2">
                <span className="font-bold text-slate-900 dark:text-slate-100 text-lg tracking-tight">LiteValley</span>
                <span className="text-xs font-semibold px-2 py-0.5 rounded-full bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-300 border border-slate-200 dark:border-slate-700">
                  v0.3.0
                </span>
              </div>
              <p className="text-xs text-slate-500 dark:text-slate-400 hidden sm:block">
                {telemetry?.serverName || 'LiteValley Headless Dedicated Server'}
              </p>
            </div>
          </div>

          <div className="h-5 w-px bg-slate-200 dark:bg-slate-800 mx-2 hidden md:block" />

          {/* Status Pill */}
          <div className="flex items-center space-x-2">
            <span className="relative flex h-2.5 w-2.5">
              {isOnline ? (
                <>
                  <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-75"></span>
                  <span className="relative inline-flex rounded-full h-2.5 w-2.5 bg-emerald-500"></span>
                </>
              ) : (
                <span className="relative inline-flex rounded-full h-2.5 w-2.5 bg-amber-500"></span>
              )}
            </span>
            <span
              className={`text-xs font-medium uppercase tracking-wider ${
                isOnline ? 'text-emerald-600 dark:text-emerald-400' : 'text-amber-600 dark:text-amber-400'
              }`}
            >
              {isOnline ? (telemetry?.isPaused ? 'Siaga (Dijeda)' : 'Server Online') : 'Menghubungkan...'}
            </span>
          </div>
        </div>

        {/* Right: Quick Stats, Theme Toggle, & Auth */}
        <div className="flex items-center space-x-2.5 sm:space-x-3.5">
          {/* Active Players */}
          <div
            title={`${onlinePlayersCount} pemain online dari ${maxPlayers} slot kabin (${registeredPlayersCount} terdaftar)`}
            className="hidden sm:flex items-center space-x-1.5 text-xs text-slate-700 dark:text-slate-300 bg-slate-100 dark:bg-slate-800/80 px-3 py-1.5 rounded-md border border-slate-200 dark:border-slate-700/60"
          >
            <Users className="w-3.5 h-3.5 text-emerald-500 dark:text-emerald-400" />
            <span>Online:</span>
            <span className="font-semibold text-slate-900 dark:text-slate-100">
              {onlinePlayersCount}/{maxPlayers}
            </span>
          </div>

          {/* Uptime */}
          {telemetry && (
            <div className="hidden lg:flex items-center space-x-1.5 text-xs text-slate-700 dark:text-slate-300 bg-slate-100 dark:bg-slate-800/80 px-3 py-1.5 rounded-md border border-slate-200 dark:border-slate-700/60">
              <Clock className="w-3.5 h-3.5 text-blue-500 dark:text-blue-400" />
              <span>Uptime:</span>
              <span className="font-semibold text-slate-900 dark:text-slate-100">
                {formatUptime(telemetry.uptimeSeconds)}
              </span>
            </div>
          )}

          {/* Theme Toggle Button */}
          <button
            onClick={onToggleTheme}
            title={theme === 'dark' ? 'Ganti ke Mode Terang (Light Mode)' : 'Ganti ke Mode Gelap (Dark Mode)'}
            className="p-2 rounded-lg bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 text-slate-600 dark:text-slate-300 transition-colors border border-slate-200 dark:border-slate-700 shadow-sm"
          >
            {theme === 'dark' ? (
              <Sun className="w-4 h-4 text-amber-400" />
            ) : (
              <Moon className="w-4 h-4 text-slate-700" />
            )}
          </button>

          {/* Auth Button */}
          {user ? (
            <div className="flex items-center space-x-2 pl-2 border-l border-slate-200 dark:border-slate-800">
              <div className="text-right hidden sm:block">
                <div className="text-xs font-semibold text-slate-800 dark:text-slate-200">{user.username}</div>
                <div className="text-[10px] uppercase font-bold text-emerald-600 dark:text-emerald-400">
                  {user.role === 'admin' ? 'Sysadmin' : user.profession || 'Petani'}
                </div>
              </div>
              <button
                onClick={onLogout}
                title="Keluar Sesi"
                className="p-2 rounded-lg bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 text-slate-600 dark:text-slate-300 hover:text-slate-900 dark:hover:text-white transition-colors border border-slate-200 dark:border-slate-700"
              >
                <LogOut className="w-4 h-4" />
              </button>
            </div>
          ) : (
            <div className="flex items-center space-x-2">
              <button
                onClick={() => onOpenAuth('register')}
                className="hidden sm:flex items-center space-x-1.5 text-xs font-medium bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 text-slate-800 dark:text-slate-200 border border-slate-200 dark:border-slate-700 px-3 py-1.5 rounded-lg transition-colors"
              >
                <UserPlus className="w-3.5 h-3.5 text-emerald-500" />
                <span>Daftar Petani</span>
              </button>
              <button
                onClick={() => onOpenAuth('login')}
                className="flex items-center space-x-1.5 text-xs font-medium bg-emerald-600 hover:bg-emerald-500 text-white px-3 py-1.5 rounded-lg transition-colors shadow-sm"
              >
                <LogIn className="w-3.5 h-3.5" />
                <span>Masuk</span>
              </button>
            </div>
          )}
        </div>
      </div>
    </header>
  );
};
