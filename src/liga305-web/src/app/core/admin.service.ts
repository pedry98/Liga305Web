import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';
import { Season } from '../models/season';

export interface AdminUser {
  id: string;
  steamId64: string;
  displayName: string;
  avatarUrl: string | null;
  mmr: number | null;
  wins: number;
  losses: number;
  abandons: number;
  isAdmin: boolean;
  isBanned: boolean;
  joinedAt: string;
}

@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);
  private readonly api = environment.apiBaseUrl;

  listUsers(): Promise<AdminUser[]> {
    return firstValueFrom(
      this.http.get<AdminUser[]>(`${this.api}/admin/users`, { withCredentials: true })
    );
  }

  toggleAdmin(userId: string): Promise<{ isAdmin: boolean }> {
    return firstValueFrom(
      this.http.post<{ isAdmin: boolean }>(
        `${this.api}/admin/users/${userId}/toggle-admin`,
        null,
        { withCredentials: true }
      )
    );
  }

  toggleBan(userId: string): Promise<{ isBanned: boolean }> {
    return firstValueFrom(
      this.http.post<{ isBanned: boolean }>(
        `${this.api}/admin/users/${userId}/toggle-ban`,
        null,
        { withCredentials: true }
      )
    );
  }

  createSeason(req: { name: string; startsAt: string; endsAt: string; makeActive?: boolean; startingMmr?: number }): Promise<Season> {
    return firstValueFrom(
      this.http.post<Season>(`${this.api}/admin/seasons`, req, { withCredentials: true })
    );
  }

  endSeason(seasonId: string): Promise<{ id: string; isActive: false; endsAt: string }> {
    return firstValueFrom(
      this.http.post<{ id: string; isActive: false; endsAt: string }>(
        `${this.api}/admin/seasons/${seasonId}/end`,
        null,
        { withCredentials: true }
      )
    );
  }

  updateSeason(seasonId: string, req: { name?: string; startsAt?: string; endsAt?: string }): Promise<Season> {
    return firstValueFrom(
      this.http.patch<Season>(`${this.api}/admin/seasons/${seasonId}`, req, { withCredentials: true })
    );
  }

  resetLeague(): Promise<{ matchesDeleted: number; botUsersRemoved: number; realUsersReset: number; startingMmr: number }> {
    return firstValueFrom(
      this.http.post<{ matchesDeleted: number; botUsersRemoved: number; realUsersReset: number; startingMmr: number }>(
        `${this.api}/admin/reset-league?confirm=YES`,
        null,
        { withCredentials: true }
      )
    );
  }

  fillQueueWithBots(targetSize = 9): Promise<{ added: number; queueSize: number }> {
    return firstValueFrom(
      this.http.post<{ added: number; queueSize: number }>(
        `${this.api}/admin/queue/fill-bots?targetSize=${targetSize}`,
        null,
        { withCredentials: true }
      )
    );
  }

  kickFromQueue(userId: string): Promise<{ kicked: true; userId: string }> {
    return firstValueFrom(
      this.http.post<{ kicked: true; userId: string }>(
        `${this.api}/admin/queue/kick/${userId}`,
        null,
        { withCredentials: true }
      )
    );
  }

  clearTestBots(): Promise<{ cancelled: number }> {
    return firstValueFrom(
      this.http.post<{ cancelled: number }>(
        `${this.api}/admin/queue/clear-bots`,
        null,
        { withCredentials: true }
      )
    );
  }

  cancelMatch(matchId: string): Promise<{ cancelled: boolean; botDestroyed: boolean }> {
    return firstValueFrom(
      this.http.post<{ cancelled: boolean; botDestroyed: boolean }>(
        `${this.api}/admin/matches/${matchId}/cancel`,
        null,
        { withCredentials: true }
      )
    );
  }

  testSettleMatch(matchId: string, radiantWin: boolean, durationSec = 1845): Promise<{ settled: boolean }> {
    return firstValueFrom(
      this.http.post<{ settled: boolean }>(
        `${this.api}/admin/matches/${matchId}/test-settle`,
        { radiantWin, durationSec },
        { withCredentials: true }
      )
    );
  }

  setDotaMatchId(matchId: string, dotaMatchId: number): Promise<{ dotaMatchId: number }> {
    return firstValueFrom(
      this.http.post<{ dotaMatchId: number }>(
        `${this.api}/admin/matches/${matchId}/set-dota-match-id`,
        { dotaMatchId },
        { withCredentials: true }
      )
    );
  }

  probeOpenDotaMatch(dotaMatchId: number): Promise<OpenDotaProbeResult> {
    return firstValueFrom(
      this.http.get<OpenDotaProbeResult>(
        `${this.api}/admin/opendota/match/${dotaMatchId}`,
        { withCredentials: true }
      )
    );
  }

  importMatchFromOpenDota(dotaMatchId: number): Promise<ImportMatchResult> {
    return firstValueFrom(
      this.http.post<ImportMatchResult>(
        `${this.api}/admin/matches/import-from-opendota`,
        { dotaMatchId },
        { withCredentials: true }
      )
    );
  }
}

export interface OpenDotaProbePlayer {
  accountId: number;
  steamId64: string | null;
  isRadiant: boolean;
  playerSlot: number;
  kills: number;
  deaths: number;
  assists: number;
  abandoned: boolean;
  leagueUserId: string | null;
  leagueDisplayName: string | null;
  seasonMmr: number | null;
  matched: boolean;
}

export interface OpenDotaProbeResult {
  found: boolean;
  dotaMatchId: number;
  message?: string;
  radiantWin?: boolean;
  durationSec?: number;
  startedAt?: string | null;
  parsed?: boolean;
  playerCount?: number;
  matchedCount?: number;
  activeSeasonId?: string | null;
  activeSeasonName?: string | null;
  alreadyImported?: boolean;
  importable?: boolean;
  importBlockedReason?: string | null;
  players?: OpenDotaProbePlayer[];
}

export interface ImportMatchResult {
  imported: boolean;
  matchId: string;
  dotaMatchId: number;
  seasonId: string;
  seasonName: string;
  radiantWin: boolean;
  durationSec: number;
}
