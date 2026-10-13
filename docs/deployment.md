# Deployment

Production Compose runs two services: the API and PostgreSQL. PostgreSQL has no published port. The API is published only on `127.0.0.1:5080`. Nginx on the server serves the dashboard and proxies `/api`, `/openapi`, and `/health` to that address.

Run one API container. Startup takes an advisory lock and then applies migrations.

## First-time setup

1. Copy the example environment file and replace every value:

```powershell
copy deploy\.env.example deploy\.env
```

`JWT_SIGNING_KEY` must be a random string of at least 32 characters. `READINGHUB_DOMAIN` is the public hostname, used as `https://` plus that name for `Cors__WebOrigin`. `POSTGRES_PASSWORD` is the database password.

2. Install Node.js 24 on the server if it is not already installed, then build the dashboard from the repository root:

```powershell
cd apps/web
npm ci
npm run build
```

Copy `apps/web/dist` to the directory named by `root` in `deploy/nginx/readinghub.conf` (`/var/www/readinghub` unless you change it).

3. Edit `deploy/nginx/readinghub.conf` so `server_name` is `READINGHUB_DOMAIN`. Put this hostname's certificate at the `ssl_certificate` paths in that file, then enable the site and reload Nginx. The site listens on ports 80 and 443 for this hostname only. Port 80 redirects to HTTPS.

4. Start the API and database from the repository root:

```powershell
docker compose --env-file deploy/.env -f deploy/docker-compose.yml up -d --build
```

5. Open `https://your-domain` and create the account. Registration closes after that user exists.

6. Point the extension build at the same origin with `READINGHUB_API_ORIGIN` before `npm run build` in `apps/extension`.

Do not publish Postgres on the host. Data Protection keys live in the `dp_keys` volume so logins survive API restarts.

## Upgrades

Pull the new code, rebuild the dashboard, copy `apps/web/dist` into the Nginx root again, and rebuild the containers:

```powershell
cd apps/web
npm ci
npm run build
cd ../..
docker compose --env-file deploy/.env -f deploy/docker-compose.yml up -d --build
```

Migrations run when the API starts. Take a database backup first. Reload Nginx only when `deploy/nginx/readinghub.conf` changed.

## Logs and health

```powershell
docker compose --env-file deploy/.env -f deploy/docker-compose.yml logs -f readinghub-api
```

`/health/live` answers when the process is up. `/health/ready` also checks PostgreSQL.
