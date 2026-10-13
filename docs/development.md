# Development

ReadingHub is a single repository:

- `apps/api` — ASP.NET Core API and EF Core migrations
- `apps/web` — React dashboard
- `apps/extension` — Chrome extension (Manifest V3, WXT)
- `fixtures` — saved HTML used by extension tests
- `deploy` — Compose files and Caddy

## Database

`deploy/docker-compose.dev.yml` publishes PostgreSQL on `localhost:5432` with user `readinghub`, password `readinghub`, and database `readinghub`. The API applies EF Core migrations on startup and takes a Postgres advisory lock first, so a single instance is the supported setup.

Generate a migration after model changes:

```powershell
$env:ConnectionStrings__Database = "Host=localhost;Port=5432;Database=readinghub;Username=readinghub;Password=readinghub"
dotnet tool run dotnet-ef -- migrations add Name --project apps/api/ReadingHub.Api --output-dir Data/Migrations
```

## API

Configuration comes from environment variables and `appsettings`. Production refuses to start when the JWT signing key still contains `dev-only`.

OpenAPI is served at `/openapi/v1.json`. Health checks are `/health/live` and `/health/ready`.

Cookie sessions are for the dashboard. The extension uses a one-time device code and a rotating refresh token. Content scripts never receive those tokens.

## Web

The dashboard talks to `/api` on the same origin in production. In development, Vite proxies that path to port 5080. Sign-in uses an HttpOnly session cookie plus an `X-CSRF-TOKEN` header from `GET /api/v1/auth/csrf`.

## Extension

The committed manifest key keeps the extension id stable: `ajfgibjobkbgeioiegmcmmglalnbnmnd`.

Required host permission is the API origin (`READINGHUB_API_ORIGIN`, default `http://localhost:5080`). Site access for automatic tracking is an optional `<all_urls>` permission. Saving the current page uses `activeTab` while the popup is open.

Automatic tracking defaults to off in account settings. Royal Road and Webnovel have adapters. Other sites use a generic detector and ask for confirmation when confidence is below 0.6.

Rebuild the extension when the API origin changes:

```powershell
$env:READINGHUB_API_ORIGIN = "https://reading.example.com"
npm run build
```

## What is intentionally out of scope

Reading statistics, new-chapter polling, context menus, offline article storage, and a PWA are not part of this version. Chapter text is never stored.
