import React, { useState, useEffect } from 'react';
import { AuthUser, ServerTelemetry, PlayerData } from './types';
import { getStoredAuth, setStoredAuth, fetchTelemetry, fetchPlayers } from './lib/api';
import { Theme, getInitialTheme, applyTheme } from './lib/theme';
import { Header } from './components/Header';
import { TabsNav, TabKey } from './components/TabsNav';
import { OverviewTab } from './components/OverviewTab';
import { ConsoleTab } from './components/ConsoleTab';
import { FarmTab } from './components/FarmTab';
import { SettingsTab } from './components/SettingsTab';
import { AuthModal } from './components/AuthModal';

export const App: React.FC = () => {
  const [activeTab, setActiveTab] = useState<TabKey>('overview');
  const [user, setUser] = useState<AuthUser | null>(getStoredAuth());
  const [telemetry, setTelemetry] = useState<ServerTelemetry | null>(null);
  const [players, setPlayers] = useState<PlayerData[]>([]);
  const [isAuthOpen, setIsAuthOpen] = useState(false);
  const [theme, setTheme] = useState<Theme>(getInitialTheme());

  const isAdmin = user?.role === 'admin';

  // Sinkronisasi tema class pada <html>
  useEffect(() => {
    applyTheme(theme);
  }, [theme]);

  const handleToggleTheme = () => {
    setTheme((prev) => (prev === 'dark' ? 'light' : 'dark'));
  };

  // Jika bukan admin dan tab aktif adalah tab sysadmin, alihkan kembali ke overview
  useEffect(() => {
    if (!isAdmin && (activeTab === 'console' || activeTab === 'settings')) {
      setActiveTab('overview');
    }
  }, [isAdmin, activeTab]);

  const loadData = async () => {
    try {
      const [tel, play] = await Promise.all([
        fetchTelemetry().catch(() => null),
        fetchPlayers().catch(() => []),
      ]);
      if (tel) setTelemetry(tel);
      if (play) setPlayers(play);
    } catch {}
  };

  useEffect(() => {
    loadData();
    const timer = setInterval(loadData, 4000);
    return () => clearInterval(timer);
  }, []);

  const handleLogout = () => {
    setStoredAuth(null);
    setUser(null);
    setActiveTab('overview');
  };

  return (
    <div className="min-h-screen bg-slate-50 dark:bg-slate-950 text-slate-900 dark:text-slate-100 flex flex-col font-sans transition-colors duration-150">
      {/* Top Header */}
      <Header
        telemetry={telemetry}
        user={user}
        theme={theme}
        onToggleTheme={handleToggleTheme}
        onOpenAuth={() => setIsAuthOpen(true)}
        onLogout={handleLogout}
        activePlayersCount={players.length}
      />

      {/* Tabs Navigation (Khusus Admin memunculkan Console & Settings) */}
      <TabsNav 
        activeTab={activeTab} 
        onChangeTab={setActiveTab} 
        isAdmin={isAdmin}
      />

      {/* Main Content Area */}
      <main className="flex-1 max-w-7xl w-full mx-auto px-4 sm:px-6 lg:px-8 py-6">
        {activeTab === 'overview' && (
          <OverviewTab
            telemetry={telemetry}
            players={players}
            user={user}
            onRefresh={loadData}
          />
        )}

        {isAdmin && activeTab === 'console' && (
          <ConsoleTab user={user} />
        )}

        {activeTab === 'farm' && (
          <FarmTab telemetry={telemetry} user={user} />
        )}

        {isAdmin && activeTab === 'settings' && (
          <SettingsTab telemetry={telemetry} user={user} />
        )}
      </main>

      {/* Auth Modal */}
      <AuthModal
        isOpen={isAuthOpen}
        onClose={() => setIsAuthOpen(false)}
        onSuccess={(newUser) => setUser(newUser)}
      />
    </div>
  );
};

export default App;
