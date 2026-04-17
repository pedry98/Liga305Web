import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { catchError, map, Observable, of } from 'rxjs';
import { environment } from '../../environments/environment';
import { MatchDetail, MatchSummary, MmrHistoryPoint } from '../models/match';

@Injectable({ providedIn: 'root' })
export class MatchService {
  private readonly http = inject(HttpClient);
  private readonly api = environment.apiBaseUrl;

  getRecent(): Observable<MatchSummary[]> {
    return this.http.get<MatchSummary[]>(`${this.api}/matches`, { withCredentials: true });
  }

  getById(id: string): Observable<MatchDetail | null> {
    return this.http.get<MatchDetail>(`${this.api}/matches/${id}`, { withCredentials: true }).pipe(
      catchError(() => of(null))
    );
  }

  getRecentForUser(_userId: string): Observable<MatchSummary[]> {
    return this.http
      .get<MatchSummary[]>(`${this.api}/users/me/matches?limit=5`, { withCredentials: true })
      .pipe(catchError(() => of([])));
  }

  getMmrHistory(_userId: string): Observable<MmrHistoryPoint[]> {
    return this.http
      .get<MmrHistoryPoint[]>(`${this.api}/users/me/mmr-history`, { withCredentials: true })
      .pipe(
        map(points => points.map(p => ({ ...p, radiantWin: p.radiantWin ?? null } as MmrHistoryPoint))),
        catchError(() => of([]))
      );
  }

  launchMatch(matchId: string): Observable<{ launched: boolean }> {
    return this.http.post<{ launched: boolean }>(
      `${this.api}/matches/${matchId}/launch`,
      null,
      { withCredentials: true }
    );
  }

  resendInvite(matchId: string): Observable<{ invited: number }> {
    return this.http.post<{ invited: number }>(
      `${this.api}/matches/${matchId}/resend-invite`,
      null,
      { withCredentials: true }
    );
  }
}
