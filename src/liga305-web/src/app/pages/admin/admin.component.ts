import { Component, computed, effect, HostListener, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AdminService, AdminUser, OpenDotaProbeResult } from '../../core/admin.service';
import { AuthService } from '../../core/auth.service';
import { SeasonService } from '../../core/season.service';
import { MatchService } from '../../core/match.service';
import { Season } from '../../models/season';
import { MatchSummary } from '../../models/match';

type Tab = 'seasons' | 'users' | 'matches';

interface SeasonDraft {
  name: string;
  startsAt: string; // yyyy-MM-dd (input[type=date] value)
  endsAt: string;
}

@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [RouterLink, FormsModule],
  templateUrl: './admin.component.html',
  styleUrl: './admin.component.scss'
})
export class AdminComponent {
  private readonly auth = inject(AuthService);
  private readonly matches = inject(MatchService);
  private readonly adminSvc = inject(AdminService);
  private readonly seasonSvc = inject(SeasonService);

  readonly user = this.auth.user;
  readonly isReady = this.auth.isReady;
  readonly tab = signal<Tab>('seasons');
  readonly preview = signal(false);

  readonly seasons = signal<Season[]>([]);
  readonly seasonsLoading = signal(false);
  readonly seasonsError = signal<string | null>(null);

  // New-season form state
  readonly showNewSeasonForm = signal(false);
  readonly newSeasonDraft = signal<SeasonDraft>(this.blankDraft());
  readonly creatingSeason = signal(false);

  // Inline-edit state — only one row at a time
  readonly editingSeasonId = signal<string | null>(null);
  readonly editDraft = signal<SeasonDraft>(this.blankDraft());
  readonly savingSeason = signal(false);

  readonly users = signal<AdminUser[]>([]);
  readonly usersLoading = signal(false);
  readonly usersError = signal<string | null>(null);
  readonly activeMatches = signal<MatchSummary[]>([]);

  // OpenDota stats probe — check whether a Dota match ID would settle via the poller.
  readonly probeInput = signal('');
  readonly probing = signal(false);
  readonly probeResult = signal<OpenDotaProbeResult | null>(null);
  readonly probeError = signal<string | null>(null);
  readonly importing = signal(false);
  readonly importMessage = signal<string | null>(null);

  readonly canSee = computed(() => {
    const u = this.user();
    return this.preview() || (!!u && u.isAdmin);
  });

  enablePreview() { this.preview.set(true); }

  constructor() {
    // Public data loads immediately — no auth dependency, no effect timing risk.
    this.loadSeasons();
    this.loadMatches();

    // Users requires admin. Load the moment canSee flips true (auth resolves
    // after the constructor) AND the user is on the Users tab.
    effect(() => {
      if (this.canSee() && this.tab() === 'users') this.loadUsers();
    });
  }

  // Refresh the active tab whenever the window regains focus — keeps lists fresh
  // without a manual refresh button.
  @HostListener('window:focus')
  onWindowFocus() {
    this.refreshActiveTab();
  }

  setTab(t: Tab) {
    this.tab.set(t);
    // Always re-fetch whichever tab the user just clicked (acts as refresh too).
    this.refreshActiveTab();
  }

  private refreshActiveTab() {
    const t = this.tab();
    if (t === 'users') {
      if (this.canSee()) this.loadUsers();
    } else if (t === 'seasons') {
      this.loadSeasons();
    } else if (t === 'matches') {
      this.loadMatches();
    }
  }

  // ----- Seasons -----

  loadSeasons() {
    if (this.seasonsLoading()) return;
    this.seasonsLoading.set(true);
    this.seasonsError.set(null);
    this.seasonSvc.getAllSeasons().subscribe({
      next: rows => { this.seasons.set(rows); this.seasonsLoading.set(false); },
      error: e => {
        this.seasonsError.set(e?.error?.error ?? 'Could not load seasons.');
        this.seasonsLoading.set(false);
      }
    });
  }

  loadMatches() {
    this.matches.getRecent().subscribe(rows => this.activeMatches.set(rows));
  }

