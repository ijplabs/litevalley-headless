import React from 'react';
import { LayoutDashboard, Terminal, Sprout, Settings } from 'lucide-react';

export type TabKey = 'overview' | 'farm' | 'console' | 'settings';

interface TabsNavProps {
  activeTab: TabKey;
  onChangeTab: (tab: TabKey) => void;
  isAdmin: boolean;
}

export const TabsNav: React.FC<TabsNavProps> = ({ activeTab, onChangeTab, isAdmin }) => {
  const tabs = [
    { key: 'overview' as TabKey, label: 'Overview Farm', icon: LayoutDashboard, adminOnly: false },
    { key: 'farm' as TabKey, label: 'Kondisi Pertanian', icon: Sprout, adminOnly: false },
    { key: 'console' as TabKey, label: 'Console & Logs', icon: Terminal, adminOnly: true },
    { key: 'settings' as TabKey, label: 'Settings & Steam', icon: Settings, adminOnly: true },
  ].filter((t) => !t.adminOnly || isAdmin);

  return (
    <div className="border-b border-slate-200 dark:border-slate-800 bg-white/50 dark:bg-slate-900/50 transition-colors">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <nav className="flex space-x-2 sm:space-x-8 overflow-x-auto py-2">
          {tabs.map((tab) => {
            const Icon = tab.icon;
            const isActive = activeTab === tab.key;
            return (
              <button
                key={tab.key}
                onClick={() => onChangeTab(tab.key)}
                className={`flex items-center space-x-2 py-2 px-3 sm:px-1 border-b-2 text-sm font-medium whitespace-nowrap transition-colors rounded-t-md sm:rounded-none ${
                  isActive
                    ? 'border-emerald-500 text-emerald-600 dark:text-emerald-400 bg-slate-100 dark:bg-slate-800/40 sm:bg-transparent'
                    : 'border-transparent text-slate-500 dark:text-slate-400 hover:text-slate-800 dark:hover:text-slate-200 hover:border-slate-300 dark:hover:border-slate-700'
                }`}
              >
                <Icon className={`w-4 h-4 ${isActive ? 'text-emerald-600 dark:text-emerald-400' : 'text-slate-400 dark:text-slate-500'}`} />
                <span>{tab.label}</span>
              </button>
            );
          })}
        </nav>
      </div>
    </div>
  );
};
