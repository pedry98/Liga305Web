"""Dota 2 lobby bot wrapping ValvePython's dota2 / steam clients.

Runs on its own gevent event loop in a background thread so the Flask HTTP
server can call into it from request handlers. All public methods are safe to
call from non-bot threads — they enqueue actions that execute inside the
gevent loop.
"""
from __future__ import annotations

import logging
import os
import threading
import time
from dataclasses import dataclass, field
from typing import Callable, Optional

import gevent
import gevent.event
import requests
from dota2.client import Dota2Client
from dota2.enums import DOTA_GC_TEAM
from steam.client import SteamClient
from steam.guard import SteamAuthenticator

# DOTALobbyVisibility enum values (avoid importing — name varies across lib versions).
LOBBY_VISIBILITY_PUBLIC = 0  # also: Friends=1, Unlisted=2

# Team enum integer constants — DOTA_GC_TEAM doesn't always expose these as
# importable attributes across library versions, so we keep the raw values too.
TEAM_GOOD_GUYS = int(DOTA_GC_TEAM.GOOD_GUYS)
TEAM_BAD_GUYS = int(DOTA_GC_TEAM.BAD_GUYS)
TEAM_BROADCASTER = 2
TEAM_SPECTATOR = 3
TEAM_PLAYER_POOL = 4

log = logging.getLogger("bot")

# Steam offset for converting SteamID64 ↔ AccountID32.
_STEAM_ID_BASE = 76561197960265728


def steam_id_to_account_id(steam_id_64: str) -> int:
    try:
        return int(steam_id_64) - _STEAM_ID_BASE
    except (TypeError, ValueError):
        return 0


@dataclass
class RosterPlayer:
    steam_id_64: str
    team: str  # 'Radiant' | 'Dire'

    @property
    def account_id(self) -> int:
        return steam_id_to_account_id(self.steam_id_64)

    @property
    def expected_team(self) -> int:
        return DOTA_GC_TEAM.GOOD_GUYS if self.team == "Radiant" else DOTA_GC_TEAM.BAD_GUYS


@dataclass
class ActiveMatch:
    match_id: str
    players: list[RosterPlayer]
    password: str
    abandon_timer: Optional[gevent.Greenlet] = None
    launched: bool = False
    cancelled: bool = False
    match_id_reported: bool = False
    # account_ids we've already kicked-from-team for being on the wrong side, so we don't
    # spam kicks while their client catches up.
    recently_team_kicked: dict[int, float] = field(default_factory=dict)


@dataclass
class BotConfig:
    steam_username: Optional[str]
    steam_password: Optional[str]
    steam_shared_secret: Optional[str]
    server_region: int
    game_mode: int
    api_base_url: str
    worker_shared_secret: str
    abandon_timeout_sec: int = 240


