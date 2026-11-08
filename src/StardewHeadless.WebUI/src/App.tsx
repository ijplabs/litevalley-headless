import React, { useState, useEffect } from 'react';
import {
  AuthUser,
  ServerTelemetry,
  PlayerData,
  TreasuryData,
  CommunityProject,
  WorldSummary,
} from './types';
import {
  getStoredAuth,
  setStoredAuth,
  fetchTelemetry,
  fetchPlayers,
  fetchTreasury,
  fetchProjects,
  fetchWorldSummary,
} from './lib/api';
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
  const [treasury, setTreasury] = useState<TreasuryData | null>(null);
  const [projects, setProjects] = useState<CommunityProject[]>([]);
  const [worldSummary, setWorldSummary] = useState<WorldSummary | null>(null);
  const [isAuthOpen, setIsAuthOpen] = useState(false);
  const [authMode, setAuthMode] = useState<'login' | 'register'>('login');
  const [theme, setTheme] = useState<Theme>(getInitialTheme());

  const isAdmin = user?.role === 'admin';

  useEffect(() => {
    applyTheme(theme);
  }, [theme]);

  const handleToggleTheme = () => {
    setTheme((prev) => (prev === 'dark' ? 'light' : 'dark'));
  };

  useEffect(() => {
    if (!isAdmin && (activeTab === 'console' || activeTab === 'settings')) {
      setActiveTab('overview');
    }
  }, [isAdmin, activeTab]);

  const loadData = async () => {
    try {
      const [tel, play, treas, proj, summary] = await Promise.all([
        fetchTelemetry().catch(() => null),
        fetchPlayers().catch(() => []),
        fetchTreasury().catch(() => null),
        fetchProjects().catch(() => []),
        fetchWorldSummary().catch(() => null),
      ]);
      if (tel) setTelemetry(tel);
      if (play) setPlayers(play);
      if (treas) setTreasury(treas);
      if (proj) setProjects(proj);
      if (summary) setWorldSummary(summary);
    } catch {}
  };

  useEffect(() => {
    loadData();
    const timer = setInterval(loadData, 4000);
    return () => clearInterval(timer);
  }, []);

  const handleOpenAuth = (mode: 'login' | 'register' = 'login') => {
    setAuthMode(mode);
    setIsAuthOpen(true);
  };

  const handleLogout = () => {
    setStoredAuth(null);
    setUser(null);
    setActiveTab('overview');
  };

  const onlinePlayersCount =
    telemetry?.onlinePlayers ?? players.filter((p) => p.isOnline).length;

  return (
    <div className="min-h-screen bg-slate-50 dark:bg-slate-950 text-slate-900 dark:text-slate-100 flex flex-col font-sans transition-colors duration-150">
      {/* Top Header */}
      <Header
        telemetry={telemetry}
        user={user}
        theme={theme}
        onToggleTheme={handleToggleTheme}
        onOpenAuth={handleOpenAuth}
        onLogout={handleLogout}
        onlinePlayersCount={onlinePlayersCount}
        registeredPlayersCount={players.length}
      />

      {/* Tabs Navigation */}
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
            treasury={treasury}
            user={user}
            onRefresh={loadData}
            onOpenAuth={handleOpenAuth}
          />
        )}

        {isAdmin && activeTab === 'console' && <ConsoleTab user={user} />}

        {activeTab === 'farm' && (
          <FarmTab
            telemetry={telemetry}
            worldSummary={worldSummary}
            treasury={treasury}
            projects={projects}
            user={user}
            onRefresh={loadData}
          />
        )}

        {isAdmin && activeTab === 'settings' && (
          <SettingsTab telemetry={telemetry} user={user} />
        )}
      </main>

      {/* Auth Modal */}
      <AuthModal
        isOpen={isAuthOpen}
        initialMode={authMode}
        onClose={() => setIsAuthOpen(false)}
        onSuccess={(newUser) => {
          setUser(newUser);
          loadData();
        }}
      />
    </div>
  );
};

export default App;
