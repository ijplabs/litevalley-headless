export interface AuthUser {
  token: string;
  role: 'admin' | 'player' | 'guest';
  username: string;
  email?: string;
  userId?: number;
  profession?: string;
}

export interface ServerTelemetry {
  serverName: string;
  farmName: string;
  fps: number;
  uptimeSeconds: number;
  memoryMB: number;
  onlinePlayers: number;
  maxPlayers: number;
  isPaused: boolean;
  year: number;
  season: string;
  dayOfMonth: number;
  timeOfDay: number;
  formattedTime: string;
  weather: string;
  dailyLuck: number;
  fortuneQuote: string;
  siloCount: number;
}

export interface PlayerData {
  id: number;
  username: string;
  profession: string;
  cabinIndex: number;
  cabinName: string;
  isOnline: boolean;
  money: number;
  isBanned: boolean;
  banReason?: string | null;
  bannedAt?: string | null;
  isDeleted?: boolean;
  lastIp?: string | null;
  lastHwid?: string | null;
  respecCount: number;
  lastRespecAt?: string | null;
  createdAt?: string;
}

export interface TreasuryTransaction {
  id: number;
  userId?: number | null;
  transactionType: string;
  amount: number;
  balanceAfter: number;
  notes?: string | null;
  createdAt: string;
}

export interface TreasuryData {
  balance: number;
  taxRate: number;
  transactions: TreasuryTransaction[];
}

export interface CommunityProject {
  id: string;
  name: string;
  description: string;
  requiredProfession: string;
  targetCount: number;
  currentCount: number;
  unit: string;
  isCompleted: boolean;
  hayPieces: number;
  maxHay: number;
  statusText: string;
}

export interface WorldSummary {
  totalLocations: number;
  totalCrops: number;
  wateredCropsToday: number;
  readyToHarvestCrops: number;
  totalTrees: number;
  totalOres: number;
  totalAnimals: number;
  totalMachines: number;
  totalChests: number;
  totalFarmersOnline: number;
}

export interface SaveInspectMeta {
  valid: boolean;
  farm_name: string;
  farm_type: number;
  farm_type_name: string;
  unique_id: string;
  in_game_date?: {
    season: string;
    day: number;
    year: number;
    display: string;
  };
  host?: {
    name: string;
    money: number;
    total_money_earned: number;
    daily_luck: number;
  };
  cabins_count: number;
  source_type?: string;
}

export interface PlayerOtpInfo {
  username: string;
  profession: string;
  code: string;
  command: string;
  remainingSeconds: number;
  intervalSeconds: number;
  isGameLinked: boolean;
  linkedFarmerName?: string | null;
  linkedFarmerId?: number | null;
}

export interface InstalledMod {
  uniqueId: string;
  name: string;
  author: string;
  version: string;
  description?: string | null;
  folderName: string;
  folderPath: string;
  isEnabled: boolean;
  category: string;
  sizeBytes: number;
  contentPackFor?: string | null;
  dependencies: string[];
  installedAt: string;
}

export interface AdminModsResponse {
  modsDirectory: string;
  total: number;
  activeCount: number;
  disabledCount: number;
  mods: InstalledMod[];
}

export interface ModPolicyConfig {
  allowAllMods: boolean;
  strictEnforcement: boolean;
  warning: string;
}

export interface ClientModRequirementItem {
  uniqueId: string;
  name: string;
  version: string;
  author: string;
  category: string;
  contentPackFor?: string | null;
}

export interface ClientModRequirementsResponse {
  serverName: string;
  allowAllMods: boolean;
  totalActiveMods: number;
  requiredClientMods: ClientModRequirementItem[];
}

export interface LogEntryItem {
  timestamp: string;
  level: string;
  source: string;
  category: string;
  message: string;
  exception?: string | null;
}

export interface AdminLogsResponse {
  success: boolean;
  total: number;
  categories: string[];
  logs: LogEntryItem[];
}

export interface AntiCheatIncidentItem {
  id?: number;
  farmerId: number;
  farmerName: string;
  category: string;
  severity: string;
  description: string;
  actionTaken: string;
  timestamp?: string;
  detectedAt?: string;
}
