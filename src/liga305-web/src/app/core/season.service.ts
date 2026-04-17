import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { LeaderboardEntry, Season } from '../models/season';

@Injectable({ providedIn: 'root' })
export class SeasonService {
  private readonly http = inject(HttpClient);
  private readonly api = environment.apiBaseUrl;

  getActiveSeason(): Observable<Season> {
    return this.http.get<Season>(`${this.api}/seasons/active`, { withCredentials: true });
  }

  getAllSeasons(): Observable<Season[]> {
    return this.http.get<Season[]>(`${this.api}/seasons`, { withCredentials: true });
  }

  getLeaderboard(seasonId: string): Observable<LeaderboardEntry[]> {
    return this.http.get<LeaderboardEntry[]>(
      `${this.api}/seasons/${seasonId}/leaderboard`,
      { withCredentials: true }
    );
  }

  getActiveLeaderboard(): Observable<LeaderboardEntry[]> {
    return this.http.get<LeaderboardEntry[]>(
      `${this.api}/seasons/active/leaderboard`,
      { withCredentials: true }
    );
  }
}
