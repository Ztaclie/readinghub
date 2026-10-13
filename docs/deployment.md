# Deployment

Production Compose runs two services: the API and PostgreSQL. PostgreSQL has no published port. The API is published only on `127.0.0.1:5080`. Nginx on the server serves the dashboard over HTTP on port 80 and proxies `/api`, `/openapi`, and `/health` to that address. Cloudflare terminates HTTPS, so visitors still open `https://` the public hostname.

Run one API container. Startup takes an advisory lock and then applies migrations.

## First-time setup

1. Copy the example environment file and replace every value:

```powershell
copy deploy\.env.example deploy\.env
```

`JWT_SIGNING_KEY` must be a random string of at least 32 characters. `READINGHUB_DOMAIN` is the public hostname, used as `https://` plus that name for `Cors__WebOrigin`. `POSTGRES_PASSWORD` is the database password.

2. This server has Docker and no Node.js. Build the dashboard with a one-off container from the repository root:

```powershell
docker run --rm -v "$PWD/apps/web:/src" -w /src node:24-alpine sh -c "npm ci && npm run build"
```

Copy `apps/web/dist` to `/var/www/readinghub`.

3. In `deploy/nginx/readinghub.conf`, set `server_name` to the public hostname before enabling the site. The committed name is `reading.example.com`. The site listens on port 80 only and is not the default server. Enable it only after the configuration test passes, then reload Nginx:

```powershell
nginx -t
```

4. Start the API and database from the repository root:

```powershell
docker compose --env-file deploy/.env -f deploy/docker-compose.yml up -d --build
```

5. Open `https://your-domain` and create the account. Registration closes after that user exists.

6. Point the extension build at the same origin with `READINGHUB_API_ORIGIN` before `npm run build` in `apps/extension`.

Do not publish Postgres on the host. Do not bind ports 80 or 443 from Compose. Data Protection keys live in the `dp_keys` volume so logins survive API restarts.

## Upgrades

Pull the new code, rebuild the dashboard in a one-off container, copy `apps/web/dist` to `/var/www/readinghub` again, and rebuild the containers:

```powershell
docker run --rm -v "$PWD/apps/web:/src" -w /src node:24-alpine sh -c "npm ci && npm run build"
docker compose --env-file deploy/.env -f deploy/docker-compose.yml up -d --build
```

Migrations run when the API starts. Take a database backup first. When `deploy/nginx/readinghub.conf` changed, run `nginx -t` and reload Nginx only if that test passes.

## Logs and health

```powershell
docker compose --env-file deploy/.env -f deploy/docker-compose.yml logs -f readinghub-api
```

`/health/live` answers when the process is up. `/health/ready` also checks PostgreSQL.