class DotaBot:
    def __init__(self, cfg: BotConfig):
        self.cfg = cfg
        self.simulated = not (cfg.steam_username and cfg.steam_password)
        self.ready = False
        self._steam: Optional[SteamClient] = None
        self._dota: Optional[Dota2Client] = None
        self._matches: dict[str, ActiveMatch] = {}
        self._current_match_id: Optional[str] = None
        self._lock = threading.Lock()
        self._started = gevent.event.Event()

    # ---------- lifecycle ----------

    def start_in_background(self) -> None:
        """Boot the gevent event loop in a background daemon thread."""
        if self.simulated:
            log.warning("simulation mode (no BOT_STEAM_USERNAME/PASSWORD) — lobbies will be faked")
            self._started.set()
            return

        thread = threading.Thread(target=self._run, name="dota-bot", daemon=True)
        thread.start()

    def _run(self) -> None:
        self._steam = SteamClient()
        self._dota = Dota2Client(self._steam)

        @self._steam.on("logged_on")
        def _on_logged_on():
            log.info("Steam logged on as %s (%s)", self.cfg.steam_username, self._steam.steam_id)
            self._dota.launch()

        @self._steam.on("disconnected")
        def _on_disconnected():
            log.warning("Steam disconnected — attempting reconnect")
            self.ready = False

        @self._dota.on("ready")
        def _on_dota_ready():
            log.info("Dota 2 Game Coordinator ready")
            self.ready = True
            self._started.set()

        @self._dota.on("notready")
        def _on_dota_not_ready():
            log.warning("Dota 2 Game Coordinator unready")
            self.ready = False

        # The library emits `lobby_new` / `lobby_changed` with the FULL lobby state
        # (members included). This is the part that didn't work in node-dota2.
        def _debug(name):
            def handler(message):
                members = list(getattr(message, "all_members", []) or [])
                log.info("EVENT %s: %d all_members, current_match=%s",
                         name, len(members), self._current_match_id)
                for m in members:
                    log.info("  member id=%s team=%s slot=%s name=%s",
                             getattr(m, "id", "?"), getattr(m, "team", "?"),
                             getattr(m, "slot", "?"), getattr(m, "name", "?"))
                if name == "lobby_removed":
                    self._on_lobby_removed(message)
                else:
                    self._on_lobby_event(message)
            return handler

        self._dota.on("lobby_new", _debug("lobby_new"))
        self._dota.on("lobby_changed", _debug("lobby_changed"))
        self._dota.on("lobby_removed", _debug("lobby_removed"))

        two_factor_code = None
        if self.cfg.steam_shared_secret:
            two_factor_code = SteamAuthenticator(
                {"shared_secret": self.cfg.steam_shared_secret}
            ).get_code()

        log.info("connecting to Steam...")
        result = self._steam.login(
            username=self.cfg.steam_username,
            password=self.cfg.steam_password,
            two_factor_code=two_factor_code,
        )
        log.info("login result: %s", result)
        # Block here forever — gevent loop runs handlers.
        self._steam.run_forever()

    def wait_started(self, timeout: float = 30.0) -> None:
        self._started.wait(timeout)

    # ---------- public API (called from Flask request handlers) ----------

    def create_lobby(self, match_id: str, players: list[RosterPlayer]) -> dict:
        lobby_name = f"liga305-{match_id[:8]}"
        # No password — bot enforces roster via auto-kick on lobby_changed events.
        password = ""

        if self.simulated:
            log.info("sim: created lobby '%s' for match %s", lobby_name, match_id)
            return {
                "lobbyName": lobby_name,
                "password": password,
                "botSteamName": "liga305-sim-bot",
                "simulated": True,
            }

        if not self.ready or not self._dota:
            raise RuntimeError("Dota Game Coordinator not ready")

        # Schedule the actual GC work on the gevent loop and wait for it.
        result_event: gevent.event.AsyncResult = gevent.event.AsyncResult()
        self._dota.sleep(0)  # ensure we're hooked into the loop

        def do_create():
            try:
                # Destroy any prior lobby cleanly.
                if self._dota.lobby:
                    self._dota.destroy_lobby()
                    gevent.sleep(0.5)

                self._dota.create_practice_lobby(
                    password="",
                    options={
                        "game_name": lobby_name,
                        "server_region": self.cfg.server_region,
                        "game_mode": self.cfg.game_mode,
                        "allow_cheats": False,
                        "fill_with_bots": False,
                        "allow_spectating": True,
                        "leagueid": 0,
                        "visibility": LOBBY_VISIBILITY_PUBLIC,
                    },
                )
                # The GC parks the lobby creator on Radiant slot 0 by default.
                # Move the bot OUT of the team into the broadcaster channel so it
                # doesn't occupy a player slot. If the GC is slow, the per-event
                # handler will re-issue the move on the next lobby_changed.
                gevent.sleep(1.0)
                self._move_bot_off_team()

                with self._lock:
                    am = ActiveMatch(match_id=match_id, players=players, password="")
                    am.abandon_timer = gevent.spawn_later(
                        self.cfg.abandon_timeout_sec, self._abandon_by_timeout, match_id
                    )
                    self._matches[match_id] = am
                    self._current_match_id = match_id
                log.info("lobby '%s' created for match %s; abandon timer = %ds",
                         lobby_name, match_id, self.cfg.abandon_timeout_sec)

                # Send invites
                for p in players:
                    try:
                        self._dota.invite_to_lobby(int(p.steam_id_64))
                    except Exception:
                        log.exception("invite to %s failed", p.steam_id_64)

                result_event.set({
                    "lobbyName": lobby_name,
                    "password": "",
                    "botSteamName": self.cfg.steam_username or "liga305-bot",
                    "simulated": False,
                })
            except Exception as e:
                log.exception("create_lobby failed")
                result_event.set_exception(e)

        gevent.spawn(do_create)
        return result_event.get(timeout=15)

    def cancel_lobby(self, match_id: str) -> bool:
        if self.simulated:
            self._matches.pop(match_id, None)
            return True
        with self._lock:
            am = self._matches.get(match_id)
            if not am:
                return False
            am.cancelled = True
            if am.abandon_timer:
                am.abandon_timer.kill()
        gevent.spawn(lambda: self._dota and self._dota.destroy_lobby())
        log.info("destroyed lobby for match %s", match_id)
        with self._lock:
            self._matches.pop(match_id, None)
            if self._current_match_id == match_id:
                self._current_match_id = None
        return True

    def launch_match(self, match_id: str) -> str:
        """Returns 'ok', 'unknown', or 'not_ready'."""
        if self.simulated:
            return "ok"
        with self._lock:
            am = self._matches.get(match_id)
        if not am:
            return "unknown"
        if not self.ready or not self._dota:
            return "not_ready"
        if am.launched:
            return "ok"
        am.launched = True
        if am.abandon_timer:
            am.abandon_timer.kill()
        gevent.spawn(lambda: self._dota.launch_practice_lobby())
        log.info("manual launch requested for match %s", match_id)
        return "ok"

    def resend_invites(self, match_id: str) -> Optional[int]:
        if self.simulated:
            return 0
        with self._lock:
            am = self._matches.get(match_id)
        if not am or not self.ready or not self._dota:
            return None

        def do_invites():
            for p in am.players:
                try:
                    self._dota.invite_to_lobby(int(p.steam_id_64))
                except Exception:
                    log.exception("invite to %s failed", p.steam_id_64)

        gevent.spawn(do_invites)
        return len(am.players)

    def _move_bot_off_team(self) -> None:
        """Park the bot in the broadcaster channel so it never holds a Radiant/Dire slot.

        The GC drops the lobby creator into Radiant slot 0 on creation. We can't
        ask the GC to "leave team", but we can:
          1. Move ourselves into the player pool (no team) via join_practice_lobby_team
             with team=PLAYER_POOL — clears the Radiant slot.
          2. Then join the broadcaster channel so we still appear in the lobby
             (clients show us above the Radiant team).
        """
        if not self._dota:
            return
        try:
            # Step 1: clear our team slot. Some library versions only accept
            # the GOOD_GUYS/BAD_GUYS values for team, so wrap each call.
            try:
                self._dota.join_practice_lobby_team(slot=1, team=TEAM_PLAYER_POOL)
            except Exception:
                log.exception("join_practice_lobby_team(PLAYER_POOL) failed; trying broadcast directly")
            gevent.sleep(0.3)
            # Step 2: actually become a broadcaster so we still see lobby state.
            self._dota.join_practice_lobby_broadcast_channel(1)
        except Exception:
            log.exception("_move_bot_off_team failed")

    # ---------- lobby event handlers (FACEIT-style enforcement) ----------

    def _on_lobby_event(self, lobby) -> None:
        """Called on every CSODOTALobby change. Enforces roster + auto-launches."""
        if not self._current_match_id:
            return
        with self._lock:
            am = self._matches.get(self._current_match_id)
        if not am or am.cancelled:
            return

        members = list(getattr(lobby, "all_members", []) or [])
        if not members:
            return

        bot_steam_id = int(self._steam.steam_id) if self._steam and self._steam.steam_id else 0
        roster_steam_ids = {int(p.steam_id_64) for p in am.players}
        expected_team_by_steam_id: dict[int, int] = {int(p.steam_id_64): p.expected_team for p in am.players}

        # 0. If the bot is sitting in a player team slot (Radiant/Dire), evict
        #    itself to broadcaster so it doesn't take up a roster spot.
        for m in members:
            if int(getattr(m, "id", 0) or 0) == bot_steam_id:
                bot_team = int(getattr(m, "team", -1))
                if bot_team in (TEAM_GOOD_GUYS, TEAM_BAD_GUYS):
                    log.info("bot is on team=%d (player slot) — moving to broadcaster", bot_team)
                    gevent.spawn(self._move_bot_off_team)
                break

        # 1. Kick anyone not on the roster (ignoring the bot itself).
        for m in members:
            steam_id = int(getattr(m, "id", 0) or 0)
            if steam_id <= 0 or steam_id == bot_steam_id:
                continue
            if steam_id not in roster_steam_ids:
                acct = steam_id_to_account_id(str(steam_id))
                log.info("kicking non-roster steam_id=%d (account=%d)", steam_id, acct)
                try:
                    self._dota.practice_lobby_kick(acct)
                except Exception:
                    log.exception("kick failed for steam_id=%d", steam_id)
                continue

            # 2. Roster member on wrong team → kick them off team.
            actual_team = int(getattr(m, "team", -1))
            if actual_team in (DOTA_GC_TEAM.GOOD_GUYS, DOTA_GC_TEAM.BAD_GUYS):
                expected = expected_team_by_steam_id.get(steam_id)
                if expected is not None and actual_team != expected:
                    last = am.recently_team_kicked.get(steam_id, 0.0)
                    if time.monotonic() - last > 5.0:
                        acct = steam_id_to_account_id(str(steam_id))
                        log.info("kicking %d off wrong team (was=%d expected=%d, account=%d)",
                                 steam_id, actual_team, expected, acct)
                        am.recently_team_kicked[steam_id] = time.monotonic()
                        try:
                            self._dota.practice_lobby_kick_from_team(acct)
                        except Exception:
                            log.exception("kick-from-team failed for steam_id=%d", steam_id)

        # 3. Auto-launch when all 10 roster players are on their assigned teams.
        if not am.launched:
            current_team_by_steam_id = {
                int(getattr(m, "id", 0) or 0): int(getattr(m, "team", -1)) for m in members
            }
            ready = all(
                current_team_by_steam_id.get(s) == t
                for s, t in expected_team_by_steam_id.items()
            )
            if ready:
                am.launched = True
                if am.abandon_timer:
                    am.abandon_timer.kill()
                    am.abandon_timer = None
                log.info("all 10 on correct teams — launching match %s", am.match_id)
                try:
                    self._dota.launch_practice_lobby()
                except Exception:
                    log.exception("launch_practice_lobby failed")
                    am.launched = False

        # 4. If the GC has assigned a match_id, report it to the API.
        match_id_value = int(getattr(lobby, "match_id", 0) or 0)
        if match_id_value > 0 and not am.match_id_reported:
            am.match_id_reported = True
            log.info("match %s — Dota match ID: %d", am.match_id, match_id_value)
            self._post_match_id(am.match_id, match_id_value)

    def _on_lobby_removed(self, lobby) -> None:
        if not self._current_match_id:
            return
        log.info("lobby removed (match %s)", self._current_match_id)
        with self._lock:
            self._matches.pop(self._current_match_id, None)
            self._current_match_id = None

    # ---------- background tasks ----------

    def _abandon_by_timeout(self, match_id: str) -> None:
        with self._lock:
            am = self._matches.get(match_id)
        if not am or am.launched or am.cancelled:
            return
        log.info("abandon-timeout reached for match %s — destroying lobby", match_id)
        am.cancelled = True
        try:
            if self._dota and self._dota.lobby:
                self._dota.destroy_lobby()
        except Exception:
            log.exception("destroy_lobby on timeout failed")
        with self._lock:
            self._matches.pop(match_id, None)
            if self._current_match_id == match_id:
                self._current_match_id = None
        # Tell the API to flip the match to Abandoned
        try:
            requests.post(
                f"{self.cfg.api_base_url}/internal/matches/{match_id}/abandoned",
                headers={"x-worker-secret": self.cfg.worker_shared_secret},
                json={"reason": "timeout"},
                timeout=5,
            )
        except Exception:
            log.exception("abandon report to API failed")

    def _post_match_id(self, match_id: str, dota_match_id: int) -> None:
        def do_post():
            try:
                resp = requests.patch(
                    f"{self.cfg.api_base_url}/internal/matches/{match_id}/dota-match-id",
                    headers={"x-worker-secret": self.cfg.worker_shared_secret},
                    json={"dotaMatchId": dota_match_id},
                    timeout=5,
                )
                if not resp.ok:
                    log.warning("API rejected dota-match-id report: %d", resp.status_code)
            except Exception:
                log.exception("dota-match-id report failed")

        gevent.spawn(do_post)
