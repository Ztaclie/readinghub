# Deployment

Production Compose runs three services: Caddy, the API, and PostgreSQL. Caddy serves the built dashboard and reverse-proxies `/api`, `/openapi`, and `/health` to the API. PostgreSQL has no published port.

Run one API container. Startup takes an advisory lock and then applies migrations.

## First-time setup

1. Copy the example environment file and replace every value:

```powershell
copy deploy\.env.example deploy\.env
```

`JWT_SIGNING_KEY` must be a random string of at least 32 characters. `READINGHUB_DOMAIN` must be a hostname that points at this server so Caddy can obtain a certificate. `POSTGRES_PASSWORD` is the database password.

2. Start the stack from the repository root:

```powershell
docker compose --env-file deploy/.env -f deploy/docker-compose.yml up -d --build
```

3. Open `https://your-domain` and create the account. Registration closes after that user exists.

4. Point the extension build at the same origin with `READINGHUB_API_ORIGIN` before `npm run build` in `apps/extension`.

The API is not published to the host. Do not add a public Postgres port. Data Protection keys live in the `dp_keys` volume so logins survive API restarts.

## Upgrades

Pull the new code and rebuild:

```powershell
docker compose --env-file deploy/.env -f deploy/docker-compose.yml up -d --build
```

Migrations run when the API starts. Take a database backup first.

## Logs and health

```powershell
docker compose --env-file deploy/.env -f deploy/docker-compose.yml logs -f readinghub-api
```

`/health/live` answers when the process is up. `/health/ready` also checks PostgreSQL.
