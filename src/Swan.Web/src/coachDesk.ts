// The coach's current week as the server reports it (GET /api/coach/picks/current), shared by Home and Picks.

export type DeskGame = {
  teamId: string;
  team: string;
  gameId: string;
  opponent: string;
  isHome: boolean;
  kickoff: string;
  lockTime: string;
  locked: boolean;
  pointValue: number;
  selected: boolean;
};

export type DeskDog = {
  teamId: string;
  team: string;
  gameId: string;
  opponent: string;
  isHome: boolean;
  kickoff: string;
  lockTime: string;
  locked: boolean;
  spread: number;
  selected: boolean;
};

export type Desk = {
  state:
    | "open"
    | "no-membership"
    | "no-week"
    | "no-roster"
    | "roster-incomplete";
  seasonName: string | null;
  week: {
    id: string;
    number: number;
    deadline: string;
    isDoubleDogWeek: boolean;
    pickLimit: number;
    dogLimit: number;
  } | null;
  revision: number;
  serverTime?: string;
  games: DeskGame[];
  notPlaying: string[];
  dogs: DeskDog[];
};

export type Loaded =
  | { status: "loading" }
  | { status: "failed"; error: string }
  | { status: "ready"; desk: Desk };

export async function fetchDesk(): Promise<Loaded> {
  try {
    const response = await fetch("/api/coach/picks/current", {
      credentials: "include",
    });
    if (!response.ok)
      return {
        status: "failed",
        error: `The server answered ${response.status}.`,
      };
    return { status: "ready", desk: await response.json() };
  } catch {
    return { status: "failed", error: "Could not reach the server." };
  }
}

// Everything is shown in Eastern Time, whatever the browser's zone.
const timeFormat = new Intl.DateTimeFormat("en-US", {
  timeZone: "America/New_York",
  weekday: "short",
  hour: "numeric",
  minute: "2-digit",
});
const weekdayFormat = new Intl.DateTimeFormat("en-US", {
  timeZone: "America/New_York",
  weekday: "long",
});
const hourFormat = new Intl.DateTimeFormat("en-US", {
  timeZone: "America/New_York",
  hour: "numeric",
  hourCycle: "h23",
});

export const when = (iso: string) => timeFormat.format(new Date(iso));

// Team picks lock at the earlier of kickoff and the weekly deadline; dogs lock at kickoff. Say which one applies.
export function lockText(row: {
  kickoff: string;
  lockTime: string;
  locked: boolean;
}) {
  if (row.locked) return "Locked";
  return row.lockTime === row.kickoff
    ? `Locks at kickoff, ${when(row.lockTime)}`
    : `Locks at the weekly deadline, ${when(row.lockTime)}`;
}

// The slate's usual windows: Sunday splits at the 4 PM games and the night game; other days go by name.
export function kickoffWindow(iso: string) {
  const date = new Date(iso);
  const day = weekdayFormat.format(date);
  if (day !== "Sunday") return day;
  const hour = Number(hourFormat.format(date));
  return hour < 16
    ? "Sunday early"
    : hour < 19
      ? "Sunday late"
      : "Sunday night";
}

export const points = (value: number) =>
  `${value} ${value === 1 ? "point" : "points"}`;

// What each non-open state means for the coach, in one sentence.
export const closedMessages: Record<Exclude<Desk["state"], "open">, string> = {
  "no-membership":
    "You are not on a coach roster for a live season, so there is nothing for you to pick.",
  "no-week":
    "No week is open for picks right now. When the next week opens, your picks will be here.",
  "no-roster":
    "Your roster has not been set up yet, so there is nothing to pick from. The commissioner sets rosters before the season starts.",
  "roster-incomplete":
    "Your roster needs exactly nine teams before you can pick. If the draft is still open you can finish it yourself; after the deadline the commissioner has to complete it.",
};
