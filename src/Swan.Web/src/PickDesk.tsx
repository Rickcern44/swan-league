import { useCallback, useEffect, useState } from "react";
import { goTo } from "./draftTime";

type DeskGame = {
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

type DeskDog = {
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

type Desk = {
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
  games: DeskGame[];
  notPlaying: string[];
  dogs: DeskDog[];
};

type Loaded =
  | { status: "loading" }
  | { status: "failed"; error: string }
  | { status: "ready"; desk: Desk };

const timeFormat = new Intl.DateTimeFormat("en-US", {
  timeZone: "America/New_York",
  weekday: "short",
  hour: "numeric",
  minute: "2-digit",
});

const when = (iso: string) => timeFormat.format(new Date(iso));

// Team picks lock at the earlier of kickoff and the weekly deadline; dogs lock at kickoff. Say which one applies.
function lockText(row: { kickoff: string; lockTime: string; locked: boolean }) {
  if (row.locked) return "Locked";
  return row.lockTime === row.kickoff
    ? `Locks at kickoff, ${when(row.lockTime)}`
    : `Locks at the weekly deadline, ${when(row.lockTime)}`;
}

const sameSet = (left: Set<string>, right: Set<string>) =>
  left.size === right.size && [...left].every((id) => right.has(id));

async function problemText(response: Response) {
  try {
    const body = await response.json();
    if (body && typeof body.message === "string")
      return body as { code: string; message: string };
  } catch {
    // fall through
  }
  return { code: "", message: `The server answered ${response.status}.` };
}

export default function PickDesk() {
  const [loaded, setLoaded] = useState<Loaded>({ status: "loading" });
  const [picks, setPicks] = useState<Set<string>>(new Set());
  const [dogs, setDogs] = useState<Set<string>>(new Set());
  const [problem, setProblem] = useState<{
    code: string;
    message: string;
  } | null>(null);
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);

  const adopt = useCallback((desk: Desk) => {
    setLoaded({ status: "ready", desk });
    setPicks(
      new Set(desk.games.filter((g) => g.selected).map((g) => g.teamId)),
    );
    setDogs(new Set(desk.dogs.filter((d) => d.selected).map((d) => d.teamId)));
  }, []);

  const load = useCallback(async () => {
    try {
      const response = await fetch("/api/coach/picks/current", {
        credentials: "include",
      });
      if (!response.ok) {
        setLoaded({
          status: "failed",
          error: `The server answered ${response.status}.`,
        });
        return;
      }
      adopt(await response.json());
    } catch {
      setLoaded({ status: "failed", error: "Could not reach the server." });
    }
  }, [adopt]);

  useEffect(() => {
    void load();
  }, [load]);

  if (loaded.status === "loading")
    return (
      <section aria-busy="true" aria-labelledby="desk-title">
        <h1 id="desk-title">Your picks</h1>
        <p role="status">Loading your picks…</p>
      </section>
    );

  if (loaded.status === "failed")
    return (
      <section aria-labelledby="desk-title">
        <h1 id="desk-title">Your picks</h1>
        <div role="alert" className="notice problem">
          <strong>Could not load your picks.</strong> {loaded.error}
        </div>
        <button
          type="button"
          onClick={() => {
            setLoaded({ status: "loading" });
            void load();
          }}
        >
          Try again
        </button>
      </section>
    );

  const { desk } = loaded;

  if (desk.state !== "open" || !desk.week) {
    const message = {
      "no-membership":
        "You are not on a coach roster for a live season, so there is nothing for you to pick.",
      "no-week":
        "No week is open for picks right now. When the next week opens, your picks will be here.",
      "no-roster":
        "Your roster has not been set up yet, so there is nothing to pick from. The commissioner sets rosters before the season starts.",
      "roster-incomplete":
        "Your roster needs exactly nine teams before you can pick. If the draft is still open you can finish it yourself; after the deadline the commissioner has to complete it.",
      open: "",
    }[desk.state];
    return (
      <section aria-labelledby="desk-title">
        {desk.seasonName && <p className="eyebrow">{desk.seasonName}</p>}
        <h1 id="desk-title">Your picks</h1>
        <div className="band">
          <h2>Nothing to pick yet</h2>
          <p>{message}</p>
          {(desk.state === "no-roster" ||
            desk.state === "roster-incomplete") && (
            <button type="button" onClick={() => goTo("/roster")}>
              Open your roster
            </button>
          )}
          <button
            type="button"
            className="ghost"
            onClick={() => {
              setLoaded({ status: "loading" });
              void load();
            }}
          >
            Check again
          </button>
        </div>
      </section>
    );
  }

  const week = desk.week;
  const savedPicks = new Set(
    desk.games.filter((g) => g.selected).map((g) => g.teamId),
  );
  const savedDogs = new Set(
    desk.dogs.filter((d) => d.selected).map((d) => d.teamId),
  );
  const dirty = !sameSet(picks, savedPicks) || !sameSet(dogs, savedDogs);
  const full = picks.size >= week.pickLimit;
  const dogsFull = dogs.size >= week.dogLimit;

  function toggle(
    set: Set<string>,
    setter: (s: Set<string>) => void,
    id: string,
  ) {
    const next = new Set(set);
    if (!next.delete(id)) next.add(id);
    setter(next);
    setNotice("");
    setProblem(null);
  }

  async function save() {
    setBusy(true);
    setNotice("");
    setProblem(null);
    try {
      const response = await fetch("/api/coach/picks/current", {
        method: "PUT",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          weekId: week.id,
          expectedRevision: desk.revision,
          teamIds: [...picks],
          dogTeamIds: [...dogs],
        }),
      });
      if (!response.ok) {
        setProblem(await problemText(response));
        return;
      }
      adopt(await response.json());
      setNotice("Picks saved.");
    } catch {
      setProblem({
        code: "",
        message:
          "Could not reach the server. Your choices here are not saved yet.",
      });
    } finally {
      setBusy(false);
    }
  }

  const stale =
    problem?.code === "ballot.stale" ||
    problem?.code === "week.stale" ||
    problem?.code === "pick.unavailable";
  const nearest = [
    ...desk.games.filter((g) => picks.has(g.teamId)),
    ...desk.dogs.filter((d) => dogs.has(d.teamId)),
  ]
    .filter((row) => !row.locked)
    .sort((a, b) => a.lockTime.localeCompare(b.lockTime))[0];

  return (
    <div className="workspace desk">
      <section aria-labelledby="desk-title" className="ws-main">
        <p className="eyebrow">
          Week {week.number}
          {desk.seasonName ? ` · ${desk.seasonName}` : ""}
        </p>
        <h1 id="desk-title">Who are you starting?</h1>
        <p className="lede">
          Start up to {week.pickLimit} of your teams. The weekly deadline is{" "}
          {when(week.deadline)} ET; a team that kicks off earlier locks at
          kickoff.
        </p>
        <p role="status" className="notice-line">
          {notice}
        </p>

        <section aria-labelledby="starts-title" className="week-games">
          <h2 id="starts-title">Your teams this week</h2>
          <p className="count">
            {picks.size} of {week.pickLimit} chosen
            {picks.size < week.pickLimit
              ? ` · ${week.pickLimit - picks.size} to go`
              : " · all set"}
          </p>
          <ol>
            {desk.games.map((row) => {
              const on = picks.has(row.teamId);
              const matchup = row.isHome
                ? `${row.opponent} at ${row.team}`
                : `${row.team} at ${row.opponent}`;
              return (
                <li
                  key={row.teamId}
                  className={`game pick${on ? " on" : ""}${row.locked ? " locked" : ""}`}
                >
                  <div className="game-line">
                    <p className="kick">{when(row.kickoff)}</p>
                    <p className="teams">
                      {row.team} <i>{row.isHome ? "vs" : "at"}</i>{" "}
                      {row.opponent}
                    </p>
                    <p className="status">
                      {row.pointValue}{" "}
                      {row.pointValue === 1 ? "point" : "points"} if they win ·{" "}
                      {lockText(row)}
                      {row.locked && on ? " · locked in" : ""}
                    </p>
                    {!row.locked && (
                      <div className="game-actions">
                        <button
                          type="button"
                          className={on ? "primary" : "ghost"}
                          aria-pressed={on}
                          disabled={busy || (!on && full)}
                          onClick={() => toggle(picks, setPicks, row.teamId)}
                        >
                          {on ? "Starting" : "Start"}
                          <span className="sr-only">
                            {" "}
                            {row.team}, {matchup}
                          </span>
                        </button>
                      </div>
                    )}
                  </div>
                </li>
              );
            })}
          </ol>
          {desk.games.length === 0 && <p>None of your teams play this week.</p>}
          {desk.notPlaying.length > 0 && (
            <p className="muted">
              Not playing this week: {desk.notPlaying.join(", ")}.
            </p>
          )}
        </section>

        <section aria-labelledby="dogs-title" className="week-games dogs">
          <h2 id="dogs-title">Dog pick</h2>
          <p className="count">
            {week.isDoubleDogWeek
              ? `Choose ${week.dogLimit} dogs this week`
              : "Choose 1 dog"}{" "}
            · {dogs.size} chosen
          </p>
          {desk.dogs.length === 0 && (
            <p>No dogs have been offered for this week yet.</p>
          )}
          <ol>
            {desk.dogs.map((row) => {
              const on = dogs.has(row.teamId);
              return (
                <li
                  key={row.teamId}
                  className={`game pick${on ? " on" : ""}${row.locked ? " locked" : ""}`}
                >
                  <div className="game-line">
                    <p className="kick">{when(row.kickoff)}</p>
                    <p className="teams">
                      +{row.spread} {row.team} <i>{row.isHome ? "vs" : "at"}</i>{" "}
                      {row.opponent}
                    </p>
                    <p className="status">
                      Scores {Math.ceil(row.spread)} if they win outright ·{" "}
                      {lockText(row)}
                      {row.locked && on ? " · locked in" : ""}
                    </p>
                    {!row.locked && (
                      <div className="game-actions">
                        <button
                          type="button"
                          className={on ? "primary" : "ghost"}
                          aria-pressed={on}
                          disabled={busy || (!on && dogsFull)}
                          onClick={() => toggle(dogs, setDogs, row.teamId)}
                        >
                          {on ? "Dog picked" : "Pick dog"}
                          <span className="sr-only">
                            {" "}
                            {row.team},{" "}
                            {row.isHome
                              ? `${row.opponent} at ${row.team}`
                              : `${row.team} at ${row.opponent}`}
                          </span>
                        </button>
                      </div>
                    )}
                  </div>
                </li>
              );
            })}
          </ol>
        </section>
      </section>

      <aside className="pulse" aria-labelledby="ballot-title">
        <h2 id="ballot-title">Your ballot</h2>
        <dl className="counts">
          <dt>Starting</dt>
          <dd>
            {picks.size} of {week.pickLimit}
          </dd>
          <dt>Dogs</dt>
          <dd>
            {dogs.size} of {week.dogLimit}
          </dd>
          <dt>Next lock</dt>
          <dd>{nearest ? when(nearest.lockTime) : "Nothing chosen"}</dd>
        </dl>
      </aside>

      <div className="dock" role="region" aria-label="Save picks">
        <div className="dock-text">
          <strong>
            {dirty
              ? "Unsaved changes"
              : desk.revision > 0
                ? "Saved"
                : "Nothing saved yet"}
          </strong>
          <span>
            {picks.size} {picks.size === 1 ? "start" : "starts"} · {dogs.size}{" "}
            {dogs.size === 1 ? "dog" : "dogs"}
          </span>
          {problem && (
            <span role="alert" className="problem">
              {problem.message}
            </span>
          )}
        </div>
        {stale && (
          <button
            type="button"
            className="ghost"
            onClick={() => {
              setProblem(null);
              void load();
            }}
          >
            Reload saved picks
          </button>
        )}
        <button
          type="button"
          className="primary"
          disabled={busy || !dirty}
          onClick={() => void save()}
        >
          {dirty
            ? `Save ${picks.size} ${picks.size === 1 ? "pick" : "picks"}${dogs.size ? ` and ${dogs.size} ${dogs.size === 1 ? "dog" : "dogs"}` : ""}`
            : desk.revision > 0
              ? "Saved"
              : "Choose your picks"}
        </button>
      </div>
    </div>
  );
}
