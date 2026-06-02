export type Mode = 'automatic' | 'manual';
export type LocationKey = 'inside' | 'outside';
export type MetricKey = 'temperature' | 'humidity' | 'dewPoint' | 'dewPointDifference';

export interface LocationReading {
  temperature: number;
  humidity: number;
  dewPointC: number;
}

export interface DashboardSnapshot {
  measuredAt: string;
  inside?: LocationReading | null;
  outside?: LocationReading | null;
  dewPointDifferenceC?: number | null;
  controlDewPointDifferenceC?: number | null;
  manualDewPointDifferenceC?: number | null;
  fanOnThresholdC?: number | null;
  fanOffThresholdC?: number | null;
  displayTime?: string | null;
  displayTimeSource?: string | null;
  fanOn?: boolean | null;
  controlMode?: Mode | string | null;
}

export interface ControlSettings {
  mode: Mode;
  manualDewPointDifferenceC?: number | null;
  dewPointDiffOn: number;
  dewPointDiffOff: number;
  fanOnThresholdC: number;
  fanOffThresholdC: number;
  displayTime?: string | null;
  displayTimeSource?: string | null;
  usePiTime: boolean;
  updatedAt: string;
}

export interface ControlUpdate {
  mode?: Mode;
  manualDewPointDifferenceC?: number | null;
  dewPointDiffOn?: number;
  dewPointDiffOff?: number;
  fanOnThresholdC?: number;
  fanOffThresholdC?: number;
  displayTime?: string | null;
  usePiTime?: boolean;
}

export interface HealthStatus {
  ok: boolean;
  database: string;
  apiKeyRequired: boolean;
  utcNow: string;
}

export interface HistoryRow {
  measuredAt: string;
  metric: MetricKey | string;
  location?: LocationKey | string | null;
  value?: number | null;
  dewPointDifferenceC?: number | null;
  controlDewPointDifferenceC?: number | null;
  manualDewPointDifferenceC?: number | null;
  fanOnThresholdC?: number | null;
  fanOffThresholdC?: number | null;
  displayTime?: string | null;
  displayTimeSource?: string | null;
  fanOn: boolean;
  controlMode: Mode | string;
}
