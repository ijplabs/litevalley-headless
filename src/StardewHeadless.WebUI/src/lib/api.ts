import {
  AuthUser,
  ServerTelemetry,
  PlayerData,
  TreasuryData,
  CommunityProject,
  WorldSummary,
  SaveInspectMeta,
  PlayerOtpInfo,
  AdminModsResponse,
  ModPolicyConfig,
  ClientModRequirementsResponse,
  AdminLogsResponse,
  AntiCheatIncidentItem,
} from '../types';

const AUTH_STORAGE_KEY = 'litevalley_auth';

export function getStoredAuth(): AuthUser | null {
  try {
    const raw = localStorage.getItem(AUTH_STORAGE_KEY);
    if (!raw) return null;
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

export function setStoredAuth(user: AuthUser | null): void {
  if (user) {
    localStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify(user));
  } else {
    localStorage.removeItem(AUTH_STORAGE_KEY);
  }
}

export function getAuthHeaders(extra?: Record<string, string>): Record<string, string> {
  const auth = getStoredAuth();
  const headers: Record<string, string> = { ...(extra || {}) };
  if (auth?.token) {
    headers['Authorization'] = `Bearer ${auth.token}`;
  }
  return headers;
}

export function formatUptime(seconds?: number): string {
  if (!seconds || seconds <= 0) return '0m';
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const mins = Math.floor((seconds % 3600) / 60);
  if (days > 0) return `${days}h ${hours}j ${mins}m`;
  if (hours > 0) return `${hours}j ${mins}m`;
  return `${mins}m ${seconds % 60}d`;
}

export async function fetchTelemetry(): Promise<ServerTelemetry> {
  const res = await fetch('/api/telemetry');
  if (!res.ok) throw new Error('Gagal memuat telemetri server');
  return res.json();
}

export async function fetchPlayers(): Promise<PlayerData[]> {
  const res = await fetch('/api/players');
  if (!res.ok) throw new Error('Gagal memuat daftar pemain');
  return res.json();
}

export async function fetchTreasury(): Promise<TreasuryData> {
  const res = await fetch('/api/treasury');
  if (!res.ok) throw new Error('Gagal memuat kas desa');
  return res.json();
}

export async function fetchProjects(): Promise<CommunityProject[]> {
  const res = await fetch('/api/projects');
  if (!res.ok) throw new Error('Gagal memuat proyek komunitas');
  return res.json();
}

export async function fetchWorldSummary(): Promise<WorldSummary> {
  const res = await fetch('/api/v2/world/summary');
  if (!res.ok) throw new Error('Gagal memuat ringkasan dunia');
  return res.json();
}

export async function fetchClientModRequirements(): Promise<ClientModRequirementsResponse> {
  const res = await fetch('/api/mods/client-requirements');
  if (!res.ok) throw new Error('Gagal memuat daftar mod server');
  return res.json();
}

export async function fetchPlayerOtp(username: string): Promise<PlayerOtpInfo> {
  const res = await fetch(`/api/user/otp?username=${encodeURIComponent(username)}`, {
    headers: getAuthHeaders(),
  });
  if (!res.ok) {
    const data = await res.json().catch(() => ({}));
    throw new Error(data.error || 'Gagal memuat kode OTP pemain');
  }
  return res.json();
}

export async function respecPlayerProfession(
  username: string,
  newProfession: string,
  password?: string
): Promise<{ ok: boolean; message: string; feeDeducted?: number }> {
  const res = await fetch('/api/user/respec-profession', {
    method: 'POST',
    headers: getAuthHeaders({ 'Content-Type': 'application/json' }),
    body: JSON.stringify({ username, newProfession, password: password || '' }),
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) {
    return { ok: false, message: data.error || 'Gagal mengubah profesi' };
  }
  return { ok: true, message: data.message || 'Profesi berhasil diperbarui', feeDeducted: data.feeDeducted };
}

export async function unlinkPlayerCharacter(
  username: string,
  passwordOrOtp: string
): Promise<{ ok: boolean; message: string }> {
  const res = await fetch('/api/user/unlink-character', {
    method: 'POST',
    headers: getAuthHeaders({ 'Content-Type': 'application/json' }),
    body: JSON.stringify({ username, passwordOrOtp }),
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) {
    return { ok: false, message: data.error || 'Gagal memutuskan tautan karakter' };
  }
  return { ok: true, message: data.message || 'Tautan karakter berhasil diputuskan' };
}

export async function executeAdminAction(
  endpoint: string,
  payload?: any,
  method: 'POST' | 'DELETE' = 'POST'
): Promise<{ ok: boolean; message?: string; data?: any }> {
  const res = await fetch(endpoint, {
    method,
    headers: getAuthHeaders({ 'Content-Type': 'application/json' }),
    body: payload !== undefined ? JSON.stringify(payload) : undefined,
  });

  const data = await res.json().catch(() => null);
  if (!res.ok) {
    return {
      ok: false,
      message: data?.error || data?.message || `Permintaan gagal (HTTP ${res.status})`,
    };
  }

  return { ok: true, message: data?.message, data };
}

export async function fetchSaveInspection(): Promise<SaveInspectMeta | null> {
  const res = await fetch('/api/admin/save/inspect', {
    headers: getAuthHeaders(),
  });
  if (!res.ok) return null;
  return res.json();
}

export async function downloadSaveBackupZip(fallbackFarmName = 'LiteValley_Save'): Promise<{ ok: boolean; message: string }> {
  const res = await fetch('/api/admin/save/export', {
    method: 'GET',
    headers: getAuthHeaders(),
  });

  if (!res.ok) {
    const err = await res.json().catch(() => ({}));
    return { ok: false, message: err.error || `Gagal mengunduh arsip savegame (HTTP ${res.status})` };
  }

  const blob = await res.blob();
  const disposition = res.headers.get('content-disposition');
  let filename = `${fallbackFarmName}_backup.zip`;
  if (disposition && disposition.includes('filename=')) {
    const match = disposition.match(/filename="?([^";]+)"?/i);
    if (match?.[1]) filename = match[1];
  }

  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);

  return { ok: true, message: `Arsip ${filename} berhasil diunduh.` };
}

export async function uploadSavegameZip(file: File): Promise<{ ok: boolean; message: string }> {
  const buffer = await file.arrayBuffer();
  const res = await fetch('/api/admin/save/import', {
    method: 'POST',
    headers: getAuthHeaders({
      'Content-Type': 'application/zip',
      'X-File-Name': file.name,
    }),
    body: buffer,
  });

  const data = await res.json().catch(() => ({}));
  if (!res.ok) {
    return { ok: false, message: data.error || 'Gagal mengimpor arsip savegame.' };
  }
  return { ok: true, message: data.message || 'Savegame berhasil diimpor ke server!' };
}

export async function fetchAdminLogs(params?: {
  limit?: number;
  level?: string;
  category?: string;
  search?: string;
}): Promise<AdminLogsResponse> {
  const qs = new URLSearchParams();
  if (params?.limit) qs.set('limit', String(params.limit));
  if (params?.level && params.level !== 'ALL') qs.set('level', params.level);
  if (params?.category && params.category !== 'ALL') qs.set('category', params.category);
  if (params?.search) qs.set('search', params.search);

  const res = await fetch(`/api/admin/logs?${qs.toString()}`, {
    headers: getAuthHeaders(),
  });
  if (!res.ok) throw new Error('Gagal memuat log server');
  return res.json();
}

export async function fetchAntiCheatIncidents(limit = 50): Promise<AntiCheatIncidentItem[]> {
  const res = await fetch(`/api/v2/anticheat/incidents?limit=${limit}`, {
    headers: getAuthHeaders(),
  });
  if (!res.ok) return [];
  return res.json();
}

export async function fetchAdminMods(): Promise<AdminModsResponse> {
  const res = await fetch('/api/admin/mods', {
    headers: getAuthHeaders(),
  });
  if (!res.ok) throw new Error('Gagal memuat daftar mod');
  return res.json();
}

export async function fetchAdminModPolicy(): Promise<ModPolicyConfig> {
  const res = await fetch('/api/admin/mods/policy', {
    headers: getAuthHeaders(),
  });
  if (!res.ok) throw new Error('Gagal memuat kebijakan mod');
  return res.json();
}

export async function updateAdminModPolicy(allowAllMods: boolean): Promise<{ ok: boolean; message: string }> {
  const res = await executeAdminAction('/api/admin/mods/policy', { allowAllMods });
  return { ok: res.ok, message: res.message || 'Kebijakan mod diperbarui.' };
}

export async function uploadAdminModZip(file: File): Promise<{ ok: boolean; message: string }> {
  const formData = new FormData();
  formData.append('file', file);

  const res = await fetch('/api/admin/mods/upload', {
    method: 'POST',
    headers: getAuthHeaders(),
    body: formData,
  });

  const data = await res.json().catch(() => ({}));
  if (!res.ok) {
    return { ok: false, message: data.error || data.message || 'Gagal mengunggah mod (.zip).' };
  }
  return { ok: true, message: data.message || `Mod ${file.name} berhasil dipasang.` };
}
