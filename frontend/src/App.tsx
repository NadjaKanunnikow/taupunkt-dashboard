import { useCallback, useEffect, useState } from 'react';
import {
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis
} from 'recharts';
import { ApiError, api, getStoredApiKey, setStoredApiKey } from './api';
import type {
  ControlSettings,
  DashboardSnapshot,
  HealthStatus,
  HistoryRow,
  LocationKey,
  MetricKey
} from './types';

type Route =
  | { page: 'home' }
  | { page: 'history'; metric: MetricKey; location?: LocationKey };

type ChartDatum = {
  label: string;
  measuredAt: string;
  value: number;
  fanOn?: boolean | null;
  controlMode?: string | null;
  fanOnThresholdC?: number | null;
  fanOffThresholdC?: number | null;
};

const metricLabels: Record<MetricKey, string> = {
  temperature: 'Temperatur',
  humidity: 'Luftfeuchtigkeit',
  dewPoint: 'Taupunkt',
  dewPointDifference: 'Taupunkt-Differenz'
};

const metricUnits: Record<MetricKey, string> = {
  temperature: '°C',
  humidity: '%',
  dewPoint: '°C',
  dewPointDifference: '°C'
};

const locationLabels: Record<LocationKey, string> = {
  inside: 'Innen',
  outside: 'Außen'
};

function App() {
  const [route, setRoute] = useState<Route>(parseRoute);
  const [apiKeyVersion, setApiKeyVersion] = useState(0);

  useEffect(() => {
    const onHashChange = () => setRoute(parseRoute());
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, []);

  const onApiKeySaved = () => setApiKeyVersion((value) => value + 1);

  return (
    <main className="app-shell">
      <header className="hero">
        <div>
          <p className="eyebrow">Joy-Pi / Raspberry Pi</p>
          <h1>Taupunkt Dashboard</h1>
          <p className="hero-text">
            Temperatur, Feuchtigkeit, Taupunkt und Lüfterstatus aus dem Python-Controller.
          </p>
        </div>
        <a className="home-link" href="#/">Startseite</a>
      </header>

      {route.page === 'home' ? (
        <HomePage apiKeyVersion={apiKeyVersion} onApiKeySaved={onApiKeySaved} />
      ) : (
        <HistoryPage
          metric={route.metric}
          location={route.location}
          apiKeyVersion={apiKeyVersion}
          onApiKeySaved={onApiKeySaved}
        />
      )}
    </main>
  );
}

function HomePage({ apiKeyVersion, onApiKeySaved }: { apiKeyVersion: number; onApiKeySaved: () => void }) {
  const [health, setHealth] = useState<HealthStatus | null>(null);
  const [settings, setSettings] = useState<ControlSettings | null>(null);
  const [snapshots, setSnapshots] = useState<DashboardSnapshot[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [authError, setAuthError] = useState(false);
  const [lastRefresh, setLastRefresh] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setError(null);
    try {
      const nextHealth = await api.health();
      setHealth(nextHealth);
      const nextSnapshots = await api.latest(10);
      setSnapshots(nextSnapshots);
      setLastRefresh(new Date().toISOString());
      const nextSettings = await api.control();
      setSettings(nextSettings);
      setAuthError(false);
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        setAuthError(true);
        setError('API key fehlt oder ist falsch. Speichere den gleichen Key wie in Render APP_API_KEY.');
      } else {
        setError(err instanceof Error ? err.message : 'Unbekannter Fehler');
      }
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    let alive = true;
    const run = async () => {
      if (alive) {
        await refresh();
      }
    };

    run();
    const interval = window.setInterval(run, 30_000);
    return () => {
      alive = false;
      window.clearInterval(interval);
    };
  }, [refresh, apiKeyVersion]);

  const latest = snapshots.length > 0 ? snapshots[snapshots.length - 1] : undefined;

  return (
    <>
      <AdminKeyPanel
        health={health}
        forceOpen={authError}
        onSaved={() => {
          onApiKeySaved();
          refresh();
        }}
      />

      {error && <div className="banner error">{error}</div>}
      {loading && <div className="banner">Lade aktuelle Daten...</div>}

      <section className="status-grid">
        <StatusCard label="Backend" value={health?.ok ? 'GOOD' : 'Unbekannt'} />
        <StatusCard label="Modus" value={settings?.mode === 'manual' ? 'Manuell' : 'Automatik'} />
        <StatusCard label="Lüfter" value={latest?.fanOn ? 'Ein' : 'Aus'} />
        <StatusCard label="Letztes Update" value={lastRefresh ? formatFullDateTime(lastRefresh) : '-'} />
      </section>

      {settings && (
        <ControlPanel
          settings={settings}
          latestSnapshot={latest}
          onSettingsChanged={(next) => setSettings(next)}
          onError={setError}
        />
      )}

      <SplitMetricSection
        metric="temperature"
        title="Temperatur"
        unit="°C"
        snapshots={snapshots}
      />

      <SplitMetricSection
        metric="humidity"
        title="Luftfeuchtigkeit"
        unit="%"
        snapshots={snapshots}
      />

      <SplitMetricSection
        metric="dewPoint"
        title="Taupunkt"
        unit="°C"
        snapshots={snapshots}
      />

      <section className="metric-section">
        <div className="section-heading">
          <div>
            <p className="eyebrow">Regelwert</p>
            <h2>Taupunkt-Differenz</h2>
          </div>
        </div>
        <ChartCard
          title="Taupunkt-Differenz innen minus außen"
          unit="°C"
          data={makeDifferenceData(snapshots)}
          historyHref="#/history/dewPointDifference"
          showFanInTooltip
          wide
        />
      </section>
    </>
  );
}

