// Coach standings, read from GET /api/coach/standings. Totals only: no picks are ever exposed here.

export type Pool = "Main" | "Dog" | "RegularSeason" | "Playoff";

export const pools: { id: Pool; label: string }[] = [
  { id: "Main", label: "Main" },
  { id: "Dog", label: "Dog" },
  { id: "RegularSeason", label: "Regular season" },
  { id: "Playoff", label: "Playoff" },
];

export type StandingRow = {
  membershipId: string;
  rank: number;
  name: string;
  points: number;
  wins: number;
  isYou: boolean;
  movement: number | null;
};

export type StandingsData = {
  state: "ready" | "no-scores" | "no-membership";
  seasonName: string | null;
  pool: Pool;
  throughWeek: number | null;
  coachCount: number;
  rows: StandingRow[];
  you: (StandingRow & { behindLeader: number }) | null;
};

export type LoadedStandings =
  | { status: "loading" }
  | { status: "failed"; error: string }
  | { status: "ready"; data: StandingsData };

export async function fetchStandings(pool: Pool): Promise<LoadedStandings> {
  try {
    const response = await fetch(`/api/coach/standings?pool=${pool}`, {
      credentials: "include",
    });
    if (!response.ok)
      return {
        status: "failed",
        error: `The server answered ${response.status}.`,
      };
    return { status: "ready", data: await response.json() };
  } catch {
    return { status: "failed", error: "Could not reach the server." };
  }
}

// 1st, 2nd, 3rd, 4th … 11th, 12th, 13th … 21st.
export function ordinal(n: number) {
  const teen = n % 100 >= 11 && n % 100 <= 13;
  const suffix = teen
    ? "th"
    : (({ 1: "st", 2: "nd", 3: "rd" } as Record<number, string>)[n % 10] ??
      "th");
  return `${n}${suffix}`;
}

export const pts = (n: number) => `${n} ${n === 1 ? "pt" : "pts"}`;

export const poolLabel = (pool: Pool) =>
  pools.find((item) => item.id === pool)?.label ?? pool;
