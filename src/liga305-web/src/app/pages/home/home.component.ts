import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { QueueService } from '../../core/queue.service';
import { SeasonService } from '../../core/season.service';
import { Season } from '../../models/season';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss'
})
export class HomeComponent {
  private readonly auth = inject(AuthService);
  private readonly seasons = inject(SeasonService);
  private readonly queue = inject(QueueService);

  readonly user = this.auth.user;
  readonly season = signal<Season | null>(null);
  readonly queueState = this.queue.state;

  readonly steamLoginUrl = this.auth.loginUrl('/');

  readonly queueProgress = computed(() => {
    const s = this.queueState();
    return Math.round((s.size / s.capacity) * 100);
  });

  constructor() {
    this.seasons.getActiveSeason().subscribe(s => this.season.set(s));
    this.queue.load().subscribe();
  }
}
