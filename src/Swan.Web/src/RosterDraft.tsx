import { useCallback, useEffect, useRef, useState } from "react";
import { formatDeadline, timeLeft } from "./draftTime";

type DraftTeam = {
  teamId: string;
  name: string;
  division: string;
  pointValue: number;
  selected: boolean;
};

type Desk = {
  state: "no-membership" | "open" | "locked" | "closed";
  seasonId: string | null;
  seasonName: string | null;
  deadline: string | null;
  serverTime: string;
  isLocked: boolean;
  complete: boolean;
  revision: number;
  teams: DraftTeam[];
};

type Loaded =
  | { status: "loading" }
  | { status: "failed"; error: string }
  | { status: "ready"; desk: Desk };

type Problem = { code: string; message: string };

const sameSet = (left: Set<string>, right: Set<string>) =>
  left.size === right.size && [...left].every((id) => right.has(id));

async function problemOf(response: Response): Promise<Problem> {
  try {
    const body = await response.json();
    if (body && typeof body.message === "string")
      return { code: String(body.code ?? ""), message: body.message };
  } catch {
    // fall through
  }
  return { code: "", message: `The server answered ${response.status}.` };
}

export default function RosterDraft() {
  const [loaded, setLoaded] = useState<Loaded>({ status: "loading" });
  const [chosen, setChosen] = useState<Set<string>>(new Set());
  const [problem, setProblem] = useState<Problem | null>(null);
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const returnToLock = useRef(false);

  const adopt = useCallback((desk: Desk) => {
    setLoaded({ status: "ready", desk });
    setChosen(
      new Set(desk.teams.filter((t) => t.selected).map((t) => t.teamId)),
    );
    setConfirming(false);
  }, []);

  const load = useCallback(async () => {
    try {
      const response = await fetch("/api/coach/roster", {
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

  // Cancelling the lock confirmation puts focus back on the button that opened it.
  useEffect(() => {
    if (!confirming && returnToLock.current) {
      document.getElementById("lock-roster")?.focus();
      returnToLock.current = false;
    }
  }, [confirming]);

  if (loaded.status === "loading")
    return (
      <section aria-busy="true" aria-labelledby="roster-title">
        <h1 id="roster-title">Your roster</h1>
        <p role="status">Loading your roster…</p>
      </section>
    );

  if (loaded.status === "failed")
    return (
      <section aria-labelledby="roster-title">
        <h1 id="roster-title">Your roster</h1>
        <div role="alert" className="notice problem">
          <strong>Could not load your roster.</strong> {loaded.error}
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

  if (desk.state === "no-membership")
    return (
      <section aria-labelledby="roster-title">
        <h1 id="roster-title">Your roster</h1>
        <div className="band">
          <h2>No roster to draft</h2>
          <p>
            You are not a coach in a live season, so there is no roster for you
            to choose.
          </p>
        </div>
      </section>
    );

  const seasonId = desk.seasonId!;
  const saved = new Set(
    desk.teams.filter((t) => t.selected).map((t) => t.teamId),
  );
  const dirty = !sameSet(chosen, saved);
  const editable = desk.state === "open";
  const divisions = [...new Set(desk.teams.map((t) => t.division))];
  const mine = desk.teams.filter((t) => saved.has(t.teamId));
  const stale =
    problem?.code === "roster.stale" || problem?.code === "season.stale";

  function toggle(id: string) {
    const next = new Set(chosen);
    if (!next.delete(id)) next.add(id);
    setChosen(next);
    setProblem(null);
    setNotice("");
    setConfirming(false);
  }

  async function call(
    path: string,
    method: string,
    body: object,
    done: string,
  ) {
    setBusy(true);
    setProblem(null);
    setNotice("");
    try {
      const response = await fetch(path, {
        method,
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      if (!response.ok) {
        setProblem(await problemOf(response));
        return;
      }
      adopt(await response.json());
      setNotice(done);
    } catch {
      setProblem({
        code: "",
        message: "Could not reach the server. Nothing here is saved yet.",
      });
    } finally {
      setBusy(false);
    }
  }

  const save = () =>
    call(
      "/api/coach/roster",
      "PUT",
      { seasonId, expectedRevision: desk.revision, teamIds: [...chosen] },
      "Roster saved.",
    );
  const lock = () =>
    call(
      "/api/coach/roster/lock",
      "POST",
      { seasonId, expectedRevision: desk.revision },
      "Roster locked.",
    );

  const header = (
    <>
      {desk.seasonName && <p className="eyebrow">{desk.seasonName}</p>}
      <h1 id="roster-title">{editable ? "Choose your nine" : "Your roster"}</h1>
    </>
  );

  if (!editable)
    return (
      <section aria-labelledby="roster-title">
        {header}
        <div className="band">
          {desk.state === "locked" && (
            <>
              <h2>Your roster is locked</h2>
              <p>
                You locked your nine teams. Only the commissioner can change
                them now.
                {desk.deadline &&
                  ` The draft closes ${formatDeadline(desk.deadline)}.`}
              </p>
            </>
          )}
          {desk.state === "closed" && desk.complete && (
            <>
              <h2>The draft is closed</h2>
              <p>
                {desk.deadline
                  ? `It closed ${formatDeadline(desk.deadline)}. `
                  : ""}
                These are your nine teams. Only the commissioner can change them
                now.
              </p>
            </>
          )}
          {desk.state === "closed" && !desk.complete && (
            <>
              <h2>Your roster is incomplete</h2>
              <p role="status">
                {desk.deadline
                  ? `The draft closed ${formatDeadline(desk.deadline)} `
                  : "The draft is closed "}
                with {mine.length} of 9 teams chosen. You cannot make weekly
                picks until the commissioner completes your roster.
              </p>
            </>
          )}
        </div>
        {mine.length > 0 && (
          <section aria-labelledby="mine-title" className="week-games">
            <h2 id="mine-title">Your teams</h2>
            <ol>
              {mine.map((team) => (
                <li key={team.teamId} className="game">
                  <p className="teams">{team.name}</p>
                  <p className="status">
                    {team.division} · {team.pointValue}{" "}
                    {team.pointValue === 1 ? "point" : "points"} if they win
                  </p>
                </li>
              ))}
            </ol>
          </section>
        )}
      </section>
    );

  const complete = chosen.size === 9;
  const savedComplete = desk.complete && !dirty;
  return (
    <div className="workspace desk">
      <section aria-labelledby="roster-title" className="ws-main">
        {header}
        <p className="lede">
          Choose exactly nine teams. You can change your mind until you lock the
          roster or the draft closes
          {desk.deadline ? ` on ${formatDeadline(desk.deadline)}` : ""}
          {desk.deadline
            ? ` (${timeLeft(desk.deadline, desk.serverTime)} left)`
            : ""}
          ; after that only the commissioner can change it.
        </p>
        <p role="status" className="notice-line">
          {notice}
        </p>
        <p className="count">
          {chosen.size} of 9 chosen
          {chosen.size < 9 ? ` · ${9 - chosen.size} to go` : " · ready to save"}
        </p>
        {divisions.map((division) => (
          <section
            key={division}
            aria-labelledby={`div-${division.replace(/\W+/g, "-")}`}
            className="week-games division-block"
          >
            <h2 id={`div-${division.replace(/\W+/g, "-")}`}>{division}</h2>
            <ol>
              {desk.teams
                .filter((t) => t.division === division)
                .map((team) => {
                  const on = chosen.has(team.teamId);
                  return (
                    <li
                      key={team.teamId}
                      className={`game pick${on ? " on" : ""}`}
                    >
                      <div className="game-line">
                        <p className="teams">{team.name}</p>
                        <p className="status">
                          {team.pointValue}{" "}
                          {team.pointValue === 1 ? "point" : "points"} if they
                          win
                        </p>
                        <div className="game-actions">
                          <button
                            type="button"
                            className={on ? "primary" : "ghost"}
                            aria-pressed={on}
                            disabled={busy || (!on && chosen.size >= 9)}
                            onClick={() => toggle(team.teamId)}
                          >
                            {on ? "Chosen" : "Choose"}
                            <span className="sr-only"> {team.name}</span>
                          </button>
                        </div>
                      </div>
                    </li>
                  );
                })}
            </ol>
          </section>
        ))}
      </section>

      <aside className="pulse" aria-labelledby="roster-summary">
        <h2 id="roster-summary">Your roster</h2>
        <dl className="counts">
          <dt>Chosen</dt>
          <dd>{chosen.size} of 9</dd>
          <dt>Saved</dt>
          <dd>{desk.complete ? "Yes, nine teams" : "Not yet"}</dd>
          {desk.deadline && (
            <>
              <dt>Closes</dt>
              <dd>{formatDeadline(desk.deadline)}</dd>
            </>
          )}
        </dl>
      </aside>

      <div className="dock" role="region" aria-label="Save roster">
        {confirming ? (
          <div
            role="group"
            aria-label="Confirm lock"
            className="confirm"
            onKeyDown={(event) => {
              if (event.key === "Escape") {
                returnToLock.current = true;
                setConfirming(false);
              }
            }}
          >
            <p>
              Lock your roster? You will not be able to change these nine teams
              afterwards; only the commissioner can.
            </p>
            <button
              type="button"
              className="primary"
              autoFocus
              disabled={busy}
              onClick={() => void lock()}
            >
              Confirm lock
            </button>
            <button
              type="button"
              className="ghost"
              onClick={() => {
                returnToLock.current = true;
                setConfirming(false);
              }}
            >
              Cancel
            </button>
          </div>
        ) : (
          <>
            <div className="dock-text">
              <strong>
                {dirty
                  ? "Unsaved changes"
                  : desk.complete
                    ? "Saved"
                    : "Nothing saved yet"}
              </strong>
              <span>{chosen.size} of 9 chosen</span>
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
                Reload saved roster
              </button>
            )}
            {savedComplete && (
              <button
                type="button"
                id="lock-roster"
                className="ghost"
                disabled={busy}
                onClick={() => setConfirming(true)}
              >
                Lock roster
              </button>
            )}
            <button
              type="button"
              className="primary"
              disabled={busy || !dirty || !complete}
              onClick={() => void save()}
            >
              {complete
                ? dirty
                  ? "Save roster"
                  : "Saved"
                : `Choose ${9 - chosen.size} more`}
            </button>
          </>
        )}
      </div>
    </div>
  );
}