function ControlPanel({
  settings,
  latestSnapshot,
  onSettingsChanged,
  onError
}: {
  settings: ControlSettings;
  latestSnapshot?: DashboardSnapshot;
  onSettingsChanged: (settings: ControlSettings) => void;
  onError: (message: string | null) => void;
}) {
  const latestMeasuredDiff = latestSnapshot?.dewPointDifferenceC ?? 0;
  const [manualDiff, setManualDiff] = useState(formatNumber(settings.manualDewPointDifferenceC ?? latestMeasuredDiff));
  const [onThreshold, setOnThreshold] = useState(formatNumber(settings.fanOnThresholdC));
  const [offThreshold, setOffThreshold] = useState(formatNumber(settings.fanOffThresholdC));
  const [displayTime, setDisplayTime] = useState(settings.displayTime ?? currentTimeForInput());
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    setManualDiff(formatNumber(settings.manualDewPointDifferenceC ?? latestMeasuredDiff));
    setOnThreshold(formatNumber(settings.fanOnThresholdC));
    setOffThreshold(formatNumber(settings.fanOffThresholdC));
    setDisplayTime(settings.displayTime ?? currentTimeForInput());
  }, [settings, latestMeasuredDiff]);

  const isManual = settings.mode === 'manual';

  const send = async (payload: Record<string, unknown>) => {
    setSaving(true);
    onError(null);
    try {
      const next = await api.updateControl(payload);
      onSettingsChanged(next);
    } catch (err) {
      onError(err instanceof Error ? err.message : 'Speichern fehlgeschlagen');
    } finally {
      setSaving(false);
    }
  };

  const manualNumber = parseInputNumber(manualDiff);
  const onNumber = parseInputNumber(onThreshold);
  const offNumber = parseInputNumber(offThreshold);

  return (
    <section className="control-panel">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Steuerung</p>
          <h2>Raspberry Pi Einstellungen</h2>
        </div>
        {saving && <span className="saving">Speichere...</span>}
      </div>

      <div className="control-grid">
        <div className="control-card">
          <h3>Modus</h3>
          <div className="button-row">
            <button
              className={settings.mode === 'automatic' ? 'active' : ''}
              onClick={() => send({ mode: 'automatic' })}
              disabled={saving}
            >
              Automatik
            </button>
            <button
              className={settings.mode === 'manual' ? 'active' : ''}
              onClick={() => send({ mode: 'manual', manualDewPointDifferenceC: manualNumber ?? latestMeasuredDiff })}
              disabled={saving}
            >
              Manuell
            </button>
          </div>
          <p className="hint">
            Beim Wechsel zu Automatik bleiben Schwellen und Displayzeit erhalten. Die manuelle Taupunkt-Differenz wird gelöscht.
          </p>
        </div>

        <div className="control-card">
          <h3>Manuelle Taupunkt-Differenz</h3>
          <label>
            Wert in °C
            <input
              type="number"
              step="0.1"
              min="-40"
              max="60"
              value={manualDiff}
              onChange={(event) => setManualDiff(event.target.value)}
              disabled={!isManual || saving}
            />
          </label>
          <button
            onClick={() => send({ mode: 'manual', manualDewPointDifferenceC: manualNumber ?? 0 })}
            disabled={!isManual || saving}
          >
            Taupunkt-Differenz speichern
          </button>
        </div>

        <div className="control-card wide-card">
          <h3>Lüfter-Schwellen</h3>
          <div className="inline-fields">
            <label>
              Lüfter EIN ab °C
              <input
                type="number"
                step="0.1"
                min="-40"
                max="60"
                value={onThreshold}
                onChange={(event) => setOnThreshold(event.target.value)}
                disabled={!isManual || saving}
              />
            </label>
            <label>
              Lüfter AUS bis °C
              <input
                type="number"
                step="0.1"
                min="-40"
                max="60"
                value={offThreshold}
                onChange={(event) => setOffThreshold(event.target.value)}
                disabled={!isManual || saving}
              />
            </label>
          </div>
          <div className="button-row wrap">
            <button onClick={() => send({ dewPointDiffOn: onNumber ?? settings.fanOnThresholdC })} disabled={!isManual || saving}>
              Nur EIN-Schwelle speichern
            </button>
            <button onClick={() => send({ dewPointDiffOff: offNumber ?? settings.fanOffThresholdC })} disabled={!isManual || saving}>
              Nur AUS-Schwelle speichern
            </button>
            <button
              onClick={() => send({
                dewPointDiffOn: onNumber ?? settings.fanOnThresholdC,
                dewPointDiffOff: offNumber ?? settings.fanOffThresholdC
              })}
              disabled={!isManual || saving}
            >
              Beide Schwellen speichern
            </button>
          </div>
        </div>

        <div className="control-card wide-card">
          <h3>Display-Zeit</h3>
          <div className="inline-fields">
            <label>
              Manuelle Zeit
              <input
                type="time"
                value={displayTime}
                onChange={(event) => setDisplayTime(event.target.value)}
                disabled={!isManual || saving}
              />
            </label>
            <label>
              Quelle
              <input readOnly value={settings.usePiTime ? 'Raspberry Pi Zeit' : 'Website Zeit'} />
            </label>
          </div>
          <div className="button-row wrap">
            <button onClick={() => send({ displayTime })} disabled={!isManual || saving}>
              Website-Zeit auf Pi setzen
            </button>
            <button
              onClick={() => send(settings.usePiTime ? { usePiTime: false, displayTime } : { usePiTime: true })}
              disabled={saving}
            >
              {settings.usePiTime ? 'Pi-Zeit ausschalten' : 'Pi-Zeit einschalten'}
            </button>
          </div>
          <p className="hint">
            Dieser Pi-Zeit-Schalter ist in Automatik und Manuell verfügbar.
          </p>
        </div>
      </div>
    </section>
  );
}

