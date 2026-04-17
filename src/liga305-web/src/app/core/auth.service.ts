import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';

export interface CurrentUser {
  id: string;
  steamId64: string;
  displayName: string;
  avatarUrl: string | null;
  isAdmin: boolean;
  hasCustomProfile: boolean;
}

export interface UpdateProfilePayload {
  displayName?: string;
  avatarUrl?: string | null;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly api = environment.apiBaseUrl;

  private readonly _user = signal<CurrentUser | null | undefined>(undefined);

  readonly user = this._user.asReadonly();
  readonly isAuthenticated = computed(() => !!this._user());
  readonly isReady = computed(() => this._user() !== undefined);

  async refresh(): Promise<void> {
    try {
      const me = await firstValueFrom(
        this.http.get<CurrentUser>(`${this.api}/auth/me`, { withCredentials: true })
      );
      this._user.set(me);
    } catch {
      this._user.set(null);
    }
  }

  loginUrl(returnPath = '/'): string {
    const target = encodeURIComponent(returnPath);
    return `${this.api}/auth/steam/login?returnUrl=${target}`;
  }

  async logout(): Promise<void> {
    await firstValueFrom(
      this.http.post(`${this.api}/auth/logout`, null, { withCredentials: true })
    );
    this._user.set(null);
  }

  async updateProfile(payload: UpdateProfilePayload): Promise<void> {
    const updated = await firstValueFrom(
      this.http.patch<CurrentUser>(`${this.api}/users/me/profile`, payload, { withCredentials: true })
    );
    this._user.set(updated);
  }

  async uploadAvatar(file: File): Promise<void> {
    const fd = new FormData();
    fd.append('file', file);
    const updated = await firstValueFrom(
      this.http.post<CurrentUser>(`${this.api}/users/me/avatar`, fd, { withCredentials: true })
    );
    this._user.set(updated);
  }

  async resetProfile(): Promise<void> {
    const updated = await firstValueFrom(
      this.http.post<CurrentUser>(`${this.api}/users/me/profile/reset`, null, { withCredentials: true })
    );
    this._user.set(updated);
  }
}
