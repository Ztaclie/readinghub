# Backup and restore

The data that matters is the PostgreSQL volume `pg_data` and the Data Protection volume `dp_keys`. The dashboard can also export bookmarks and reading history as JSON from Settings. Those files are a convenience copy. They are not a full database backup.

## Backup

Stop writes if you want a perfectly consistent copy, or use `pg_dump` while the database is running:

```powershell
docker compose --env-file deploy/.env -f deploy/docker-compose.yml exec -T readinghub-db pg_dump -U readinghub -d readinghub > readinghub.sql
```

Replace `readinghub` if `POSTGRES_USER` or `POSTGRES_DB` in `deploy/.env` is different.

Copy `readinghub.sql` somewhere that is not the VPS disk you are backing up. Keep the `dp_keys` volume as well if you want existing browser sessions to keep working after a restore:

```powershell
docker run --rm -v readinghub_dp_keys:/keys -v ${PWD}:/backup alpine tar czf /backup/dp-keys.tgz -C /keys .
```

The volume name is prefixed by the Compose project name. Check `docker volume ls` if the name differs.

## Restore

1. Stop the API so nothing writes during the restore:

```powershell
docker compose --env-file deploy/.env -f deploy/docker-compose.yml stop readinghub-api caddy
```

2. Restore the database into the running Postgres container:

```powershell
Get-Content readinghub.sql | docker compose --env-file deploy/.env -f deploy/docker-compose.yml exec -T readinghub-db psql -U readinghub -d readinghub
```

For an empty database, drop and recreate it before piping the dump. `pg_dump` output from the command above is a plain SQL script.

3. Restore Data Protection keys only when you want old session cookies to remain valid. Otherwise skip this and everyone signs in again.

```powershell
docker run --rm -v readinghub_dp_keys:/keys -v ${PWD}:/backup alpine tar xzf /backup/dp-keys.tgz -C /keys
```

4. Start the stack again:

```powershell
docker compose --env-file deploy/.env -f deploy/docker-compose.yml up -d
```

Confirm `/health/ready` before using the site.