function SplitMetricSection({
  metric,
  title,
  unit,
  snapshots
}: {
  metric: Exclude<MetricKey, 'dewPointDifference'>;
  title: string;
  unit: string;
  snapshots: DashboardSnapshot[];
}) {
  return (
    <section className="metric-section">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Messwert</p>
          <h2>{title}</h2>
        </div>
      </div>
      <div className="split-grid">
        <ChartCard
          title={`${title} innen`}
          unit={unit}
          data={makeLocationData(snapshots, 'inside', metric)}
          historyHref={`#/history/${metric}/inside`}
        />
        <ChartCard
          title={`${title} außen`}
          unit={unit}
          data={makeLocationData(snapshots, 'outside', metric)}
          historyHref={`#/history/${metric}/outside`}
        />
      </div>
    </section>
  );
}

function ChartCard({
  title,
  unit,
  data,
  historyHref,
  showFanInTooltip = false,
  wide = false
}: {
  title: string;
  unit: string;
  data: ChartDatum[];
  historyHref: string;
  showFanInTooltip?: boolean;
  wide?: boolean;
}) {
  return (
    <article className={wide ? 'chart-card chart-card-wide' : 'chart-card'}>
      <div className="chart-card-header">
        <h3>{title}</h3>
        <a className="small-button" href={historyHref}>Verlauf ansehen</a>
      </div>
      {data.length === 0 ? (
        <div className="empty-chart">Noch keine Messwerte.</div>
      ) : (
        <div className="chart-box">
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={data} margin={{ top: 12, right: 18, left: 0, bottom: 8 }}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis dataKey="label" minTickGap={18} />
              <YAxis unit={unit} width={54} />
              <Tooltip content={<ChartTooltip unit={unit} showFan={showFanInTooltip} />} />
              <Line type="monotone" dataKey="value" dot activeDot={{ r: 6 }} name={title} />
            </LineChart>
          </ResponsiveContainer>
        </div>
      )}
      <p className="chart-hint">X-Achse: Datum und Uhrzeit. Es werden nur die letzten 10 Werte angezeigt.</p>
    </article>
  );
}

