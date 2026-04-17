import { MatchDetail, MatchPlayer, MatchSummary, MmrHistoryPoint, Team } from '../models/match';
import { MOCK_LEADERBOARD } from './mock-data';

function pr(seed: number): number {
  const x = Math.sin(seed) * 10000;
  return x - Math.floor(x);
}

function buildPlayers(matchId: string, startIndex: number, withResult: boolean): MatchPlayer[] {
  return Array.from({ length: 10 }, (_, i) => {
    const leaderboard = MOCK_LEADERBOARD[(startIndex + i) % MOCK_LEADERBOARD.length];
    const team: Team = i < 5 ? 'Radiant' : 'Dire';
    const mmrBefore = leaderboard.mmr + Math.round((pr(startIndex + i) - 0.5) * 40);
    const delta = withResult ? Math.round(18 + pr(i + 11) * 14) : 0;
    const won = team === 'Radiant' ? (startIndex % 2 === 0) : (startIndex % 2 === 1);
    return {
      userId: leaderboard.userId,
      steamId64: leaderboard.steamId64,
      displayName: leaderboard.displayName,
      avatarUrl: null,
      team,
      mmrBefore,
      mmrAfter: withResult ? mmrBefore + (won ? delta : -delta) : null,
      joinedLobby: true,
      abandoned: false
    };
  });
}

function avg(players: MatchPlayer[], team: Team): number {
  const ofTeam = players.filter(p => p.team === team);
  return Math.round(ofTeam.reduce((s, p) => s + p.mmrBefore, 0) / ofTeam.length);
}

const now = Date.now();

const completedPlayers = buildPlayers('match-1', 0, true);
const abandonedPlayers = buildPlayers('match-2', 4, false).map((p, i) => ({
  ...p,
  joinedLobby: i !== 7,
  abandoned: i === 7
}));
const livePlayers = buildPlayers('match-3', 2, false);
const lobbyPlayers = buildPlayers('match-4', 6, false);
const draftPlayers = buildPlayers('match-5', 1, false);

export const MOCK_MATCHES: MatchDetail[] = [
  {
    id: 'match-1',
    seasonId: 'season-mock-1',
    seasonName: 'Pre-Season Test Cup',
    dotaMatchId: '7842193847',
    status: 'Completed',
    createdAt: new Date(now - 3 * 3600_000).toISOString(),
    startedAt: new Date(now - 3 * 3600_000 + 8 * 60_000).toISOString(),
    endedAt: new Date(now - 3 * 3600_000 + 52 * 60_000).toISOString(),
    durationSec: 44 * 60 + 37,
    radiantWin: true,
    radiantAvgMmr: avg(completedPlayers, 'Radiant'),
    direAvgMmr: avg(completedPlayers, 'Dire'),
    lobbyName: 'liga305-0001',
    lobbyPassword: 'l305a9f2c',
    botSteamName: 'Liga305Bot',
    players: completedPlayers
  },
  {
    id: 'match-3',
    seasonId: 'season-mock-1',
    seasonName: 'Pre-Season Test Cup',
    dotaMatchId: '7842198771',
    status: 'Live',
    createdAt: new Date(now - 40 * 60_000).toISOString(),
    startedAt: new Date(now - 32 * 60_000).toISOString(),
    endedAt: null,
    durationSec: null,
    radiantWin: null,
    radiantAvgMmr: avg(livePlayers, 'Radiant'),
    direAvgMmr: avg(livePlayers, 'Dire'),
    lobbyName: 'liga305-0003',
    lobbyPassword: 'l305d4b1e',
    botSteamName: 'Liga305Bot',
    players: livePlayers
  },
  {
    id: 'match-4',
    seasonId: 'season-mock-1',
    seasonName: 'Pre-Season Test Cup',
    dotaMatchId: null,
    status: 'Lobby',
    createdAt: new Date(now - 4 * 60_000).toISOString(),
    startedAt: null,
    endedAt: null,
    durationSec: null,
    radiantWin: null,
    radiantAvgMmr: avg(lobbyPlayers, 'Radiant'),
    direAvgMmr: avg(lobbyPlayers, 'Dire'),
    lobbyName: 'liga305-0004',
    lobbyPassword: 'l305b7e20',
    botSteamName: 'Liga305Bot',
    players: lobbyPlayers
  },
  {
    id: 'match-5',
    seasonId: 'season-mock-1',
    seasonName: 'Pre-Season Test Cup',
    dotaMatchId: null,
    status: 'Draft',
    createdAt: new Date(now - 40_000).toISOString(),
    startedAt: null,
    endedAt: null,
    durationSec: null,
    radiantWin: null,
    radiantAvgMmr: avg(draftPlayers, 'Radiant'),
    direAvgMmr: avg(draftPlayers, 'Dire'),
    lobbyName: null,
    lobbyPassword: null,
    botSteamName: null,
    players: draftPlayers
  },
  {
    id: 'match-2',
    seasonId: 'season-mock-1',
    seasonName: 'Pre-Season Test Cup',
    dotaMatchId: null,
    status: 'Abandoned',
    createdAt: new Date(now - 26 * 3600_000).toISOString(),
    startedAt: null,
    endedAt: new Date(now - 26 * 3600_000 + 6 * 60_000).toISOString(),
    durationSec: null,
    radiantWin: null,
    radiantAvgMmr: avg(abandonedPlayers, 'Radiant'),
    direAvgMmr: avg(abandonedPlayers, 'Dire'),
    lobbyName: 'liga305-0002',
    lobbyPassword: 'l305ab3e9',
    botSteamName: 'Liga305Bot',
    players: abandonedPlayers
  }
];

export function mockMmrHistory(): MmrHistoryPoint[] {
  let mmr = 1500;
  const points: MmrHistoryPoint[] = [];
  for (let i = 0; i < 22; i++) {
    const won = pr(i * 7) > 0.45;
    const delta = Math.round(18 + pr(i * 3) * 10);
    const before = mmr;
    mmr += won ? delta : -delta;
    points.push({
      matchId: `past-${i}`,
      at: new Date(now - (22 - i) * 18 * 3600_000).toISOString(),
      mmrBefore: before,
      mmrAfter: mmr,
      delta: won ? delta : -delta,
      radiantWin: pr(i) > 0.5,
      won
    });
  }
  return points;
}

export function matchSummaries(): MatchSummary[] {
  return MOCK_MATCHES.map(({ players, lobbyName, lobbyPassword, botSteamName, ...rest }) => rest);
}
