# Running Ultimate POS in production

How to put the API, the frontend and the database on one Linux server, release updates, and back up. Everything here was tested
end to end with Docker (see "Tested" at the bottom).

## What runs where

```
browser ── https://pos.yourcompany.co.ke ──> Caddy (ports 80, 443; gets the HTTPS certificate itself)
                                              ├─ /api/v1/*        -> api  (.NET 8, port 8080, ONE copy)
                                              └─ everything else  -> web  (Next.js, port 3000)
                                             api -> db (PostgreSQL 16, no public port)
```

| File | Purpose |
|---|---|
| `docker-compose.prod.yml` | The five services: `db`, `api`, `web`, `caddy`, and `migrate` (runs on demand). |
| `Caddyfile` | HTTPS and routing. |
| `.env.example` | Every setting, explained. The real `.env` lives only on the server. |
| `pos.sh` | `docker compose` with the right files: `./pos.sh up -d`, `./pos.sh logs -f api`, … |
| `deploy.sh` | One release: backup, pull, build, migrate, restart, check health. |
| `backup.sh` | Database dump, off-server copy, old copies removed. |
| `../src/UltimatePos.Api/Dockerfile` | The API image. `--migrate` makes it update the database and exit. |
| frontend repo `Dockerfile` | The web image. |

Server layout:

```
/opt/ultimatepos/
├── .env          the settings (chmod 600)
├── backend/      this repository
├── frontend/     the frontend repository
└── backups/      local database dumps
```

## 1. Before you start

- A server: Ubuntu 24.04, 4 vCPU / 8 GB RAM / 80 GB disk (e.g. Hetzner CPX31), with backups turned on in the provider's panel.
- A domain whose DNS you control. Create an `A` record, e.g. `pos` -> the server's IP. With Cloudflare, leave it **DNS only**
  (grey cloud) so Caddy can get the certificate.
- Read access to both GitHub repositories from the server (a deploy key per repository, or a fine-grained token).
- For the `.env`: SMTP details from your email provider, and (when approved) Daraja production keys.

## 2. Prepare the server (once)

Connect as root (`ssh root@SERVER_IP`), then:

```bash
adduser deploy
usermod -aG sudo deploy
rsync -a ~/.ssh /home/deploy/ && chown -R deploy:deploy /home/deploy/.ssh

# No root or password logins: in /etc/ssh/sshd_config set
#   PermitRootLogin no
#   PasswordAuthentication no
systemctl restart ssh

apt update && apt -y upgrade
apt -y install unattended-upgrades ufw git rclone curl
ufw allow OpenSSH && ufw allow 80 && ufw allow 443 && ufw --force enable

curl -fsSL https://get.docker.com | sh
usermod -aG docker deploy
```

Log out, and from now on connect as `ssh deploy@SERVER_IP`.

## 3. Get the code and the settings

```bash
sudo mkdir -p /opt/ultimatepos && sudo chown deploy:deploy /opt/ultimatepos
cd /opt/ultimatepos
git clone git@github.com:CalebMUC/UltimatePos_Multi-Tenant_Backend.git backend
git clone git@github.com:CalebMUC/Ultimate_Pos_Multi_Tenant_Frontend.git frontend
chmod +x backend/deploy/*.sh

cp backend/deploy/.env.example .env
chmod 600 .env
nano .env        # fill in every <...>; generate secrets with the commands shown next to them
```

Copy the finished `.env` into your password manager.

## 4. First start

```bash
cd /opt/ultimatepos/backend/deploy
./pos.sh build                 # about 5 minutes the first time
./pos.sh up -d db              # the database first
./pos.sh run --rm migrate      # creates every table and the permission list
./pos.sh up -d                 # api, web, caddy
./pos.sh ps                    # all four "Up"
./pos.sh logs api | tail -20   # "Created super user ...", "Now listening on: http://[::]:8080"
curl https://pos.yourcompany.co.ke/api/v1/health   # {"status":"ok"}
```

Then, in the browser:

1. Open `https://pos.yourcompany.co.ke` and sign in with `Platform__BootstrapSuperUser__Email` / `Password` from `.env`.
2. **Change that password at once** (the bootstrap account is not forced to).
3. Delete the three `Platform__BootstrapSuperUser__*` lines from `.env`, then `./pos.sh up -d` (the account stays).
4. Create a second Super User for a colleague, so nobody is ever locked out.
5. Onboard the first business (Platform -> Register business), with its till or paybill.

