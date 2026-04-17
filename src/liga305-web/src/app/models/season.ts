export interface Season {
  id: string;
  name: string;
  startsAt: string;
  endsAt: string;
  isActive: boolean;
  playerCount: number;
  matchCount: number;
}

export interface LeaderboardEntry {
  rank: number;
  userId: string;
  steamId64: string;
  displayName: string;
  avatarUrl: string | null;
  mmr: number;
  rd: number;
  wins: number;
  losses: number;
  abandons: number;
}

export interface QueueState {
  seasonId: string;
  size: number;
  capacity: number;
  entries: QueueEntry[];
  selfInQueue: boolean;
  lastMatchId?: string | null;
}

export interface QueueEntry {
  userId: string;
  displayName: string;
  avatarUrl: string | null;
  mmr: number;
  enqueuedAt: string;
}
