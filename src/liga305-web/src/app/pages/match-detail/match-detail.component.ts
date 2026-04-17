import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { interval } from 'rxjs';
import { AdminService } from '../../core/admin.service';
import { AuthService } from '../../core/auth.service';
import { MatchService } from '../../core/match.service';
import { MatchDetail, MatchPlayer, Team } from '../../models/match';

@Component({
  selector: 'app-match-detail',
  standalone: true,
  imports: [RouterLink, FormsModule],
  templateUrl: './match-detail.component.html',
  styleUrl: './match-detail.component.scss'
})
export class MatchDetailComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly matches = inject(MatchService);
  private readonly admin = inject(AdminService);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);

  readonly user = this.auth.user;
  readonly match = signal<MatchDetail | null | undefined>(undefined);
  readonly copyHint = signal<string | null>(null);
  readonly resendBusy = signal(false);
  readonly resendMessage = signal<string | null>(null);
  readonly settleBusy = signal(false);
  readonly pasteMatchId = signal('');
  readonly pasteBusy = signal(false);
  readonly nowMs = signal(Date.now());

  readonly countdown = computed<string | null>(() => {
    const m = this.match();
    if (!m?.abandonsAt) return null;
    if (m.status !== 'Draft' && m.status !== 'Lobby') return null;
    const remainingMs = new Date(m.abandonsAt).getTime() - this.nowMs();
    if (remainingMs <= 0) return '0:00';
    const totalSec = Math.floor(remainingMs / 1000);
    const mm = Math.floor(totalSec / 60);
    const ss = totalSec % 60;
    return `${mm}:${ss.toString().padStart(2, '0')}`;
  });

  readonly countdownLow = computed(() => {
    const m = this.match();
    if (!m?.abandonsAt) return false;
    return new Date(m.abandonsAt).getTime() - this.nowMs() < 60_000; // last minute
  });

  readonly radiant = computed<MatchPlayer[]>(() => this.match()?.players.filter(p => p.team === 'Radiant') ?? []);
  readonly dire    = computed<MatchPlayer[]>(() => this.match()?.players.filter(p => p.team === 'Dire')    ?? []);

  constructor() {
    const id = this.route.snapshot.paramMap.get('id') ?? '';
    this.matches.getById(id).subscribe(m => this.match.set(m));

    // Poll every 3s while the match is in a transient state (Draft -> Lobby -> Live).
    interval(3000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        const current = this.match();
        if (!current) return;
        if (current.status === 'Completed' || current.status === 'Abandoned') return;
        this.matches.getById(id).subscribe(updated => {
          if (updated) this.match.set(updated);
        });
      });

    // Tick the countdown signal every 1s for a smooth display.
    interval(1000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.nowMs.set(Date.now()));
  }

  durationText(sec: number | null): string {
    if (sec === null) return '—';
    const m = Math.floor(sec / 60);
    const s = sec % 60;
    return `${m}:${s.toString().padStart(2, '0')}`;
  }

  mmrDelta(p: MatchPlayer): number | null {
    if (p.mmrAfter === null) return null;
    return p.mmrAfter - p.mmrBefore;
  }

  async copy(value: string | null, label: string) {
    if (!value) return;
    try {
      await navigator.clipboard.writeText(value);
      this.copyHint.set(`${label} copied`);
      setTimeout(() => this.copyHint.set(null), 1500);
    } catch {
      this.copyHint.set('Copy failed');
      setTimeout(() => this.copyHint.set(null), 1500);
    }
  }

  async resendInvite() {
    const m = this.match();
    if (!m || this.resendBusy()) return;
    this.resendBusy.set(true);
    this.resendMessage.set(null);
    this.matches.resendInvite(m.id).subscribe({
      next: r => {
        this.resendMessage.set(`Invite sent to ${r.invited} player(s). Check your Dota 2 client.`);
        setTimeout(() => this.resendMessage.set(null), 4000);
      },
      error: e => {
        this.resendMessage.set(e?.error?.error ?? 'Could not send invite.');
        setTimeout(() => this.resendMessage.set(null), 4000);
      },
      complete: () => this.resendBusy.set(false)
    });
  }

  async testSettle(radiantWin: boolean) {
    const m = this.match();
    if (!m || this.settleBusy()) return;
    this.settleBusy.set(true);
    try {
      await this.admin.testSettleMatch(m.id, radiantWin);
      this.matches.getById(m.id).subscribe(updated => { if (updated) this.match.set(updated); });
    } catch (e: any) {
      alert(e?.error?.error ?? 'Settle failed');
    } finally {
      this.settleBusy.set(false);
    }
  }

  async submitDotaMatchId() {
    const m = this.match();
    const id = Number(this.pasteMatchId().trim());
    if (!m || !id || id <= 0 || this.pasteBusy()) return;
    this.pasteBusy.set(true);
    try {
      await this.admin.setDotaMatchId(m.id, id);
      this.pasteMatchId.set('');
      this.matches.getById(m.id).subscribe(updated => { if (updated) this.match.set(updated); });
    } catch (e: any) {
      alert(e?.error?.error ?? 'Could not set match ID');
    } finally {
      this.pasteBusy.set(false);
    }
  }

  statusClass(s: string): string { return 'status-' + s.toLowerCase(); }
  teamWon(team: Team, radiantWin: boolean | null): boolean {
    if (radiantWin === null) return false;
    return (team === 'Radiant' && radiantWin) || (team === 'Dire' && !radiantWin);
  }
}