  openNewSeasonForm() {
    this.newSeasonDraft.set(this.suggestNextSeasonDraft());
    this.showNewSeasonForm.set(true);
  }

  cancelNewSeason() {
    this.showNewSeasonForm.set(false);
  }

  async saveNewSeason() {
    const d = this.newSeasonDraft();
    if (!d.name.trim()) { alert('Season name is required.'); return; }
    if (!d.startsAt || !d.endsAt) { alert('Start and end dates are required.'); return; }
    if (new Date(d.endsAt) <= new Date(d.startsAt)) { alert('End date must be after start date.'); return; }
    if (!confirm(`Create season "${d.name}" and make it active? Any current active season will be ended.`)) return;

    this.creatingSeason.set(true);
    try {
      await this.adminSvc.createSeason({
        name: d.name.trim(),
        startsAt: new Date(d.startsAt + 'T00:00:00Z').toISOString(),
        endsAt: new Date(d.endsAt + 'T23:59:59Z').toISOString(),
        makeActive: true
      });
      this.showNewSeasonForm.set(false);
      this.loadSeasons();
    } catch (e: any) {
      alert(e?.error?.error ?? 'Could not create season.');
    } finally {
      this.creatingSeason.set(false);
    }
  }

  startEditSeason(s: Season) {
    this.editingSeasonId.set(s.id);
    this.editDraft.set({
      name: s.name,
      startsAt: this.toDateInput(s.startsAt),
      endsAt: this.toDateInput(s.endsAt)
    });
  }

  cancelEditSeason() {
    this.editingSeasonId.set(null);
  }

  async saveEditSeason(s: Season) {
    const d = this.editDraft();
    if (!d.name.trim()) { alert('Season name is required.'); return; }
    if (new Date(d.endsAt) <= new Date(d.startsAt)) { alert('End date must be after start date.'); return; }

    this.savingSeason.set(true);
    try {
      await this.adminSvc.updateSeason(s.id, {
        name: d.name.trim(),
        startsAt: new Date(d.startsAt + 'T00:00:00Z').toISOString(),
        endsAt: new Date(d.endsAt + 'T23:59:59Z').toISOString()
      });
      this.editingSeasonId.set(null);
      this.loadSeasons();
    } catch (e: any) {
      alert(e?.error?.error ?? 'Could not save season.');
    } finally {
      this.savingSeason.set(false);
    }
  }

  async endSeason(s: Season) {
    if (!confirm(`End "${s.name}"? Players will need a new season to keep playing ranked.`)) return;
    try {
      await this.adminSvc.endSeason(s.id);
      this.loadSeasons();
    } catch (e: any) {
      alert(e?.error?.error ?? 'Could not end season.');
    }
  }

  // ----- League reset -----

  async resetLeague() {
    if (!confirm('Reset the league?\n\nThis will:\n• delete every match and its history\n• remove all TestBot accounts\n• reset every real player to 1000 MMR with 0 wins/losses\n\nThere is no undo.')) return;
    if (!confirm('Are you absolutely sure? This is the second confirm.')) return;
    try {
      const r = await this.adminSvc.resetLeague();
      alert(`League reset.\n\n${r.matchesDeleted} matches deleted, ${r.botUsersRemoved} bot users removed, ${r.realUsersReset} real users reset to ${r.startingMmr} MMR.`);
      this.loadUsers();
      this.loadSeasons();
      this.loadMatches();
    } catch (e: any) {
      alert(e?.error?.error ?? 'Reset failed');
    }
  }

  // ----- Users -----

