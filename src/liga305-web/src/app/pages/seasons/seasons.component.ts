import { Component, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MOCK_ALL_SEASONS } from '../../core/admin-mock';
import { Season } from '../../models/season';

@Component({
  selector: 'app-seasons',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './seasons.component.html',
  styleUrl: './seasons.component.scss'
})
export class SeasonsComponent {
  readonly seasons = signal<Season[]>(MOCK_ALL_SEASONS);

  formatDate(iso: string): string {
    return new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
  }
}
