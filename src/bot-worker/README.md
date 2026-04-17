# Liga305 Bot Worker (Python)

ValvePython-based Dota 2 lobby bot. Replaces the previous Node.js worker
because `node-dota2` doesn't reliably deliver lobby member updates from the
Game Coordinator, which makes auto-kick / auto-launch impossible.

## Why Python

`ValvePython/dota2` and `ValvePython/steam` are actively maintained, properly
handle GC cache subscription deltas (members list works), and are the de facto
choice for tournament/league bots. Switching the worker to Python keeps the
same HTTP surface, so the .NET API doesn't change.

## Running locally

Prereq: Python 3.11+ on PATH.

```bash
cd src/bot-worker-py
python -m venv .venv
.venv/Scripts/activate            # Windows
# source .venv/bin/activate         # macOS / Linux
pip install -r requirements.txt

cp .env.example .env              # then fill in BOT_STEAM_USERNAME etc.
python -m src.main
```

You should see:
```
[bot-worker-py] listening on http://localhost:4100
[bot] Steam logged on as <username> (76561198...)
[bot] Dota 2 Game Coordinator ready
```

## HTTP API

| Method | Path                              | Purpose                                       |
|--------|-----------------------------------|-----------------------------------------------|
| GET    | `/health`                         | Status + bot connection state                  |
| POST   | `/lobbies`                        | Create lobby for a match                       |
| POST   | `/lobbies/:matchId/invite`        | Re-send invites                                |
| POST   | `/lobbies/:matchId/launch`        | Launch the match (auto-fires when teams ready) |
| POST   | `/lobbies/:matchId/cancel`        | Destroy the lobby                              |

All endpoints except `/health` require `x-worker-secret: <WORKER_SHARED_SECRET>`.

## Behavior (FACEIT-style)

When a lobby is created, the bot:

1. Joins the lobby's broadcast channel (no team slot occupied)
2. Invites every roster player by Steam ID
3. Watches lobby state changes via the GC:
   - **Non-roster joiners** → kicked from the lobby immediately
   - **Roster member on the wrong team** → kicked off team (sent back to
     unassigned). They have to re-pick the correct team from the website.
   - **All 10 on correct teams** → auto-launches the match
4. After 4 minutes (configurable via `ABANDON_TIMEOUT_SEC`), destroys the lobby
   and reports the match as abandoned to the API.
5. When the GC assigns a `match_id` (right before the game starts), PATCHes it
   to the API at `/internal/matches/:id/dota-match-id`.

Lobby is created with no password and `visibility=Public` so it's findable in
the Browse list, but bot enforcement makes the password redundant.
