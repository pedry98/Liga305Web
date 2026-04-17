import { Component, computed, inject, signal, ElementRef, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { MatchService } from '../../core/match.service';
import { MatchSummary, MmrHistoryPoint } from '../../models/match';

interface SparkPoint {
  x: number;
  y: number;
  delta: number;
  won: boolean;
  mmr: number;
}

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [RouterLink, FormsModule],
  templateUrl: './profile.component.html',
  styleUrl: './profile.component.scss'
})
export class ProfileComponent {
  private readonly auth = inject(AuthService);
  private readonly matches = inject(MatchService);
  private readonly router = inject(Router);

  readonly user = this.auth.user;
  readonly isReady = this.auth.isReady;
  readonly mmrHistory = signal<MmrHistoryPoint[]>([]);
  readonly recentMatches = signal<MatchSummary[]>([]);

  readonly editing = signal(false);
  readonly saving = signal(false);
  readonly uploading = signal(false);
  readonly editError = signal<string | null>(null);

  readonly editName = signal('');
  readonly editAvatarUrl = signal('');

  readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  readonly currentMmr = computed(() => {
    const h = this.mmrHistory();
    return h.length > 0 ? h[h.length - 1].mmrAfter : 1500;
  });

  readonly peakMmr = computed(() => {
    const h = this.mmrHistory();
    return h.length > 0 ? Math.max(...h.map(p => p.mmrAfter)) : 1500;
  });

  readonly wins = computed(() => this.mmrHistory().filter(p => p.won).length);
  readonly losses = computed(() => this.mmrHistory().filter(p => !p.won).length);
  readonly winrate = computed(() => {
    const games = this.wins() + this.losses();
    return games === 0 ? 0 : Math.round((this.wins() / games) * 100);
  });

  readonly chartWidth = 680;
  readonly chartHeight = 160;
  private readonly padX = 10;
  private readonly padY = 14;

  readonly sparkPoints = computed<SparkPoint[]>(() => {
    const h = this.mmrHistory();
    if (h.length === 0) return [];
    const min = Math.min(...h.map(p => p.mmrAfter));
    const max = Math.max(...h.map(p => p.mmrAfter));
    const range = Math.max(1, max - min);
    const innerW = this.chartWidth - this.padX * 2;
    const innerH = this.chartHeight - this.padY * 2;
    return h.map((p, i) => ({
      x: this.padX + (i / Math.max(1, h.length - 1)) * innerW,
      y: this.padY + (1 - (p.mmrAfter - min) / range) * innerH,
      delta: p.delta,
      won: p.won,
      mmr: p.mmrAfter
    }));
  });

  readonly sparkPath = computed(() =>
    this.sparkPoints().map((pt, i) => `${i === 0 ? 'M' : 'L'}${pt.x},${pt.y}`).join(' ')
  );

  readonly sparkArea = computed(() => {
    const pts = this.sparkPoints();
    if (pts.length === 0) return '';
    const base = this.chartHeight - this.padY;
    const line = pts.map((pt, i) => `${i === 0 ? 'M' : 'L'}${pt.x},${pt.y}`).join(' ');
    return `${line} L${pts[pts.length - 1].x},${base} L${pts[0].x},${base} Z`;
  });

  constructor() {
    queueMicrotask(() => {
      if (this.isReady() && !this.user()) {
        this.router.navigate(['/']);
      }
    });

    const u = this.user();
    if (u) {
      this.matches.getMmrHistory(u.id).subscribe(h => this.mmrHistory.set(h));
      this.matches.getRecentForUser(u.id).subscribe(m => this.recentMatches.set(m));
    }
  }

  beginEdit() {
    const u = this.user();
    if (!u) return;
    this.editName.set(u.displayName);
    this.editAvatarUrl.set(u.avatarUrl ?? '');
    this.editError.set(null);
    this.editing.set(true);
  }

  cancelEdit() {
    this.editing.set(false);
    this.editError.set(null);
  }

  async save() {
    if (this.saving()) return;
    this.saving.set(true);
    this.editError.set(null);
    try {
      await this.auth.updateProfile({
        displayName: this.editName().trim(),
        avatarUrl: this.editAvatarUrl().trim() || null
      });
      this.editing.set(false);
    } catch (e: any) {
      this.editError.set(e?.error?.message ?? 'Could not save. Check your inputs.');
    } finally {
      this.saving.set(false);
    }
  }

  triggerUpload() {
    this.fileInput()?.nativeElement.click();
  }

  async onFileChosen(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file || this.uploading()) return;
    this.uploading.set(true);
    this.editError.set(null);
    try {
      await this.auth.uploadAvatar(file);
      const u = this.user();
      if (u) this.editAvatarUrl.set(u.avatarUrl ?? '');
    } catch (e: any) {
      this.editError.set(e?.error?.message ?? 'Upload failed.');
    } finally {
      this.uploading.set(false);
      input.value = '';
    }
  }

  async resetToSteam() {
    if (!confirm('Reset your display name and avatar to your Steam profile?')) return;
    if (this.saving()) return;
    this.saving.set(true);
    this.editError.set(null);
    try {
      await this.auth.resetProfile();
      this.editing.set(false);
    } catch (e: any) {
      this.editError.set(e?.error?.message ?? 'Could not reset.');
    } finally {
      this.saving.set(false);
    }
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
}
