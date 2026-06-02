# Open the whole project in JetBrains Rider

The repository contains two parts:

- `backend/` — ASP.NET Core / C# project
- `frontend/` — React + Vite project

## Recommended way

1. Extract the ZIP.
2. Open JetBrains Rider.
3. Choose **File → Open**.
4. Select the extracted project root folder, not the `backend` folder.
5. Open `TaupunktDashboard.sln` from the root.

If Rider shows only the backend in **Solution** view, switch the left project panel to **Files** view. The frontend is a Node/Vite project, so it is not a C# project, but it is still part of the same root folder.

## Backend local run

Start PostgreSQL first:

```bash
docker compose up db
```

Then run the Rider profile **Taupunkt.Api local**.

Backend URL:

```text
http://localhost:8080
```

Local API key:

```text
dev-key
```

## Frontend local run from Rider terminal

Open the Rider terminal in the project root:

```bash
cd frontend
npm install
npm run dev
```

Frontend URL:

```text
http://localhost:5173
```

The Vite dev server proxies `/api` requests to `http://localhost:8080`, so the backend should be running too.

## Easiest full local run

From the project root:

```bash
docker compose up --build
```

Open:

```text
http://localhost:8080
```
