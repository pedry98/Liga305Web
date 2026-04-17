import { Routes } from '@angular/router';
import { HomeComponent } from './pages/home/home.component';
import { ProfileComponent } from './pages/profile/profile.component';
import { LeaderboardComponent } from './pages/leaderboard/leaderboard.component';
import { QueueComponent } from './pages/queue/queue.component';
import { MatchesComponent } from './pages/matches/matches.component';
import { MatchDetailComponent } from './pages/match-detail/match-detail.component';
import { SeasonsComponent } from './pages/seasons/seasons.component';
import { AdminComponent } from './pages/admin/admin.component';

export const routes: Routes = [
  { path: '', component: HomeComponent },
  { path: 'leaderboard', component: LeaderboardComponent },
  { path: 'queue', component: QueueComponent },
  { path: 'matches', component: MatchesComponent },
  { path: 'matches/:id', component: MatchDetailComponent },
  { path: 'seasons', component: SeasonsComponent },
  { path: 'profile', component: ProfileComponent },
  { path: 'admin', component: AdminComponent },
  { path: '**', redirectTo: '' }
];
