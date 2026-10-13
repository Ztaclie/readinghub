# ReadingHub

Self-hosted bookmarks and reading progress. One ASP.NET Core API, a React dashboard, PostgreSQL, and a Chrome extension share the same account.

The first person to register becomes the only account. Registration then closes.

## Run it locally

1. Start PostgreSQL:

```powershell
docker compose -f deploy/docker-compose.dev.yml up -d
```

2. Start the API:

```powershell
dotnet run --project apps/api/ReadingHub.Api
```

The API listens on `http://localhost:5080`. Development settings in `appsettings.Development.json` are for this local database only. Do not use that signing key in production.

3. Start the dashboard:

```powershell
cd apps/web
npm install
npm run dev
```

Open `http://localhost:5173`. Vite proxies `/api` to the API.

4. Load the extension from `apps/extension` with `npm install` and `npm run dev`, then choose the WXT Chrome output in `chrome://extensions`.

## Tests

```powershell
dotnet test apps/api/ReadingHub.Api.Tests
cd apps/web; npm test
cd apps/extension; npm test
```

API tests start PostgreSQL with Testcontainers, so Docker needs to be running.

## Deploy

See [docs/deployment.md](docs/deployment.md) and [docs/backup-restore.md](docs/backup-restore.md).

More day-to-day detail is in [docs/development.md](docs/development.md).
