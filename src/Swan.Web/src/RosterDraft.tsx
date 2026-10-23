import { useCallback, useEffect, useRef, useState } from "react";
import { formatDeadline, timeLeft } from "./draftTime";
import PageState from "./PageState";

type DraftTeam = {
  teamId: string;
  name: string;
  division: string;
  pointValue: number;
  selected: boolean;
};

type Desk = {
  state: "no-membership" | "unopened" | "open" | "locked" | "closed";
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
      <section aria-labelledby="roster-title">
        <h1 id="roster-title">Your roster</h1>
        <PageState kind="loading" message="Loading your roster…" />
      </section>
    );

  if (loaded.status === "failed")
    return (
      <section aria-labelledby="roster-title">
        <h1 id="roster-title">Your roster</h1>
        <PageState
          kind="error"
          title="Could not load your roster."
          message={loaded.error}
          onRetry={() => {
            setLoaded({ status: "loading" });
            void load();
          }}
        />
      </section>
    );

  const { desk } = loaded;

  if (desk.state === "no-membership")
    return (
      <section aria-labelledby="roster-title">
        <h1 id="roster-title">Your roster</h1>
        <PageState kind="empty" title="No roster to draft">
          <p>
            You are not a coach in a live season, so there is no roster for you
            to choose.
          </p>
        </PageState>
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

  const points = (n: number) => `${n} ${n === 1 ? "point" : "points"}`;
  const slug = (text: string) => text.replace(/\W+/g, "-");

  const header = (
    <header className="week-hero draft-hero">
      <p className="eyebrow">
        {desk.seasonName ? `${desk.seasonName} · Draft` : "Draft"}
      </p>
      <h1 id="roster-title">{editable ? "Choose your nine" : "Your roster"}</h1>
      {editable && (
        <p className="hero-line">
          Choose exactly nine teams. You can change your mind until you lock the
          roster or the draft closes
          {desk.deadline ? ` on ${formatDeadline(desk.deadline)}` : ""}
          {desk.deadline
            ? ` (${timeLeft(desk.deadline, desk.serverTime)} left)`
            : ""}
          ; after that only the commissioner can change it.
        </p>
      )}
      {editable && desk.deadline && (
        <div className="hero-clocks">
          <div className="clock">
            <span>Draft closes</span>
            <strong>{formatDeadline(desk.deadline)}</strong>
          </div>
          <div className="clock next">
            <span>Time left</span>
            <strong>{timeLeft(desk.deadline, desk.serverTime)}</strong>
          </div>
        </div>
      )}
    </header>
  );

  if (!editable)
    return (
      <section aria-labelledby="roster-title" className="draft-desk">
        {header}
        {desk.state === "locked" && (
          <PageState kind="empty" title="Your roster is locked">
            <p>
              You locked your nine teams. Only the commissioner can change them
              now.
              {desk.deadline &&
                ` The draft closes ${formatDeadline(desk.deadline)}.`}
            </p>
          </PageState>
        )}
        {desk.state === "unopened" && (
          <PageState kind="empty" title="The draft has not opened yet" live>
            <p>
              The commissioner has started the season and will open the roster
              draft when it is ready.
            </p>
          </PageState>
        )}
        {desk.state === "closed" && desk.complete && (
          <PageState kind="empty" title="The draft is closed">
            <p>
              {desk.deadline
                ? `It closed ${formatDeadline(desk.deadline)}. `
                : ""}
              These are your nine teams. Only the commissioner can change them
              now.
            </p>
          </PageState>
        )}
        {desk.state === "closed" && !desk.complete && (
          <PageState kind="empty" title="Your roster is incomplete" live>
            <p>
              {desk.deadline
                ? `The draft closed ${formatDeadline(desk.deadline)} `
                : "The draft is closed "}
              with {mine.length} of 9 teams chosen. You cannot make weekly picks
              until the commissioner completes your roster.
            </p>
          </PageState>
        )}
        {mine.length > 0 && (
          <section aria-labelledby="mine-title" className="draft-mine">
            <h2 id="mine-title">Your teams</h2>
            <ol className="tile-grid">
              {[...mine]
                .sort((a, b) => a.pointValue - b.pointValue)
                .map((team) => (
                  <li key={team.teamId} className="tile ro">
                    <span
                      className={`tier tier-${team.pointValue}`}
                      aria-hidden="true"
                    >
                      {team.pointValue}
                    </span>
                    <span className="tile-text">
                      <span className="teams">{team.name}</span>
                      <span className="status">
                        {team.division} · {points(team.pointValue)} if they win
                      </span>
                    </span>
                  </li>
                ))}
            </ol>
          </section>
        )}
      </section>
    );

  const complete = chosen.size === 9;
  const savedComplete = desk.complete && !dirty;
  const picked = desk.teams
    .filter((t) => chosen.has(t.teamId))
    .map((t) => t.pointValue)
    .sort((a, b) => a - b);
  const mix = [1, 2, 3, 4].map(
    (value) =>
      `${value} ${value === 1 ? "pt" : "pts"}: ${picked.filter((p) => p === value).length}`,
  );
  return (
    <div className="draft-desk">
      {header}
      <p role="status" className="notice-line">
        {notice}
      </p>
      <section aria-label="Your draft so far" className="draft-summary">
        <p className="count">
          {chosen.size} of 9 chosen
          {chosen.size < 9 ? ` · ${9 - chosen.size} to go` : " · ready to save"}
        </p>
        <div
          className="meter"
          role="img"
          aria-label={`${chosen.size} of 9 chosen`}
        >
          {Array.from({ length: 9 }, (_, i) => (
            <span
              // biome-ignore lint/suspicious/noArrayIndexKey: the nine slots are fixed.
              key={i}
              className={picked[i] ? `tier-${picked[i]}` : "empty"}
            />
          ))}
        </div>
        <p className="mix" aria-label="Point mix">
          {mix.map((text, i) => (
            <span key={text}>
              {i > 0 && " · "}
              {text}
            </span>
          ))}
        </p>
      </section>
      {divisions.map((division) => (
        <section
          key={division}
          aria-labelledby={`div-${slug(division)}`}
          className="division-block"
        >
          <h2 id={`div-${slug(division)}`}>{division}</h2>
          <ul className="tile-grid" aria-labelledby={`div-${slug(division)}`}>
            {desk.teams
              .filter((t) => t.division === division)
              .map((team) => {
                const on = chosen.has(team.teamId);
                return (
                  <li key={team.teamId}>
                    <button
                      type="button"
                      className={`tile${on ? " on" : ""}`}
                      aria-pressed={on}
                      aria-describedby={`about-${team.teamId}`}
                      disabled={busy || (!on && chosen.size >= 9)}
                      onClick={() => toggle(team.teamId)}
                    >
                      <span
                        className={`tier tier-${team.pointValue}`}
                        aria-hidden="true"
                      >
                        {team.pointValue}
                      </span>
                      <span className="sr-only">
                        {on ? "Chosen" : "Choose"}{" "}
                      </span>
                      <span className="teams">{team.name}</span>
                    </button>
                    <span id={`about-${team.teamId}`} className="sr-only">
                      {points(team.pointValue)} · {team.division}
                    </span>
                  </li>
                );
              })}
          </ul>
        </section>
      ))}

      <div className="dock" role="region" aria-label="Save draft">
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
              Lock these nine teams for the season? You can't change them after
              this; only the commissioner can correct a locked roster.
            </p>
            <button
              type="button"
              className="ghost"
              onClick={() => {
                returnToLock.current = true;
                setConfirming(false);
              }}
            >
              Keep editing
            </button>
            <button
              type="button"
              className="primary"
              autoFocus
              disabled={busy}
              onClick={() => void lock()}
            >
              Confirm lock
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
                  ? "Save draft"
                  : "Saved"
                : `Choose ${9 - chosen.size} more`}
            </button>
          </>
        )}
      </div>
    </div>
  );
}
