import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { interval } from 'rxjs';
import { AdminService } from '../../core/admin.service';
import { AuthService } from '../../core/auth.service';
import { DotaConstantsService, HeroInfo, ItemInfo } from '../../core/dota-constants.service';
import { MatchService } from '../../core/match.service';
import { MatchDetail, MatchPlayer, Team } from '../../models/match';

@Component({
  selector: 'app-match-detail',
  standalone: true,
  imports: [RouterLink, FormsModule, NgTemplateOutlet],
  templateUrl: './match-detail.component.html',
  styleUrl: './match-detail.component.scss'
})
export class MatchDetailComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly matches = inject(MatchService);
  private readonly admin = inject(AdminService);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dotaConstants = inject(DotaConstantsService);
  readonly constantsLoaded = this.dotaConstants.loaded;

  readonly user = this.auth.user;
  readonly match = signal<MatchDetail | null | undefined>(undefined);
  readonly copyHint = signal<string | null>(null);
  readonly resendBusy = signal(false);
  readonly resendMessage = signal<string | null>(null);
  readonly settleBusy = signal(false);
  readonly pasteMatchId = signal('');
  readonly pasteBusy = signal(false);
  readonly nowMs = signal(Date.now());

  readonly pickBusy = signal(false);

  readonly countdown = computed<string | null>(() => {
    const m = this.match();
    if (!m?.abandonsAt) return null;
    if (m.status !== 'Drafting' && m.status !== 'Draft' && m.status !== 'Lobby') return null;
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

  // Drafting view: only show players who are picked/captain on their team. The
  // "pool" is everyone who hasn't been picked yet (PickOrder === null).
  readonly radiant = computed<MatchPlayer[]>(() => {
    const m = this.match();
    if (!m) return [];
    if (m.status === 'Drafting') return m.players.filter(p => p.isPicked && p.team === 'Radiant');
    return m.players.filter(p => p.team === 'Radiant');
  });
  readonly dire = computed<MatchPlayer[]>(() => {
    const m = this.match();
    if (!m) return [];
    if (m.status === 'Drafting') return m.players.filter(p => p.isPicked && p.team === 'Dire');
    return m.players.filter(p => p.team === 'Dire');
  });
  readonly pool = computed<MatchPlayer[]>(() =>
    this.match()?.players.filter(p => !p.isPicked).sort((a, b) => b.mmrBefore - a.mmrBefore) ?? []
  );

  readonly isMyTurn = computed(() => {
    const m = this.match();
    const u = this.user();
    return !!(m && u && m.status === 'Drafting' && m.currentPickerUserId === u.id);
  });

  readonly currentPickerName = computed(() => {
    const m = this.match();
    if (!m?.currentPickerUserId) return null;
    return m.players.find(p => p.userId === m.currentPickerUserId)?.displayName ?? null;
  });

  // ---- Post-game rich stats -----------------------------------------------

  readonly radiantTotals = computed(() => this.teamTotals('Radiant'));
  readonly direTotals = computed(() => this.teamTotals('Dire'));

  /** Gold-advantage area-chart path + polarity zones. */
  readonly goldChart = computed(() => this.buildAdvantageChart(this.match()?.radiantGoldAdv ?? null));
  readonly xpChart = computed(() => this.buildAdvantageChart(this.match()?.radiantXpAdv ?? null));

  // ---- Interactive per-player net worth chart ----------------------------
  // Fixed viewBox coords; CSS scales the SVG to 100% width. Keeps math simple.
  readonly nwChartWidth = 1000;
  readonly nwChartHeight = 340;
  readonly nwPadLeft = 8;
  readonly nwPadRight = 8;
  readonly nwPadTop = 18;
  readonly nwPadBottom = 26;

  // Dota's official per-slot player colors (0..4 Radiant, 0..4 Dire).
  private readonly radiantSlotColors = ['#3375FF', '#66FFBF', '#BF00BF', '#F3F00B', '#FF6B00'];
  private readonly direSlotColors    = ['#FE86C2', '#A1B447', '#65D9F7', '#008321', '#A46900'];

  readonly hoverMinute = signal<number | null>(null);
  readonly hoverX = signal<number>(0); // px within the chart box, for tooltip placement

  readonly playerNetWorthChart = computed(() => this.buildPlayerNetWorthChart());

  hero(p: { heroId: number | null } | null | undefined): HeroInfo | null {
    return p ? this.dotaConstants.hero(p.heroId) : null;
  }
  item(id: number | null | undefined): ItemInfo | null { return this.dotaConstants.item(id ?? null); }

  fmtInt(n: number | null | undefined): string {
    if (n == null) return '—';
    return n.toLocaleString();
  }

  // Compact net-worth format: "12.3k" for anything over 1000.
  fmtNetWorth(n: number | null | undefined): string {
    if (n == null) return '—';
    if (n >= 1000) return (n / 1000).toFixed(1) + 'k';
    return String(n);
  }

  private teamTotals(team: Team) {
    const m = this.match();
    if (!m) return { kills: 0, netWorth: 0 };
    const ps = m.players.filter(p => p.team === team);
    return {
      kills: ps.reduce((s, p) => s + (p.kills ?? 0), 0),
      netWorth: ps.reduce((s, p) => s + (p.netWorth ?? 0), 0)
    };
  }

  /**
   * Build an SVG path for a per-minute Radiant-advantage array. Positive = Radiant
   * leads (green), negative = Dire leads (red). Output is normalized to a 0..1
   * coordinate space — the template scales to actual pixels via preserveAspectRatio="none".
   */
  private buildAdvantageChart(series: number[] | null) {
    if (!series || series.length < 2) return null;
    const n = series.length;
    const maxAbs = Math.max(1, ...series.map(v => Math.abs(v)));

    // Map point i to (x, y) in 0..1.
    const x = (i: number) => n === 1 ? 0 : i / (n - 1);
    const y = (v: number) => 0.5 - (v / (2 * maxAbs)); // zero line at 0.5; +v goes up

    // Line path.
    let linePath = '';
    for (let i = 0; i < n; i++) {
      linePath += `${i === 0 ? 'M' : 'L'}${x(i).toFixed(4)},${y(series[i]).toFixed(4)} `;
    }

    // Filled area — closed to the zero line. Drawn once with a clip per polarity
    // in the template (we render it twice, clipped to positive / negative halves).
    let areaPath = `M${x(0).toFixed(4)},0.5 `;
    for (let i = 0; i < n; i++) areaPath += `L${x(i).toFixed(4)},${y(series[i]).toFixed(4)} `;
    areaPath += `L${x(n - 1).toFixed(4)},0.5 Z`;

    // Peak lead labels.
    let maxPos = 0, maxPosIdx = 0, maxNeg = 0, maxNegIdx = 0;
    series.forEach((v, i) => {
      if (v > maxPos) { maxPos = v; maxPosIdx = i; }
      if (v < maxNeg) { maxNeg = v; maxNegIdx = i; }
    });

    return {
      linePath,
      areaPath,
      points: n,
      durationMin: n - 1,
      midMin: Math.floor((n - 1) / 2),
      peakRadiant: maxPos > 0 ? { value: maxPos, atMinute: maxPosIdx } : null,
      peakDire: maxNeg < 0 ? { value: -maxNeg, atMinute: maxNegIdx } : null
    };
  }

  // ---- Interactive per-player net worth chart ---------------------------

  /**
   * Build an interactive 10-line net-worth chart. Each player gets a Dota-slot
   * color. Returns pixel-space SVG paths (viewBox is fixed to nwChartWidth ×
   * nwChartHeight) plus a snapshot of every player's NW at each minute — so the
   * tooltip just does an array lookup during mouse move.
   */
  private buildPlayerNetWorthChart() {
    const m = this.match();
    if (!m) return null;
    const withSeries = m.players.filter(p => p.goldT && p.goldT.length >= 2);
    if (withSeries.length === 0) return null;

    const durationMin = Math.max(...withSeries.map(p => (p.goldT?.length ?? 1) - 1));
    const maxGold = Math.max(...withSeries.flatMap(p => p.goldT!));
    if (!Number.isFinite(maxGold) || maxGold <= 0) return null;

    const innerW = this.nwChartWidth - this.nwPadLeft - this.nwPadRight;
    const innerH = this.nwChartHeight - this.nwPadTop - this.nwPadBottom;

    const x = (i: number) => this.nwPadLeft + (durationMin === 0 ? 0 : (i / durationMin) * innerW);
    const y = (v: number) => this.nwPadTop + innerH - (v / maxGold) * innerH;

    // Build per-team slot index (0..4) from the order players are returned.
    const radiantOrder = m.players.filter(p => p.team === 'Radiant');
    const direOrder    = m.players.filter(p => p.team === 'Dire');

    const lines = m.players.map(p => {
      const goldT = p.goldT;
      const team = p.team;
      const slot = team === 'Radiant'
        ? radiantOrder.indexOf(p)
        : direOrder.indexOf(p);
      const color = team === 'Radiant'
        ? (this.radiantSlotColors[slot] ?? '#66c0f4')
        : (this.direSlotColors[slot] ?? '#d9807e');

      let pathD = '';
      if (goldT && goldT.length > 0) {
        for (let i = 0; i < goldT.length; i++) {
          pathD += `${i === 0 ? 'M' : 'L'}${x(i).toFixed(1)},${y(goldT[i]).toFixed(1)} `;
        }
      }
      return {
        userId: p.userId,
        displayName: p.displayName,
        team,
        color,
        slot,
        heroId: p.heroId,
        avatarUrl: p.avatarUrl,
        goldT: goldT ?? [],
        pathD,
        finalNetWorth: goldT && goldT.length > 0 ? goldT[goldT.length - 1] : null
      };
    });

    // Y-axis gridlines at nice round 5k steps.
    const step = maxGold >= 30000 ? 10000 : maxGold >= 15000 ? 5000 : 2000;
    const gridY: { value: number; y: number }[] = [];
    for (let v = step; v < maxGold; v += step) gridY.push({ value: v, y: y(v) });

    // X-axis ticks every 5 minutes.
    const gridX: { minute: number; x: number }[] = [];
    for (let min = 0; min <= durationMin; min += 5) gridX.push({ minute: min, x: x(min) });

    return {
      width: this.nwChartWidth,
      height: this.nwChartHeight,
      padLeft: this.nwPadLeft,
      padRight: this.nwPadRight,
      padTop: this.nwPadTop,
      padBottom: this.nwPadBottom,
      innerW,
      innerH,
      durationMin,
      maxGold,
      lines,
      gridX,
      gridY,
      xOfMinute: (i: number) => x(i)
    };
  }

  onNwChartMove(evt: MouseEvent) {
    const chart = this.playerNetWorthChart();
    if (!chart) return;
    const target = evt.currentTarget as HTMLElement;
    const rect = target.getBoundingClientRect();
    const relX = evt.clientX - rect.left;
    // Map back from rendered px to minute using the chart's inner x-range.
    const scale = rect.width / chart.width;
    const innerLeftPx = chart.padLeft * scale;
    const innerWPx = chart.innerW * scale;
    const clamped = Math.max(0, Math.min(innerWPx, relX - innerLeftPx));
    const minute = chart.durationMin === 0 ? 0 : Math.round((clamped / innerWPx) * chart.durationMin);
    this.hoverMinute.set(Math.max(0, Math.min(chart.durationMin, minute)));
    this.hoverX.set(relX);
  }

  onNwChartLeave() {
    this.hoverMinute.set(null);
  }

  /**
   * Players sorted by net worth at the hovered minute (descending). Drives the
   * floating tooltip, mimicking Dota's post-game graph where the leader is at
   * the top and the trailing player at the bottom.
   */
  readonly hoverBoard = computed(() => {
    const chart = this.playerNetWorthChart();
    const min = this.hoverMinute();
    if (!chart || min === null) return null;
    const rows = chart.lines
      .map(l => ({ ...l, networthAt: l.goldT[min] ?? l.goldT[l.goldT.length - 1] ?? 0 }))
      .sort((a, b) => b.networthAt - a.networthAt);
    return { minute: min, rows };
  });

  constructor() {
    const id = this.route.snapshot.paramMap.get('id') ?? '';
    this.matches.getById(id).subscribe(m => this.match.set(m));

    // Kick off the hero/item constants fetch. Doesn't block the initial render.
    this.dotaConstants.ensureLoaded();

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

  async pickPlayer(player: MatchPlayer) {
    const m = this.match();
    if (!m || this.pickBusy()) return;
    if (!this.isMyTurn()) return;
    if (player.isPicked) return;
    this.pickBusy.set(true);
    this.matches.pickPlayer(m.id, player.userId).subscribe({
      next: updated => this.match.set(updated),
      error: e => alert(e?.error?.error ?? 'Pick failed'),
      complete: () => this.pickBusy.set(false)
    });
  }

  statusClass(s: string): string { return 'status-' + s.toLowerCase(); }
  teamWon(team: Team, radiantWin: boolean | null): boolean {
    if (radiantWin === null) return false;
    return (team === 'Radiant' && radiantWin) || (team === 'Dire' && !radiantWin);
  }
}