function ChartTooltip({ active, payload, unit, showFan }: any) {
  if (!active || !payload?.length) {
    return null;
  }

  const point = payload[0].payload as ChartDatum;
  return (
    <div className="tooltip-card">
      <strong>{formatFullDateTime(point.measuredAt)}</strong>
      <span>Wert: {formatNumber(point.value)} {unit}</span>
      {showFan && <span>Lüfter: {point.fanOn ? 'Ein' : 'Aus'}</span>}
      {point.controlMode && <span>Modus: {point.controlMode}</span>}
      {showFan && point.fanOnThresholdC !== undefined && point.fanOffThresholdC !== undefined && (
        <span>Schwellen: EIN {formatNumber(point.fanOnThresholdC)} / AUS {formatNumber(point.fanOffThresholdC)} °C</span>
      )}
    </div>
  );
}

function HistoryPage({
  metric,
  location,
  apiKeyVersion,
  onApiKeySaved
}: {
  metric: MetricKey;
  location?: LocationKey;
  apiKeyVersion: number;
  onApiKeySaved: () => void;
}) {
  const resolvedLocation = metric === 'dewPointDifference' ? undefined : location ?? 'inside';
  const [health, setHealth] = useState<HealthStatus | null>(null);
  const [rows, setRows] = useState<HistoryRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let alive = true;
    const run = async () => {
      setLoading(true);
      setError(null);
      try {
        const nextHealth = await api.health();
        const nextRows = await api.history(metric, resolvedLocation);
        if (alive) {
          setHealth(nextHealth);
          setRows(nextRows);
        }
      } catch (err) {
        if (alive) {
          setError(err instanceof Error ? err.message : 'Verlauf konnte nicht geladen werden.');
        }
      } finally {
        if (alive) {
          setLoading(false);
        }
      }
    };

    run();
    return () => {
      alive = false;
    };
  }, [metric, resolvedLocation, apiKeyVersion]);

  const title = metric === 'dewPointDifference'
    ? metricLabels[metric]
    : `${metricLabels[metric]} ${resolvedLocation ? locationLabels[resolvedLocation] : ''}`;

  return (
    <>
      <AdminKeyPanel health={health} forceOpen={false} onSaved={onApiKeySaved} />
      <section className="history-page">
        <div className="history-header">
          <div>
            <p className="eyebrow">Verlauf</p>
            <h2>{title}</h2>
            <p className="hint">Auf dieser Seite wird nicht alle 30 Sekunden gepollt.</p>
          </div>
          <a className="small-button primary" href="#/">Zurück nach Hause</a>
        </div>

        {loading && <div className="banner">Lade Verlauf...</div>}
        {error && <div className="banner error">{error}</div>}

        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Datum / Uhrzeit</th>
                {metric !== 'dewPointDifference' && <th>Ort</th>}
                {metric !== 'dewPointDifference' ? <th>Wert</th> : <th>Taupunkt-Differenz</th>}
                {metric === 'dewPointDifference' && <th>Regel-Differenz</th>}
                {metric === 'dewPointDifference' && <th>Manuell</th>}
                <th>Lüfter</th>
                <th>Modus</th>
                <th>EIN-Schwelle</th>
                <th>AUS-Schwelle</th>
                <th>Displayzeit</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row, index) => (
                <tr key={`${row.measuredAt}-${index}`}>
                  <td>{formatFullDateTime(row.measuredAt)}</td>
                  {metric !== 'dewPointDifference' && <td>{row.location ? locationLabels[row.location as LocationKey] : '-'}</td>}
                  {metric !== 'dewPointDifference' ? (
                    <td>{formatNumber(row.value)} {metricUnits[metric]}</td>
                  ) : (
                    <td>{formatNumber(row.dewPointDifferenceC)} °C</td>
                  )}
                  {metric === 'dewPointDifference' && <td>{formatNumber(row.controlDewPointDifferenceC)} °C</td>}
                  {metric === 'dewPointDifference' && <td>{row.manualDewPointDifferenceC == null ? '-' : `${formatNumber(row.manualDewPointDifferenceC)} °C`}</td>}
                  <td>{row.fanOn ? 'Ein' : 'Aus'}</td>
                  <td>{row.controlMode}</td>
                  <td>{formatNumber(row.fanOnThresholdC)} °C</td>
                  <td>{formatNumber(row.fanOffThresholdC)} °C</td>
                  <td>{row.displayTime ? `${row.displayTime} (${row.displayTimeSource ?? '-'})` : '-'}</td>
                </tr>
              ))}
              {!loading && rows.length === 0 && (
                <tr>
                  <td colSpan={10}>Noch keine Verlaufseinträge.</td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </>
  );
}

