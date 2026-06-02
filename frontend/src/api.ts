import type { ControlSettings, ControlUpdate, DashboardSnapshot, HealthStatus, HistoryRow, LocationKey, MetricKey } from './types';

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? '';
const API_KEY_STORAGE_KEY = 'taupunkt-api-key';

export class ApiError extends Error {
  status: number;

  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

export function getStoredApiKey(): string {
  return localStorage.getItem(API_KEY_STORAGE_KEY) ?? '';
}

export function setStoredApiKey(value: string): void {
  const trimmed = value.trim();
  if (trimmed) {
    localStorage.setItem(API_KEY_STORAGE_KEY, trimmed);
  } else {
    localStorage.removeItem(API_KEY_STORAGE_KEY);
  }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set('Accept', 'application/json');

  const key = getStoredApiKey();
  if (key) {
    headers.set('X-API-Key', key);
  }

  if (init.body && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  const response = await fetch(`${API_BASE}${path}`, { ...init, headers });

  if (response.status === 401) {
    throw new ApiError(401, 'API key is missing or invalid.');
  }

  if (!response.ok) {
    let message = `HTTP ${response.status}`;
    try {
      const payload = await response.json();
      message = payload.error ?? payload.title ?? message;
    } catch {
      const text = await response.text();
      if (text) {
        message = text;
      }
    }
    throw new ApiError(response.status, message);
  }

  const contentType = response.headers.get('content-type') ?? '';
  if (!contentType.includes('application/json')) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

export const api = {
  health: () => request<HealthStatus>('/api/status/health'),
  control: () => request<ControlSettings>('/api/control'),
  updateControl: (update: ControlUpdate | Record<string, unknown>) => request<ControlSettings>('/api/control', {
    method: 'PUT',
    body: JSON.stringify(update)
  }),
  latest: (take = 10) => request<DashboardSnapshot[]>(`/api/dashboard/latest?take=${take}`),
  history: (metric: MetricKey, location?: LocationKey) => {
    const params = new URLSearchParams();
    if (location) {
      params.set('location', location);
    }
    params.set('limit', '10000');
    return request<HistoryRow[]>(`/api/history/${metric}?${params.toString()}`);
  }
};
