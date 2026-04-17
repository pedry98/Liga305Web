import { Component, computed, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { LeaderboardEntry, Season } from '../../models/season';
import { SeasonService } from '../../core/season.service';

@Component({
  selector: 'app-leaderboard',
  standalone: true,
  templateUrl: './leaderboard.component.html',
  styleUrl: './leaderboard.component.scss'
})
export class LeaderboardComponent {
  private readonly seasons = inject(SeasonService);

  readonly season = signal<Season | null>(null);
  readonly entries = signal<LeaderboardEntry[]>([]);
  readonly loading = signal(true);

  readonly topThree = computed(() => this.entries().slice(0, 3));
  readonly rest = computed(() => this.entries().slice(3));

  private subs: Subscription[] = [];

  constructor() {
    this.subs.push(
      this.seasons.getActiveSeason().subscribe(s => {
        this.season.set(s);
        this.subs.push(
          this.seasons.getLeaderboard(s.id).subscribe(rows => {
            this.entries.set(rows);
            this.loading.set(false);
          })
        );
      })
    );
  }

  winrate(e: LeaderboardEntry): number {
    const games = e.wins + e.losses;
    return games === 0 ? 0 : Math.round((e.wins / games) * 100);
  }

  rankClass(rank: number): string {
    if (rank === 1) return 'gold';
    if (rank === 2) return 'silver';
    if (rank === 3) return 'bronze';
    return '';
  }

  ngOnDestroy() {
    this.subs.forEach(s => s.unsubscribe());
  }
}
