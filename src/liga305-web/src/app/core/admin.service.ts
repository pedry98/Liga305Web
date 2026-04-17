import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';

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

  fillQueueWithBots(targetSize = 9): Promise<{ added: number; queueSize: number }> {
    return firstValueFrom(
      this.http.post<{ added: number; queueSize: number }>(
        `${this.api}/admin/queue/fill-bots?targetSize=${targetSize}`,
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
}
