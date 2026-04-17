import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AdminService } from '../../core/admin.service';
import { AuthService } from '../../core/auth.service';
import { AdminUser, MOCK_ADMIN_USERS, MOCK_ALL_SEASONS } from '../../core/admin-mock';
import { MatchService } from '../../core/match.service';
import { Season } from '../../models/season';
import { MatchSummary } from '../../models/match';

type Tab = 'seasons' | 'users' | 'matches';

@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './admin.component.html',
  styleUrl: './admin.component.scss'
})
export class AdminComponent {
  private readonly auth = inject(AuthService);
  private readonly matches = inject(MatchService);
  private readonly adminSvc = inject(AdminService);

  readonly user = this.auth.user;
  readonly isReady = this.auth.isReady;
  readonly tab = signal<Tab>('seasons');
  readonly preview = signal(false);

  readonly seasons = signal<Season[]>(MOCK_ALL_SEASONS);
  readonly users = signal<AdminUser[]>(MOCK_ADMIN_USERS);
  readonly activeMatches = signal<MatchSummary[]>([]);

  readonly canSee = computed(() => {
    const u = this.user();
    return this.preview() || (!!u && u.isAdmin);
  });

  enablePreview() { this.preview.set(true); }

  constructor() {
    this.matches.getRecent().subscribe(rows => this.activeMatches.set(rows));
  }

  setTab(t: Tab) { this.tab.set(t); }

  toggleAdmin(user: AdminUser) {
    this.users.set(this.users().map(u => u.id === user.id ? { ...u, isAdmin: !u.isAdmin } : u));
  }

  toggleBan(user: AdminUser) {
    this.users.set(this.users().map(u => u.id === user.id ? { ...u, isBanned: !u.isBanned } : u));
  }

  endSeason(season: Season) {
    this.seasons.set(this.seasons().map(s => s.id === season.id ? { ...s, isActive: false } : s));
  }

  async cancelMatch(m: MatchSummary) {
    if (!confirm(`Force-cancel match #${m.id.slice(0, 8)} and destroy the Dota lobby?`)) return;
    try {
      await this.adminSvc.cancelMatch(m.id);
      this.activeMatches.set(this.activeMatches().map(x => x.id === m.id ? { ...x, status: 'Abandoned' } : x));
    } catch (e: any) {
      alert(e?.error?.error ?? 'Cancel failed');
    }
  }

  formatDate(iso: string): string {
    return new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
  }
}
