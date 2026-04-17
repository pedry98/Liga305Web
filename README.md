# Liga305Web

Private FACEIT-style league for a Discord group, Dota 2 only. Steam-linked
accounts, per-season MMR (Glicko-2), 10-player queue, auto-created Dota 2
lobbies via a bot, auto-settled results.

Plan: `C:\Users\pedry\.claude\plans\i-need-to-create-lucky-hartmanis.md`

## Stack

- **Backend**: ASP.NET Core 9 Web API (`src/Liga305.Api`)
- **Domain / Infra**: `src/Liga305.Domain`, `src/Liga305.Infrastructure` (EF Core + Npgsql)
- **Frontend**: Angular 18 SPA (`src/liga305-web`)
- **Bot worker**: Python + ValvePython (`src/bot-worker`) — `dota2`, `steam`, `gevent`, `flask`
- **DB**: PostgreSQL 16 (Neon for dev, self-hosted for prod)
- **Auth**: Steam OpenID

## Quick start (dev)

Prerequisites: .NET 9 SDK, Node 20+, Python 3.11+, a Postgres instance.

### 1. Point the API at a Postgres

**Option A — Neon hosted Postgres (recommended for dev)**

1. Create a free project at <https://neon.tech>.
2. Copy the connection string. It looks like
   `postgresql://user:pass@ep-xxx.region.aws.neon.tech/neondb?sslmode=require`.
3. Save it as a user secret (never committed):

   ```bash
   dotnet user-secrets set "ConnectionStrings:Postgres" \
     "Host=ep-xxx.region.aws.neon.tech;Database=neondb;Username=user;Password=pass;SslMode=Require;Trust Server Certificate=true" \
     --project src/Liga305.Api
   ```

**Option B — Local Postgres via docker-compose**

```bash
cp .env.example .env        # fill in POSTGRES_PASSWORD
docker compose up -d postgres
```

### 2. Run everything

Three terminals:

```bash
# API
cd src/Liga305.Api
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile --urls http://localhost:5080
# → http://localhost:5080/health/db
```

```bash
# Angular SPA
cd src/liga305-web
npm start
# → http://localhost:4200
```

```bash
# Bot worker (one-time setup)
cd src/bot-worker
python -m venv .venv
.venv/Scripts/activate              # macOS/Linux: source .venv/bin/activate
pip install -r requirements.txt
cp .env.example .env                # fill in BOT_STEAM_USERNAME etc.
python -m src.main
# → http://localhost:4100/health
```

The bot worker runs in **simulation mode** without Steam credentials — fine for
exercising the API + UI flow. Set `BOT_STEAM_USERNAME` / `_PASSWORD` /
`_SHARED_SECRET` in its `.env` to flip it to real mode (hosts actual Dota 2
lobbies via ValvePython).

## Repo layout

```
Liga305Web/
├── Liga305.sln
├── src/
│   ├── Liga305.Api/              ASP.NET Core 9 Web API
│   ├── Liga305.Domain/           Entities, Glicko-2
│   ├── Liga305.Infrastructure/   EF Core, Postgres, Steam/OpenDota/BotWorker clients
│   ├── liga305-web/              Angular 18 SPA
│   └── bot-worker/               Python + ValvePython Dota 2 bot
├── docker-compose.yml            Postgres for dev
├── .env.example
└── .gitignore
```

## Bot worker behavior (FACEIT-style)

When a queue pops, the API forms a balanced match (snake-draft by MMR), then
calls the bot worker over HTTP. The bot:

1. Creates a Dota 2 practice lobby (Captain's Mode, no password)
2. Joins the lobby's broadcast channel — doesn't take a team slot
3. Invites every roster player by Steam ID
4. **Auto-kicks** any non-roster player who joins
5. **Kicks roster members off the wrong team** until they pick the side they
   were assigned by the API's snake draft
6. **Auto-launches** the match once all 10 are on their assigned teams
7. **Abandons** the match (destroys the lobby + reports back) if 10 players
   haven't shown up within 4 minutes
8. **Reports the Dota match ID** to the API the moment Valve's GC assigns one,
   which kicks off OpenDota result polling for MMR settlement
