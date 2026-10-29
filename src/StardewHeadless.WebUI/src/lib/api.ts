import { AuthUser, ServerTelemetry, PlayerData } from '../types';

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

export async function fetchTelemetry(): Promise<ServerTelemetry> {
  const res = await fetch('/api/telemetry');
  if (!res.ok) throw new Error('Failed to fetch telemetry');
  return res.json();
}

export async function fetchPlayers(): Promise<PlayerData[]> {
  const res = await fetch('/api/players');
  if (!res.ok) throw new Error('Failed to fetch players');
  return res.json();
}

export async function executeAdminAction(endpoint: string, payload?: any): Promise<{ ok: boolean; message?: string }> {
  const auth = getStoredAuth();
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  };
  if (auth?.token) {
    headers['Authorization'] = `Bearer ${auth.token}`;
  }

  const res = await fetch(endpoint, {
    method: 'POST',
    headers,
    body: payload ? JSON.stringify(payload) : undefined,
  });

  if (!res.ok) {
    const errText = await res.text().catch(() => 'Request failed');
    return { ok: false, message: errText };
  }

  return { ok: true };
}
