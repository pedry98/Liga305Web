import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AdminService } from '../../core/admin.service';
import { AuthService } from '../../core/auth.service';
import { QueueService } from '../../core/queue.service';

@Component({
  selector: 'app-queue',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './queue.component.html',
  styleUrl: './queue.component.scss'
})
export class QueueComponent {
  private readonly auth = inject(AuthService);
  private readonly queue = inject(QueueService);
  private readonly admin = inject(AdminService);
  private readonly router = inject(Router);

  readonly user = this.auth.user;
  readonly state = this.queue.state;
  readonly busy = signal(false);
  readonly fillingBots = signal(false);
  readonly adminMessage = signal<string | null>(null);

  readonly progress = computed(() => {
    const s = this.state();
    return Math.min(100, Math.round((s.size / s.capacity) * 100));
  });

  readonly poppedClass = computed(() => this.state().size >= this.state().capacity ? 'popped' : '');
  readonly steamLoginUrl = this.auth.loginUrl('/queue');

  private prevSize = 0;

  constructor() {
    this.queue.load().subscribe(s => this.prevSize = s.size);

    // Poll for queue updates every 3 seconds. When queue size drops to 0 with a recent
    // last match (size went 10 -> 0), navigate to that match so we can see the lobby form.
    const interval = setInterval(async () => {
      try {
        const next = await new Promise<typeof this.state.prototype>(resolve => {
          this.queue.load().subscribe(s => resolve(s as any));
        }) as any;
        const wasFull = this.prevSize >= next.capacity;
        if (wasFull && next.size === 0 && next.lastMatchId) {
          clearInterval(interval);
          this.router.navigate(['/matches', next.lastMatchId]);
        }
        this.prevSize = next.size;
      } catch { /* ignore transient errors */ }
    }, 3000);
  }

  join() {
    const u = this.user();
    if (!u || this.busy()) return;
    this.busy.set(true);
    this.queue.join(u.displayName).subscribe({
      next: s => {
        // If joining brought us to capacity, navigate to the new match.
        if (s.size === 0 && s.lastMatchId) {
          this.router.navigate(['/matches', s.lastMatchId]);
        }
      },
      error: e => {
        if (e?.status === 409 && e?.error?.error === 'already_in_match') {
          alert("You're already in a match — finish it before queueing again.");
          this.queue.load().subscribe();
        } else {
          alert(e?.error?.error ?? 'Could not join queue.');
        }
        this.busy.set(false);
      },
      complete: () => this.busy.set(false)
    });
  }

  leave() {
    if (this.busy()) return;
    this.busy.set(true);
    this.queue.leave().subscribe({ complete: () => this.busy.set(false) });
  }

  async fillBots() {
    if (this.fillingBots()) return;
    this.fillingBots.set(true);
    this.adminMessage.set(null);
    try {
      const r = await this.admin.fillQueueWithBots(9);
      this.adminMessage.set(`Added ${r.added} test bot(s). Queue size: ${r.queueSize}/10. Click "Join queue" to pop.`);
      this.queue.load().subscribe();
    } catch (e: any) {
      this.adminMessage.set(e?.error?.error ?? 'Could not fill queue.');
    } finally {
      this.fillingBots.set(false);
    }
  }

  async kick(userId: string, displayName: string) {
    if (!confirm(`Kick ${displayName} from the queue?`)) return;
    try {
      await this.admin.kickFromQueue(userId);
      this.queue.load().subscribe();
    } catch (e: any) {
      alert(e?.error?.error ?? 'Could not kick player.');
    }
  }

  async clearBots() {
    if (this.fillingBots()) return;
    this.fillingBots.set(true);
    this.adminMessage.set(null);
    try {
      const r = await this.admin.clearTestBots();
      this.adminMessage.set(`Removed ${r.cancelled} test bot(s) from queue.`);
      this.queue.load().subscribe();
    } catch (e: any) {
      this.adminMessage.set(e?.error?.error ?? 'Could not clear bots.');
    } finally {
      this.fillingBots.set(false);
    }
  }
}
