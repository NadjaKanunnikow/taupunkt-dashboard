# Taupunkt Dashboard for Raspberry Pi / Joy-Pi

Full-stack web project for the Raspberry Pi `taupunktMitLuefterAdvanced.py` controller.

The Python controller is **not included** in this repository. It belongs on the Raspberry Pi. This project contains only the website/backend/database/deployment code:

- `backend/` — ASP.NET Core / C# Minimal API
- `frontend/` — React + Vite + Recharts
- PostgreSQL database support
- Docker and Render deployment files
- `TaupunktDashboard.sln` for JetBrains Rider

## Open in JetBrains Rider

Open the project root folder or `TaupunktDashboard.sln`, not only `backend/Taupunkt.Api.csproj`.

Detailed Rider steps are in:

```text
docs/RIDER.md
```

## What the website does

The home page polls the backend every 30 seconds and shows only the latest 10 snapshots:

1. Temperature: inside chart on the left, outside chart on the right.
2. Humidity: inside chart on the left, outside chart on the right.
3. Dew point: inside chart on the left, outside chart on the right.
4. Taupunkt / dew-point difference: one full-width chart.

Every chart has `Verlauf ansehen`. The history page shows all old records in a table and does not poll new Raspberry Pi data. `Zurück nach Hause` returns to the live page, which fetches the newest records immediately.

The taupunkt-difference tooltip shows whether the fan was on or off for the hovered point.

## Control logic

- manual → automatic keeps `fanOnThresholdC`, `fanOffThresholdC` and display time.
- manual → automatic does not keep the manual taupunkt difference for control.
- automatic mode uses the real sensor `dewPointDifferenceC` again.
- manual mode can change manual taupunkt difference, display time, ON threshold, OFF threshold, or both thresholds together.
- Pi-time toggle is available in automatic and manual mode.
- Python still sends measurements in manual mode.

## Python endpoints

After Render deployment, set these constants in the Python file on the Raspberry Pi:

```python
MEASUREMENTS_API_URL = "https://YOUR-SERVICE.onrender.com/api/measurements"
CONTROL_API_URL = "https://YOUR-SERVICE.onrender.com/api/control"
STATUS_API_URL = "https://YOUR-SERVICE.onrender.com/api/status/health"
API_KEY = "same value as Render APP_API_KEY"
```

More details:

```text
docs/RASPBERRY_PI_CONFIGURATION.md
```

## API endpoints

- `POST /api/measurements` for Raspberry Pi measurement uploads.
- `GET /api/control` for Python control polling.
- `PUT /api/control` and `PATCH /api/control` for website control updates.
- `GET /api/status/health` for the Python touch status and Render health check.
- `GET /api/dashboard/latest?take=10` for live charts.
- `GET /api/history/{metric}?location=inside|outside` for history tables.

## Local run with Docker

```bash
docker compose up --build
```

Open:

```text
http://localhost:8080
```

Local API key:

```text
dev-key
```

Paste it into the website API key field.

## Local development in Rider

Start PostgreSQL:

```bash
docker compose up db
```

Run backend profile in Rider:

```text
Taupunkt.Api local
```

Run frontend in Rider terminal:

```bash
cd frontend
npm install
npm run dev
```

Open:

```text
http://localhost:5173
```

## Render deployment

1. Push this folder to GitHub/GitLab/Bitbucket.
2. Create a new Render Blueprint from this repo, or manually create a Docker Web Service and Render Postgres database.
3. `render.yaml` creates one Docker service and one database.
4. Copy `APP_API_KEY` from the Render service environment.
5. Paste the same key into the website API-key field and into Python `API_KEY` on the Raspberry Pi.
6. Put the Render URL into the three Python endpoint constants on the Raspberry Pi.

The backend creates its tables automatically on startup:

- `measurements`
- `control_settings`
