# Raspberry Pi configuration

The Python controller is intentionally not included in this web project.
Keep `taupunktMitLuefterAdvanced.py` on the Raspberry Pi.

After deploying the backend/frontend to Render, set these constants in the Python file on the Raspberry Pi:

```python
MEASUREMENTS_API_URL = "https://YOUR-SERVICE.onrender.com/api/measurements"
CONTROL_API_URL = "https://YOUR-SERVICE.onrender.com/api/control"
STATUS_API_URL = "https://YOUR-SERVICE.onrender.com/api/status/health"
API_KEY = "same value as APP_API_KEY on Render"
```

For local development with `docker compose up --build`:

```python
MEASUREMENTS_API_URL = "http://YOUR-COMPUTER-LAN-IP:8080/api/measurements"
CONTROL_API_URL = "http://YOUR-COMPUTER-LAN-IP:8080/api/control"
STATUS_API_URL = "http://YOUR-COMPUTER-LAN-IP:8080/api/status/health"
API_KEY = "dev-key"
```

Do not use `localhost` on the Raspberry Pi unless the backend is running on the Raspberry Pi itself.
`localhost` from the Pi means the Pi, not your laptop/PC.

The backend expects the same field names that the Python script sends and polls:

- measurement upload: `deviceId`, `measurementLocation`, `temperature`, `humidity`, `measuredAt`, `dewPointC`, `dewPointDifferenceC`, `controlDewPointDifferenceC`, `manualDewPointDifferenceC`, `fanOnThresholdC`, `fanOffThresholdC`, `displayTime`, `displayTimeSource`, `fanOn`, `controlMode`
- control polling: `mode`, `manualDewPointDifferenceC`, `fanOnThresholdC`, `fanOffThresholdC`, `dewPointDiffOn`, `dewPointDiffOff`, `displayTime`, `usePiTime`