function AdminKeyPanel({
  health,
  forceOpen,
  onSaved
}: {
  health: HealthStatus | null;
  forceOpen: boolean;
  onSaved: () => void;
}) {
  const [open, setOpen] = useState(forceOpen);
  const [draft, setDraft] = useState(getStoredApiKey());

  useEffect(() => {
    if (forceOpen) {
      setOpen(true);
    }
  }, [forceOpen]);

  if (!health?.apiKeyRequired && !open && !getStoredApiKey()) {
    return null;
  }

  return (
    <section className="admin-key-panel">
      <button className="link-button" onClick={() => setOpen((value) => !value)}>
        {open ? 'API-Key verbergen' : 'API-Key setzen'}
      </button>
      {health?.apiKeyRequired && <span className="key-required">APP_API_KEY ist aktiv.</span>}
      {open && (
        <div className="admin-key-form">
          <input
            type="password"
            value={draft}
            placeholder="APP_API_KEY aus Render"
            onChange={(event) => setDraft(event.target.value)}
          />
          <button
            onClick={() => {
              setStoredApiKey(draft);
              onSaved();
            }}
          >
            Key speichern
          </button>
          <button
            onClick={() => {
              setDraft('');
              setStoredApiKey('');
              onSaved();
            }}
          >
            Key löschen
          </button>
        </div>
      )}
    </section>
  );
}

function StatusCard({ label, value }: { label: string; value: string }) {
  return (
    <article className="status-card">
      <span>{label}</span>
      <strong>{value}</strong>
    </article>
  );
}

function makeLocationData(
  snapshots: DashboardSnapshot[],
  location: LocationKey,
  metric: Exclude<MetricKey, 'dewPointDifference'>
): ChartDatum[] {
  const data: ChartDatum[] = [];

  for (const snapshot of snapshots) {
    const reading = snapshot[location];
    if (!reading) {
      continue;
    }

    const value = metric === 'temperature'
      ? reading.temperature
      : metric === 'humidity'
        ? reading.humidity
        : reading.dewPointC;

    data.push({
      label: formatAxisDateTime(snapshot.measuredAt),
      measuredAt: snapshot.measuredAt,
      value,
      fanOn: snapshot.fanOn,
      controlMode: snapshot.controlMode,
      fanOnThresholdC: snapshot.fanOnThresholdC,
      fanOffThresholdC: snapshot.fanOffThresholdC
    });
  }

  return data;
}

function makeDifferenceData(snapshots: DashboardSnapshot[]): ChartDatum[] {
  return snapshots
    .filter((snapshot) => snapshot.dewPointDifferenceC !== null && snapshot.dewPointDifferenceC !== undefined)
    .map((snapshot) => ({
      label: formatAxisDateTime(snapshot.measuredAt),
      measuredAt: snapshot.measuredAt,
      value: snapshot.dewPointDifferenceC ?? 0,
      fanOn: snapshot.fanOn,
      controlMode: snapshot.controlMode,
      fanOnThresholdC: snapshot.fanOnThresholdC,
      fanOffThresholdC: snapshot.fanOffThresholdC
    }));
}

function parseRoute(): Route {
  const parts = window.location.hash.replace(/^#\/?/, '').split('/').filter(Boolean);
  if (parts[0] === 'history') {
    const metric = normalizeMetric(parts[1]);
    const location = normalizeLocation(parts[2]);
    return { page: 'history', metric, location };
  }

  return { page: 'home' };
}

function normalizeMetric(value?: string): MetricKey {
  if (value === 'humidity' || value === 'dewPoint' || value === 'dewPointDifference') {
    return value;
  }
  return 'temperature';
}

function normalizeLocation(value?: string): LocationKey | undefined {
  return value === 'inside' || value === 'outside' ? value : undefined;
}

function parseInputNumber(value: string): number | null {
  const normalized = value.replace(',', '.');
  const parsed = Number(normalized);
  return Number.isFinite(parsed) ? parsed : null;
}

function formatNumber(value?: number | null): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '-';
  }

  return new Intl.NumberFormat('de-DE', {
    maximumFractionDigits: 2,
    minimumFractionDigits: 0
  }).format(value);
}

function formatAxisDateTime(value: string): string {
  const date = new Date(value);
  return new Intl.DateTimeFormat('de-DE', {
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit'
  }).format(date);
}

function formatFullDateTime(value: string): string {
  const date = new Date(value);
  return new Intl.DateTimeFormat('de-DE', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit'
  }).format(date);
}

function currentTimeForInput(): string {
  return new Date().toLocaleTimeString('de-DE', {
    hour: '2-digit',
    minute: '2-digit',
    hour12: false
  });
}

export default App;