## 5. Backups (set up the same day)

1. Create a bucket at Backblaze B2 (or Cloudflare R2), and an application key limited to it.
2. `rclone config` -> new remote, e.g. named `b2`, type Backblaze B2, with that key.
3. In `.env`: `BACKUP_REMOTE=b2:<bucket name>`.
4. Test: `./backup.sh` -> "dumped ..." and "copied to b2:...".
5. Nightly at 02:00 (`crontab -e`):

   ```
   0 2 * * * /opt/ultimatepos/backend/deploy/backup.sh >> /opt/ultimatepos/backups/backup.log 2>&1
   ```

**Restore** (test this before go-live, then monthly, into a scratch database so production is untouched):

```bash
./pos.sh exec -T db sh -c 'createdb -U "$POSTGRES_USER" restore_test'
./pos.sh exec -T db sh -c 'pg_restore -U "$POSTGRES_USER" -d restore_test --no-owner' < /opt/ultimatepos/backups/<file>.dump
./pos.sh exec -T db sh -c 'psql -U "$POSTGRES_USER" -d restore_test -c "SELECT count(*) FROM \"Businesses\""'
./pos.sh exec -T db sh -c 'dropdb -U "$POSTGRES_USER" restore_test'
```

To restore **production** itself (after a disaster): `./pos.sh stop api web`, drop and recreate the `ultimatepos` database the same
way, `pg_restore` into it, then `./pos.sh up -d`.

## 6. Releasing an update

Merge to `main` in both repositories, then on the server, outside shop hours:

```bash
/opt/ultimatepos/backend/deploy/deploy.sh
```

It backs up, pulls both repositories, builds, runs the new migrations, restarts (a few seconds of downtime) and checks
`/api/v1/health`. If the check fails it says so: read `./pos.sh logs --tail 200 api`.

Rolling back code: `git -C /opt/ultimatepos/backend checkout <previous commit>` (and the frontend), `./pos.sh build && ./pos.sh up -d`.
A release whose migration changed data needs the pre-release backup restored as well. `deploy.sh` makes that backup first.

## 7. Day to day

| Task | Command (in `backend/deploy`) |
|---|---|
| Status | `./pos.sh ps` |
| API log | `./pos.sh logs -f api` (Ctrl+C to stop following) |
| Restart the API | `./pos.sh restart api` |
| After editing `.env` | `./pos.sh up -d` (recreates what changed) |
| After changing `SUPPORT_EMAIL` / `SUPPORT_PHONE` | `./pos.sh build web && ./pos.sh up -d` (they are built into the frontend) |
| Database shell | `./pos.sh exec db sh -c 'psql -U "$POSTGRES_USER" "$POSTGRES_DB"'` |
| Disk space | `df -h /` and `docker system df` |

Monitoring: point UptimeRobot (free) at `https://pos.yourcompany.co.ke/api/v1/health` every 5 minutes, alerting by email.

## 8. M-Pesa

- Until Safaricom approves go-live, keep the sandbox values in `.env`. Businesses take M-Pesa by typed code (`MpesaReference`).
- After approval: set `Mpesa__BaseUrl=https://api.safaricom.co.ke` and the production key, secret, short code and passkey, then
  `./pos.sh up -d`.
- Register the C2B URLs once per short code, signed in with `Mpesa.Configure`: `POST /api/v1/mpesa/c2b/register-urls`.
  It registers the `/api/v1/payments/...` URLs from `.env`: Daraja refuses URLs containing "mpesa".
- Send a real small payment to the till and check it appears under unmatched M-Pesa payments.
- **STK push uses the one short code in `.env` for every business**: all STK payments land in that account. Offer STK only to the
  business that owns it until STK is per business.

## 9. Rules

- **One API container.** Its notification workers would send everything twice if two ran.
- Never publish the database port, and never put the real `.env` in git.
- Keep `caddy_data` (certificates) and `pgdata` (the database): `./pos.sh down` is safe, `./pos.sh down -v` **deletes the database**.

## Tested

On 2026-10-21, with this compose file on Docker 29 (Caddy on a local port): a fresh database migrated (14 migrations); the
bootstrap Super User signed in with every platform permission; a business registered with a till, and its owner signed in with
every business permission; `/api/v1/health` 200; Swagger off; a C2B confirmation to the till on the `/payments/` path reached that
business; the caller's real address recorded behind Caddy; `backup.sh` dump restored with identical counts.
