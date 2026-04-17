import { Component, computed, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AdminService, AdminUser } from '../../core/admin.service';
import { AuthService } from '../../core/auth.service';
import { MOCK_ALL_SEASONS } from '../../core/admin-mock';
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

  // Seasons still mocked — admin season-management isn't backed yet (Phase 6).
  readonly seasons = signal<Season[]>(MOCK_ALL_SEASONS);
  readonly users = signal<AdminUser[]>([]);
  readonly usersLoading = signal(false);
  readonly usersError = signal<string | null>(null);
  readonly activeMatches = signal<MatchSummary[]>([]);

  readonly canSee = computed(() => {
    const u = this.user();
    return this.preview() || (!!u && u.isAdmin);
  });

  enablePreview() { this.preview.set(true); }

  constructor() {
    this.matches.getRecent().subscribe(rows => this.activeMatches.set(rows));

    // Refresh users when (a) the gate opens or (b) the user clicks the Users tab.
    effect(() => {
      if (this.canSee() && this.tab() === 'users') this.loadUsers();
    });
  }

  setTab(t: Tab) { this.tab.set(t); }

  async resetLeague() {
    if (!confirm('Reset the league?\n\nThis will:\n• delete every match and its history\n• remove all TestBot accounts\n• reset every real player to 1000 MMR with 0 wins/losses\n\nThere is no undo.')) return;
    if (!confirm('Are you absolutely sure? Type-in nothing — this is the second confirm.')) return;
    try {
      const r = await this.adminSvc.resetLeague();
      alert(`League reset.\n\n${r.matchesDeleted} matches deleted, ${r.botUsersRemoved} bot users removed, ${r.realUsersReset} real users reset to ${r.startingMmr} MMR.`);
      this.loadUsers();
      this.matches.getRecent().subscribe(rows => this.activeMatches.set(rows));
    } catch (e: any) {
      alert(e?.error?.error ?? 'Reset failed');
    }
  }

  async loadUsers() {
    if (this.usersLoading()) return;
    this.usersLoading.set(true);
    this.usersError.set(null);
    try {
      const list = await this.adminSvc.listUsers();
      this.users.set(list);
    } catch (e: any) {
      // 403 = preview mode (not actually admin) — show empty + a hint, no alert.
      if (e?.status === 403) {
        this.usersError.set('Preview mode: sign in as admin to load real users.');
      } else {
        this.usersError.set(e?.error?.error ?? 'Could not load users.');
      }
    } finally {
      this.usersLoading.set(false);
    }
  }

  async toggleAdmin(user: AdminUser) {
    if (!confirm(user.isAdmin
        ? `Revoke admin from ${user.displayName}?`
        : `Grant admin to ${user.displayName}?`)) return;
    try {
      const r = await this.adminSvc.toggleAdmin(user.id);
      this.users.set(this.users().map(u => u.id === user.id ? { ...u, isAdmin: r.isAdmin } : u));
    } catch (e: any) {
      alert(e?.error?.error ?? 'Toggle failed');
    }
  }

  async toggleBan(user: AdminUser) {
    if (!confirm(user.isBanned
        ? `Unban ${user.displayName}?`
        : `Ban ${user.displayName}? They won't be able to queue or play.`)) return;
    try {
      const r = await this.adminSvc.toggleBan(user.id);
      this.users.set(this.users().map(u => u.id === user.id ? { ...u, isBanned: r.isBanned } : u));
    } catch (e: any) {
      alert(e?.error?.error ?? 'Toggle failed');
    }
  }

  endSeason(season: Season) {
    // Local-only stub for now; real season-end endpoint comes with Phase 6.
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
