import { Season } from '../models/season';
import { MOCK_LEADERBOARD } from './mock-data';

export interface AdminUser {
  id: string;
  steamId64: string;
  displayName: string;
  mmr: number;
  wins: number;
  losses: number;
  isAdmin: boolean;
  isBanned: boolean;
  joinedAt: string;
}

export const MOCK_ALL_SEASONS: Season[] = [
  {
    id: 'season-mock-1',
    name: 'Pre-Season Test Cup',
    startsAt: '2026-04-01T00:00:00Z',
    endsAt: '2026-06-30T23:59:59Z',
    isActive: true,
    playerCount: 24,
    matchCount: 18
  },
  {
    id: 'season-mock-0',
    name: 'Beta Trial',
    startsAt: '2026-01-10T00:00:00Z',
    endsAt: '2026-03-31T23:59:59Z',
    isActive: false,
    playerCount: 16,
    matchCount: 41
  },
  {
    id: 'season-mock-neg1',
    name: 'Friendlies',
    startsAt: '2025-10-01T00:00:00Z',
    endsAt: '2025-12-20T23:59:59Z',
    isActive: false,
    playerCount: 12,
    matchCount: 22
  }
];

export const MOCK_ADMIN_USERS: AdminUser[] = MOCK_LEADERBOARD.map((e, i) => ({
  id: e.userId,
  steamId64: e.steamId64,
  displayName: e.displayName,
  mmr: e.mmr,
  wins: e.wins,
  losses: e.losses,
  isAdmin: i === 0,
  isBanned: i === 19,
  joinedAt: new Date(Date.now() - (30 - i) * 86_400_000).toISOString()
}));
