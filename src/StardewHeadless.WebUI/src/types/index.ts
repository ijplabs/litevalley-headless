export interface AuthUser {
  token: string;
  role: 'admin' | 'player' | 'guest';
  username: string;
}

export interface ServerTelemetry {
  version?: string;
  serverName?: string;
  uptimeSeconds?: number;
  uptime?: string;
  cpuPercent?: number;
  ramUsageMb?: number;
  maxRamMb?: number;
  port?: number;
  webPort?: number;
  status?: 'running' | 'stopped' | 'paused' | 'restarting';
  gameTime?: {
    season?: string;
    day?: number;
    year?: number;
    timeOfDay?: string;
    weather?: string;
    weatherTomorrow?: string;
  };
  farm?: {
    name?: string;
    farmType?: string;
    sharedMoney?: number;
    activeSaveFile?: string;
    animalsCount?: number;
    cropsReady?: number;
  };
  steam?: {
    isConfigured?: boolean;
    appId?: number;
    genuineStatus?: string;
  };
}

export interface PlayerData {
  id: string | number;
  name: string;
  location?: string;
  pingMs?: number;
  isHost?: boolean;
  connectedAt?: string;
  characterProfession?: string;
}

export interface LogItem {
  id: string;
  timestamp: string;
  level: 'info' | 'warn' | 'error' | 'trace';
  category: string;
  message: string;
}
