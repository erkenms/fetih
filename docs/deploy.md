# Deploy

NixOS host (`163.5.53.47`) that pulls and builds from git itself.

| Env | Realm | Port | Schema | Source |
|---|---|---|---|---|
| prod | Altay | 5816 | `fetih_game` | latest `v*` tag |
| test | Mihenk | 5826 | `fetih_game_test` | `main` (staff only) |

Shared: `Fetih.Account` (`9960`), MariaDB (Unix socket only), Caddy (`80`, `443`), Loki, Alloy,
Prometheus, Grafana (`/grafana/`, behind web sign-in). Open ports: `22`, `80`, `443` (TCP+UDP),
`9960`, `5816`, `5826`. State lives under `/var/lib/fetih/`.

## How updates deploy

- Push to `main`: within 2 minutes `fetih-deploy` applies NixOS, resets `/var/lib/fetih/studio`
  to `main`, and restarts test (`Mihenk`).
- Push a `v*` tag: `fetih-deploy` builds prod + `Fetih.exe` + low-zoom map assets, runs a
  60s `gm.shutdown` countdown on Altay, then restarts prod.
- Studio on test edits `/var/lib/fetih/studio/src/Fetih.Game/Data` live; `/admin/studio/changes`
  copies a `git apply --3way` command to commit and push.

## Secrets (`deploy/secrets.sh`)

Keep only in iCloud Passwords (never on disk or in git). `deploy/secrets.sh` prompts with hidden
input and streams each secret over SSH into `/var/lib/fetih/secrets/` (`root`, `0400`):

- `r2`: `R2 Access Key ID` and `R2 Secret Access Key` (S3 credentials for `fetih-r2`; endpoint/bucket are in `deploy/nix/r2.nix`)
- `discord`: Discord webhook URL
- `admin`: first admin email and temporary password (change after first login)

Upload the pinned client archives once, then push secrets anytime:

```bash
./deploy/upload-client-base.sh
./deploy/secrets.sh 163.5.53.47        # or r2|discord|admin
```

## Backups and restore

R2 lifecycle rules: expire `backups/hourly/` after 2 days, `backups/daily/` after 14 days.
`fetih-backup-hourly` dumps `fetih_account` + `fetih_game`; `fetih-backup-daily` (04:00) adds
`grafana.db` and `*-audit-*.jsonl`. Restore into a scratch schema to verify:

```bash
rclone copyto r2:fetih/backups/hourly/<file>.sql.gz /tmp/restore.sql.gz
mariadb -e "CREATE DATABASE restore_scratch;"
zcat /tmp/restore.sql.gz | sed 's/`fetih_game`/`restore_scratch`/g; s/`fetih_account`/`restore_scratch`/g' | mariadb restore_scratch
mariadb -e "SHOW TABLES FROM restore_scratch; DROP DATABASE restore_scratch;"
```

## Emergencies

Boot the previous NixOS generation from the provider console, or run
`nixos-rebuild switch --rollback` over SSH. A failed switch leaves the old generation running.
