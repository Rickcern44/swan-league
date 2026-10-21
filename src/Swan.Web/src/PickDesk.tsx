import { Lock } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import {
  closedMessages,
  type Desk,
  type DeskGame,
  kickoffWindow,
  type Loaded,
  lockText,
  points,
  when,
} from "./coachDesk";
import { goTo, timeLeft } from "./draftTime";

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
  const [tab, setTab] = useState<"starts" | "dogs">("starts");

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
    const message = closedMessages[desk.state as keyof typeof closedMessages];
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
              Open your draft
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

  const startsLeft = Math.max(0, week.pickLimit - picks.size);
  const dogsLeft = Math.max(0, week.dogLimit - dogs.size);
  const left = startsLeft + dogsLeft;
  const now = desk.serverTime ?? new Date().toISOString();
  const deadlineLeft = timeLeft(week.deadline, now);
  // Starts in kickoff order, grouped by the slate's windows.
  const windows: { name: string; rows: DeskGame[] }[] = [];
  for (const row of desk.games) {
    const name = kickoffWindow(row.kickoff);
    const group = windows.find((entry) => entry.name === name);
    if (group) group.rows.push(row);
    else windows.push({ name, rows: [row] });
  }

  return (
    <div className="pick-desk">
      <header className="desk-head">
        {desk.seasonName && <p className="eyebrow">{desk.seasonName}</p>}
        <h1 id="desk-title">{`Week ${week.number} picks`}</h1>
        <p className="lede">
          {`Deadline ${when(week.deadline)} ET${deadlineLeft === "none" ? "" : ` · ${deadlineLeft} left`}. A team that kicks off earlier locks at kickoff.`}
        </p>
        <div className="pick-tabs" role="group" aria-label="Pick type">
          <button
            type="button"
            className={tab === "starts" ? "on" : ""}
            aria-pressed={tab === "starts"}
            onClick={() => setTab("starts")}
          >
            {`Starts ${picks.size} of ${week.pickLimit}`}
          </button>
          <button
            type="button"
            className={tab === "dogs" ? "on" : ""}
            aria-pressed={tab === "dogs"}
            onClick={() => setTab("dogs")}
          >
            {`Dogs ${dogs.size} of ${week.dogLimit}`}
          </button>
        </div>
      </header>
      <p role="status" className="notice-line">
        {notice}
      </p>

      {tab === "starts" && (
        <section aria-labelledby="starts-title" className="pick-list">
          <h2 id="starts-title">Your teams this week</h2>
          <p className="count">
            {picks.size} of {week.pickLimit} chosen
            {picks.size < week.pickLimit
              ? ` · ${week.pickLimit - picks.size} to go`
              : " · all set"}
          </p>
          <p className="hint">
            A win scores the team's point value; all three winning adds a +3
            Perfect Week.
          </p>
          {windows.map((group) => (
            <div key={group.name} className="pick-window">
              <h3>{group.name}</h3>
              <ol aria-label={group.name}>
                {group.rows.map((row) => {
                  const on = picks.has(row.teamId);
                  const matchup = row.isHome
                    ? `${row.opponent} at ${row.team}`
                    : `${row.team} at ${row.opponent}`;
                  return (
                    <li
                      key={row.teamId}
                      className={`pick-row${on ? " on" : ""}${row.locked ? " locked" : ""}${!on && !row.locked && full ? " full" : ""}`}
                    >
                      <span
                        className={`tier tier-${row.pointValue}`}
                        role="img"
                        aria-label={points(row.pointValue)}
                      >
                        {row.pointValue}
                      </span>
                      <div className="pick-text">
                        <p className="teams">
                          {row.team} <i>{row.isHome ? "vs" : "at"}</i>{" "}
                          {row.opponent}
                        </p>
                        <p className="kick">{when(row.kickoff)}</p>
                        <p className="status">
                          {points(row.pointValue)} if they win · {lockText(row)}
                          {row.locked && on ? " · locked in" : ""}
                        </p>
                      </div>
                      {row.locked ? (
                        <span className="locked-mark">
                          <Lock aria-hidden="true" size={16} />
                        </span>
                      ) : (
                        <button
                          type="button"
                          className={on ? "toggle on" : "toggle"}
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
                      )}
                    </li>
                  );
                })}
              </ol>
            </div>
          ))}
          {desk.games.length === 0 && <p>None of your teams play this week.</p>}
          {desk.notPlaying.length > 0 && (
            <p className="muted bye-note">
              Not playing this week: {desk.notPlaying.join(", ")}.
            </p>
          )}
        </section>
      )}

      {tab === "dogs" && (
        <section aria-labelledby="dogs-title" className="pick-list">
          <h2 id="dogs-title">Dog pick</h2>
          <p className="count">
            {week.isDoubleDogWeek
              ? `Choose ${week.dogLimit} dogs this week`
              : "Choose 1 dog"}{" "}
            · {dogs.size} chosen
          </p>
          <p className="hint dog-note">
            {week.isDoubleDogWeek
              ? "Double-dog week: pick two underdogs from two different games. "
              : ""}
            A dog that wins outright pays its spread, rounded up. Dogs lock at
            their own kickoff.
          </p>
          {desk.dogs.length === 0 && (
            <p>No dogs have been offered for this week yet.</p>
          )}
          <ol aria-label="Underdogs">
            {desk.dogs.map((row) => {
              const on = dogs.has(row.teamId);
              return (
                <li
                  key={row.teamId}
                  className={`pick-row${on ? " on" : ""}${row.locked ? " locked" : ""}${!on && !row.locked && dogsFull ? " full" : ""}`}
                >
                  <div className="pick-text">
                    <p className="teams">
                      <span className="spread">+{row.spread}</span> {row.team}{" "}
                      <i>{row.isHome ? "vs" : "at"}</i> {row.opponent}
                    </p>
                    <p className="kick">{when(row.kickoff)}</p>
                    <p className="status">
                      Scores {Math.ceil(row.spread)} if they win outright ·{" "}
                      {lockText(row)}
                      {row.locked && on ? " · locked in" : ""}
                    </p>
                  </div>
                  {row.locked ? (
                    <span className="locked-mark">
                      <Lock aria-hidden="true" size={16} />
                    </span>
                  ) : (
                    <button
                      type="button"
                      className={on ? "toggle on" : "toggle"}
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
                  )}
                </li>
              );
            })}
          </ol>
        </section>
      )}

      <div className="dock pick-dock" role="region" aria-label="Save picks">
        <div className="dock-text">
          <strong>
            {dirty
              ? "Unsaved changes"
              : desk.revision > 0
                ? "Saved"
                : "Nothing saved yet"}
          </strong>
          <span>{`${picks.size} of ${week.pickLimit} starts · ${dogs.size} of ${week.dogLimit} dogs`}</span>
          <span className={left > 0 ? "owed" : "complete"}>
            {left > 0 ? `${left} to go` : "Card complete"}
          </span>
          {nearest && <span>{`Next lock ${when(nearest.lockTime)}`}</span>}
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
