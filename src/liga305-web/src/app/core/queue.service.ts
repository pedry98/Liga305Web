import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { QueueState } from '../models/season';

const EMPTY: QueueState = {
  seasonId: '',
  size: 0,
  capacity: 10,
  selfInQueue: false,
  entries: []
};

@Injectable({ providedIn: 'root' })
export class QueueService {
  private readonly http = inject(HttpClient);
  private readonly api = environment.apiBaseUrl;

  private readonly _state = signal<QueueState>(EMPTY);
  readonly state = this._state.asReadonly();

  load(): Observable<QueueState> {
    return this.http.get<QueueState>(`${this.api}/queue`, { withCredentials: true })
      .pipe(tap(s => this._state.set(s)));
  }

  join(_displayName: string): Observable<QueueState> {
    return this.http.post<QueueState>(`${this.api}/queue/join`, null, { withCredentials: true })
      .pipe(tap(s => this._state.set(s)));
  }

  leave(): Observable<QueueState> {
    return this.http.delete<QueueState>(`${this.api}/queue/leave`, { withCredentials: true })
      .pipe(tap(s => this._state.set(s)));
  }
}
