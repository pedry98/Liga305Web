"""Flask HTTP server that mirrors the Node bot worker's API surface."""
from __future__ import annotations

# IMPORTANT: monkey-patch must come before importing flask/requests/etc.
import gevent.monkey
gevent.monkey.patch_all()

import logging
import os
from typing import Any

from dotenv import load_dotenv
from flask import Flask, jsonify, request

from .bot import BotConfig, DotaBot, RosterPlayer

load_dotenv()

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(name)s] %(message)s",
    datefmt="%H:%M:%S",
)

PORT = int(os.environ.get("PORT", "4100"))
SHARED_SECRET = os.environ.get("WORKER_SHARED_SECRET", "dev-only-not-secure")

bot = DotaBot(BotConfig(
    steam_username=os.environ.get("BOT_STEAM_USERNAME") or None,
    steam_password=os.environ.get("BOT_STEAM_PASSWORD") or None,
    steam_shared_secret=os.environ.get("BOT_STEAM_SHARED_SECRET") or None,
    server_region=int(os.environ.get("DOTA_SERVER_REGION", "2")),
    game_mode=int(os.environ.get("DOTA_GAME_MODE", "1")),
    cm_pick=int(os.environ.get("DOTA_CM_PICK", "0")),
    api_base_url=os.environ.get("API_BASE_URL", "http://localhost:5080"),
    worker_shared_secret=SHARED_SECRET,
    abandon_timeout_sec=int(os.environ.get("ABANDON_TIMEOUT_SEC", "240")),
))

bot.start_in_background()

app = Flask(__name__)


@app.before_request
def require_secret() -> Any:
    if request.path == "/health":
        return None
    if request.headers.get("x-worker-secret") != SHARED_SECRET:
        return jsonify({"error": "unauthorized"}), 401
    return None


@app.get("/health")
def health() -> Any:
    return jsonify({
        "status": "ok",
        "service": "liga305-bot-worker-py",
        "simulated": bot.simulated,
        "botConnected": bot.ready,
        "abandonTimeoutSec": bot.cfg.abandon_timeout_sec,
    })


@app.post("/lobbies")
def create_lobby() -> Any:
    body = request.get_json(force=True)
    match_id = body["matchId"]
    players = [
        RosterPlayer(steam_id_64=str(p["steamId64"]), team=p["team"])
        for p in body["players"]
    ]
    try:
        result = bot.create_lobby(match_id, players)
        return jsonify(result)
    except Exception as e:
        logging.exception("/lobbies failed")
        return jsonify({"error": str(e)}), 500


@app.post("/lobbies/<match_id>/invite")
def resend_invite(match_id: str) -> Any:
    invited = bot.resend_invites(match_id)
    if invited is None:
        return jsonify({"error": "unknown_match_or_bot_not_ready"}), 404
    return jsonify({"invited": invited})


@app.post("/lobbies/<match_id>/launch")
def launch(match_id: str) -> Any:
    result = bot.launch_match(match_id)
    if result == "unknown":
        return jsonify({"error": "unknown_match"}), 404
    if result == "not_ready":
        return jsonify({"error": "bot_not_ready"}), 503
    return jsonify({"launched": True})


@app.post("/lobbies/<match_id>/cancel")
def cancel(match_id: str) -> Any:
    ok = bot.cancel_lobby(match_id)
    if not ok:
        return jsonify({"error": "unknown_match"}), 404
    return jsonify({"cancelled": True})


def main() -> None:
    from gevent.pywsgi import WSGIServer
    print(f"[bot-worker-py] listening on http://localhost:{PORT}")
    WSGIServer(("0.0.0.0", PORT), app, log=None).serve_forever()


if __name__ == "__main__":
    main()
