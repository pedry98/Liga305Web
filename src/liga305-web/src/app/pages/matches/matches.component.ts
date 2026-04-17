import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatchService } from '../../core/match.service';
import { MatchSummary, MatchStatus } from '../../models/match';

@Component({
  selector: 'app-matches',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './matches.component.html',
  styleUrl: './matches.component.scss'
})
export class MatchesComponent {
  private readonly matches = inject(MatchService);

  readonly rows = signal<MatchSummary[]>([]);
  readonly loading = signal(true);

  constructor() {
    this.matches.getRecent().subscribe(rows => {
      this.rows.set(rows);
      this.loading.set(false);
    });
  }

  statusClass(s: MatchStatus): string {
    return 'status-' + s.toLowerCase();
  }

  durationText(sec: number | null): string {
    if (sec === null) return '—';
    const m = Math.floor(sec / 60);
    const s = sec % 60;
    return `${m}:${s.toString().padStart(2, '0')}`;
  }

  timeAgo(iso: string): string {
    const diffMs = Date.now() - new Date(iso).getTime();
    const mins = Math.round(diffMs / 60000);
    if (mins < 1) return 'just now';
    if (mins < 60) return `${mins}m ago`;
    const hours = Math.round(mins / 60);
    if (hours < 24) return `${hours}h ago`;
    const days = Math.round(hours / 24);
    return `${days}d ago`;
  }

  winnerLabel(m: MatchSummary): string {
    if (m.status !== 'Completed') return '';
    return m.radiantWin ? 'Radiant victory' : 'Dire victory';
  }
}
