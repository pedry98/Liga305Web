import { LeaderboardEntry, QueueEntry, QueueState, Season } from '../models/season';

export const MOCK_SEASON: Season = {
  id: 'season-mock-1',
  name: 'Pre-Season Test Cup',
  startsAt: '2026-04-01T00:00:00Z',
  endsAt: '2026-06-30T23:59:59Z',
  isActive: true,
  playerCount: 24,
  matchCount: 18
};

const NAMES = [
  'Pudge_Diff', 'StorminNorman', 'Crystal_Maiden', 'Juggermain',
  'Anti_Mage_GG', 'IO_Andy', 'Rubick_Steals', 'TinkerMode',
  'Riki_Gaming', 'Dazzle_Heals', 'NaturesBeast', 'PA_Crit',
  'Lina_Solo', 'Lich_King', 'Faceless_Void', 'TidehunterTime',
  'Treant_Daddy', 'Mirana_Arrow', 'CK_Press_R', 'Spectre_Shadow',
  'Zeus_Ulti', 'EarthSpirit', 'Bristleback_Tank', 'Slark_Fish'
];

const RANDOM_AVATARS = [
  'https://avatars.steamstatic.com/0000000000000000000000000000000000000000_full.jpg'
];

function pseudoRandom(seed: number): number {
  const x = Math.sin(seed) * 10000;
  return x - Math.floor(x);
}

export const MOCK_LEADERBOARD: LeaderboardEntry[] = NAMES.map((name, i) => {
  const mmr = Math.round(2400 - i * 35 + (pseudoRandom(i) - 0.5) * 80);
  const games = Math.round(8 + pseudoRandom(i + 100) * 30);
  const wins = Math.round(games * (0.35 + pseudoRandom(i + 200) * 0.3));
  return {
    rank: i + 1,
    userId: `user-${i + 1}`,
    steamId64: `7656119800000${String(i).padStart(4, '0')}`,
    displayName: name,
    avatarUrl: null,
    mmr,
    rd: 80 + Math.round(pseudoRandom(i + 300) * 40),
    wins,
    losses: games - wins,
    abandons: pseudoRandom(i + 400) > 0.85 ? 1 : 0
  };
});

const QUEUED_NAMES = NAMES.slice(0, 6);
export const MOCK_QUEUE: QueueState = {
  seasonId: MOCK_SEASON.id,
  size: QUEUED_NAMES.length,
  capacity: 10,
  selfInQueue: false,
  entries: QUEUED_NAMES.map<QueueEntry>((name, i) => ({
    userId: `user-${i + 1}`,
    displayName: name,
    avatarUrl: null,
    mmr: MOCK_LEADERBOARD[i].mmr,
    enqueuedAt: new Date(Date.now() - (QUEUED_NAMES.length - i) * 60_000).toISOString()
  }))
};
