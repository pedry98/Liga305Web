# Deploying Liga305Web to a VPS

Target: one Hetzner CX22 (or equivalent) with Docker. All four processes
(Caddy, API, SPA, bot worker) run in containers behind Caddy's auto-HTTPS.
Database stays on Neon (managed Postgres, free tier).

Cost target: **~$6/mo** (VPS) + domain.

## 1. Provision the VPS

1. Sign up at <https://www.hetzner.com/cloud>. Pick CX22 (shared vCPU x86,
   4 GB RAM, 40 GB SSD). Datacenter close to your user base (US East for NA
   Discord groups, Falkenstein for EU).
2. Choose **Ubuntu 24.04** image.
3. Add your SSH public key.
4. Create the server. Note the public IPv4.

## 2. Point DNS at the VPS

You need **two A records** for this single-domain-per-service setup:

```
liga305           A   <VPS-IP>
liga305-api       A   <VPS-IP>
```

(Replace `liga305` with whatever subdomain prefix you want. If you want the
site at the apex of a domain, use `liga305.yourdomain.com` and
`api.liga305.yourdomain.com` instead.)

Wait 1–5 minutes for DNS to propagate (`dig <domain>` from your laptop).

## 3. Install Docker on the VPS

SSH in:

```bash
ssh root@<VPS-IP>
```

Install Docker (one-liner from docker.com):

```bash
curl -fsSL https://get.docker.com | sh
systemctl enable --now docker
```

Create a non-root deploy user (optional but recommended):

```bash
adduser --gecos '' --disabled-password liga305
usermod -aG docker liga305
mkdir -p /home/liga305/.ssh
cp /root/.ssh/authorized_keys /home/liga305/.ssh/
chown -R liga305:liga305 /home/liga305/.ssh
chmod 700 /home/liga305/.ssh && chmod 600 /home/liga305/.ssh/authorized_keys
```

Then `ssh liga305@<VPS-IP>` for subsequent work.

## 4. Open firewall

```bash
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw allow 443/udp    # Caddy uses HTTP/3 over QUIC
ufw --force enable
```

## 5. Pull the repo

```bash
cd ~
git clone https://github.com/<your-user>/Liga305Web.git
cd Liga305Web
```

(If you haven't pushed to GitHub yet, `scp -r` the project from your laptop.)

## 6. Create `deploy/.env`

```bash
cp deploy/.env.production.example deploy/.env
nano deploy/.env
```

Fill in:

- `SITE_DOMAIN` and `API_DOMAIN` (the two A records you created)
- `POSTGRES_CONN` — your Neon connection string in Npgsql format
- `STEAM_WEB_API_KEY` — from <https://steamcommunity.com/dev/apikey>
- `WORKER_SHARED_SECRET` — any long random string (`openssl rand -hex 32`)
- `BOT_STEAM_USERNAME` / `_PASSWORD` / `_SHARED_SECRET` — the bot Steam account's credentials (see `src/bot-worker/README.md`)
- Optionally tune `DOTA_SERVER_REGION` and `DOTA_GAME_MODE`

## 7. First boot

```bash
cd ~/Liga305Web
docker compose --env-file deploy/.env -f deploy/docker-compose.prod.yml up -d --build
```

Watch the first boot:

```bash
docker compose --env-file deploy/.env -f deploy/docker-compose.prod.yml logs -f
```

Caddy will take ~30 seconds to fetch HTTPS certs for both domains.
The bot worker takes another ~10 seconds to log into Steam.

Smoke tests:

```bash
curl https://$SITE_DOMAIN                      # Angular SPA index
curl https://$API_DOMAIN/health                # {"status":"ok"}
curl https://$API_DOMAIN/health/db             # confirms Postgres connectivity
docker exec liga305-bot-worker-1 \
  curl -s http://localhost:4100/health         # {"botConnected":true}
```

## 8. First sign-in

Open `https://$SITE_DOMAIN` in a browser. Click **Sign in with Steam**. First
user to sign in is auto-promoted to admin (see `SteamAuthController` +
`DatabaseSeeder.EnsureBootstrapAdminAsync`).

## 9. Updates

```bash
cd ~/Liga305Web
git pull
docker compose --env-file deploy/.env -f deploy/docker-compose.prod.yml up -d --build
```

For zero-downtime-ish rolling: `docker compose ... up -d --build api` (or `web`
or `bot-worker`) — Caddy keeps routing to the old container until the new one
is healthy.

## 10. Backups

The DB is on Neon, which handles PITR and point-in-time restore for free.
The only local state on the VPS is:

- `avatars` Docker volume (user-uploaded profile images)
- `caddy-data` (Let's Encrypt certs — will re-issue if lost)

Optional: periodic `docker run --rm -v liga305_avatars:/src -v $PWD:/dst alpine tar czf /dst/avatars.tgz -C /src .` to an offsite store.

## Troubleshooting

- **Cert request fails**: check both domains resolve to the VPS IP before
  starting Caddy. Caddy retries every ~15 min.
- **Steam login keeps failing**: `docker logs liga305-bot-worker-1`. If Valve
  is sending a fresh Steam Guard code to the bot account's email, the
  `shared_secret` is wrong or the mobile authenticator was reset.
- **API can't reach bot worker**: they share the `default` compose network.
  `docker exec liga305-api-1 curl http://bot-worker:4100/health` from inside.
- **SPA can't reach API**: open the browser devtools Network tab. If requests
  are going to `localhost:5080`, the build arg didn't apply — rebuild `web`.
