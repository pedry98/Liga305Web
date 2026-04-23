export type MatchStatus = 'Drafting' | 'Draft' | 'Lobby' | 'Live' | 'Completed' | 'Abandoned';
export type Team = 'Radiant' | 'Dire';

export interface MatchPlayer {
  userId: string;
  steamId64: string;
  displayName: string;
  avatarUrl: string | null;
  team: Team;
  mmrBefore: number;
  mmrAfter: number | null;
  joinedLobby: boolean;
  abandoned: boolean;
  kills: number | null;
  deaths: number | null;
  assists: number | null;
  pickOrder: number | null;   // 0 = captain, 1+ = pick number, null = unpicked
  isCaptain: boolean;
  isPicked: boolean;

  // Post-game stats. All nullable — only present for Completed matches settled
  // via OpenDota, and only when OpenDota actually reported the field.
  heroId: number | null;
  lastHits: number | null;
  denies: number | null;
  goldPerMin: number | null;
  xpPerMin: number | null;
  netWorth: number | null;
  heroDamage: number | null;
  towerDamage: number | null;
  heroHealing: number | null;
  items: (number | null)[];     // length 6
  backpack: (number | null)[];  // length 3
  itemNeutral: number | null;
  goldT: number[] | null;       // per-minute net worth; null if match isn't parsed
}

export interface MatchSummary {
  id: string;
  seasonId: string;
  seasonName: string;
  dotaMatchId: string | null;
  status: MatchStatus;
  createdAt: string;
  startedAt: string | null;
  endedAt: string | null;
  durationSec: number | null;
  radiantWin: boolean | null;
  radiantAvgMmr: number;
  direAvgMmr: number;
}

export interface MatchDetail extends MatchSummary {
  lobbyName: string | null;
  lobbyPassword: string | null;
  botSteamName: string | null;
  abandonsAt: string | null;
  radiantCaptainUserId: string | null;
  direCaptainUserId: string | null;
  currentPickerUserId: string | null;   // null when drafting is done
  currentPickerTeam: Team | null;

  // Per-minute Radiant advantage arrays from OpenDota (positive = Radiant lead).
  // Null when the match wasn't parsed.
  radiantGoldAdv: number[] | null;
  radiantXpAdv: number[] | null;

  players: MatchPlayer[];
}

export interface MmrHistoryPoint {
  matchId: string;
  at: string;
  mmrBefore: number;
  mmrAfter: number;
  delta: number;
  radiantWin: boolean | null;
  won: boolean;
}