  async loadUsers() {
    if (this.usersLoading()) return;
    this.usersLoading.set(true);
    this.usersError.set(null);
    try {
      const list = await this.adminSvc.listUsers();
      this.users.set(list);
    } catch (e: any) {
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

  async probeOpenDota() {
    const raw = this.probeInput().trim();
    if (!raw) { this.probeError.set('Enter a Dota match ID.'); return; }
    const id = Number(raw);
    if (!Number.isInteger(id) || id <= 0) {
      this.probeError.set('Match ID must be a positive integer (e.g. 7654321098).');
      return;
    }

    this.probing.set(true);
    this.probeError.set(null);
    this.probeResult.set(null);
    this.importMessage.set(null);
    try {
      const r = await this.adminSvc.probeOpenDotaMatch(id);
      this.probeResult.set(r);
    } catch (e: any) {
      if (e?.status === 403) {
        this.probeError.set('Preview mode: sign in as admin to probe OpenDota.');
      } else {
        this.probeError.set(e?.error?.error ?? 'OpenDota probe failed.');
      }
    } finally {
      this.probing.set(false);
    }
  }

  async importProbedMatch() {
    const r = this.probeResult();
    if (!r || !r.found || !r.importable) return;
    const label = `match ${r.dotaMatchId} (${r.radiantWin ? 'Radiant win' : 'Dire win'}, ${r.durationSec}s)`;
    const placeholders = r.willCreatePlaceholders ?? 0;
    const placeholderLine = placeholders > 0
      ? `\n\n${placeholders} placeholder account(s) will be created for unregistered players. They'll be auto-claimed when those players sign in with Steam.`
      : '';
    if (!confirm(`Register ${label} into season "${r.activeSeasonName}"?\n\nThis will create the match, record K/D/A, update W/L, and recalculate MMR for all 10 players. This cannot be undone without a league reset.${placeholderLine}`)) return;

    this.importing.set(true);
    this.importMessage.set(null);
    this.probeError.set(null);
    try {
      const res = await this.adminSvc.importMatchFromOpenDota(r.dotaMatchId);
      const ph = res.placeholdersCreated > 0 ? ` ${res.placeholdersCreated} placeholder account(s) created.` : '';
      this.importMessage.set(`Imported — match ${res.matchId.slice(0, 8)} registered in "${res.seasonName}". ${res.radiantWin ? 'Radiant' : 'Dire'} won (${res.durationSec}s).${ph}`);
      // Re-probe so the result now shows "already imported" if they re-check.
      await this.probeOpenDota();
      // Refresh the matches list so the new row appears below.
      this.loadMatches();
    } catch (e: any) {
      const code = e?.error?.error;
      const pretty: Record<string, string> = {
        already_imported: 'This Dota match ID has already been imported.',
        no_active_season: 'There is no active season to import into.',
        opendota_not_ready: 'OpenDota doesn\'t have the stats yet. Try again in a few minutes.',
        wrong_player_count: 'Match does not have 10 players.',
        anonymous_slot: 'One or more players have a hidden Dota profile and can\'t be imported.',
        not_five_v_five: 'Teams are not balanced 5v5.',
      };
      this.probeError.set(pretty[code] ?? e?.error?.error ?? 'Import failed.');
    } finally {
      this.importing.set(false);
    }
  }

  clearProbe() {
    this.probeInput.set('');
    this.probeResult.set(null);
    this.probeError.set(null);
    this.importMessage.set(null);
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

  // ----- helpers -----

  updateNewDraft(field: keyof SeasonDraft, value: string) {
    this.newSeasonDraft.set({ ...this.newSeasonDraft(), [field]: value });
  }

  updateEditDraft(field: keyof SeasonDraft, value: string) {
    this.editDraft.set({ ...this.editDraft(), [field]: value });
  }

  private blankDraft(): SeasonDraft {
    return { name: '', startsAt: '', endsAt: '' };
  }

  private toDateInput(iso: string): string {
    const d = new Date(iso);
    const yyyy = d.getUTCFullYear();
    const mm = String(d.getUTCMonth() + 1).padStart(2, '0');
    const dd = String(d.getUTCDate()).padStart(2, '0');
    return `${yyyy}-${mm}-${dd}`;
  }

  private suggestNextSeasonDraft(): SeasonDraft {
    const now = new Date();
    const start = now;
    const end = new Date(now.getTime());
    end.setMonth(end.getMonth() + 3);
    return {
      name: `Season ${now.getUTCFullYear()}.${Math.floor(now.getUTCMonth() / 3) + 1}`,
      startsAt: this.toDateInput(start.toISOString()),
      endsAt: this.toDateInput(end.toISOString())
    };
  }
}
