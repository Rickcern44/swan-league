import { FormEvent, useCallback, useEffect, useState } from "react";
import { formatDeadline, goTo, timeLeft } from "./draftTime";

type Coach = {
  membershipId: string;
  name: string;
  email: string;
  status: "missing" | "incomplete" | "complete" | "locked";
  teamCount: number;
  teamIds: string[];
  teams: string[];
  isLocked: boolean;
  lockedAt: string | null;
  revision: number;
};

type Draft = {
  seasonId: string;
  seasonName: string;
  deadline: string | null;
  draftOpen: boolean;
  serverTime: string;
  teams: { teamId: string; name: string; division: string }[];
  coaches: Coach[];
};

type Loaded =
  | { status: "loading" }
  | { status: "forbidden"; message: string }
  | { status: "none"; message: string }
  | { status: "failed"; error: string }
  | { status: "ready"; draft: Draft };

const statusText: Record<Coach["status"], string> = {
  missing: "No roster yet",
  incomplete: "Incomplete",
  complete: "Complete, not locked",
  locked: "Locked",
};

async function problemOf(response: Response) {
  try {
    const body = await response.json();
    if (body && typeof body.message === "string") return body.message as string;
  } catch {
    // fall through
  }
  return `The server answered ${response.status}.`;
}

export default function CommissionerDraft() {
  const [loaded, setLoaded] = useState<Loaded>({ status: "loading" });
  const [editing, setEditing] = useState<string | null>(null);
  const [notice, setNotice] = useState("");

  const load = useCallback(async () => {
    try {
      const response = await fetch("/api/commissioner/roster-draft", {
        credentials: "include",
      });
      if (response.status === 403)
        return setLoaded({
          status: "forbidden",
          message: await problemOf(response),
        });
      if (response.status === 404)
        return setLoaded({
          status: "none",
          message: await problemOf(response),
        });
      if (!response.ok)
        return setLoaded({
          status: "failed",
          error: `The server answered ${response.status}.`,
        });
      setLoaded({ status: "ready", draft: await response.json() });
    } catch {
      setLoaded({ status: "failed", error: "Could not reach the server." });
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  if (loaded.status === "loading")
    return (
      <section aria-busy="true" aria-labelledby="draft-title">
        <h1 id="draft-title">Roster draft</h1>
        <p role="status">Loading the roster draft…</p>
      </section>
    );

  if (loaded.status === "failed")
    return (
      <section aria-labelledby="draft-title">
        <h1 id="draft-title">Roster draft</h1>
        <div role="alert" className="notice problem">
          <strong>Could not load the roster draft.</strong> {loaded.error}
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

  if (loaded.status === "forbidden")
    return (
      <section aria-labelledby="draft-title">
        <h1 id="draft-title">Roster draft</h1>
        <div className="band">
          <h2>Only the commissioner can manage rosters</h2>
          <p>
            {loaded.message} Being an administrator is not enough: this belongs
            to the season's own commissioner.
          </p>
        </div>
      </section>
    );

  if (loaded.status === "none")
    return (
      <section aria-labelledby="draft-title">
        <h1 id="draft-title">Roster draft</h1>
        <div className="band">
          <h2>No season is live yet</h2>
          <p>
            The roster draft starts when the season is started from Season
            setup.
          </p>
          <button type="button" onClick={() => goTo("/setup")}>
            Go to Season setup
          </button>
        </div>
      </section>
    );

  const { draft } = loaded;
  const counts = {
    locked: draft.coaches.filter((c) => c.status === "locked").length,
    complete: draft.coaches.filter((c) => c.status === "complete").length,
    incomplete: draft.coaches.filter((c) => c.status === "incomplete").length,
    missing: draft.coaches.filter((c) => c.status === "missing").length,
  };

  return (
    <section aria-labelledby="draft-title">
      <p className="eyebrow">{draft.seasonName}</p>
      <h1 id="draft-title">Roster draft</h1>
      <p className="lede">
        {draft.deadline
          ? draft.draftOpen
            ? `Coaches can choose and lock nine teams until ${formatDeadline(draft.deadline)} (${timeLeft(draft.deadline, draft.serverTime)} left).`
            : `The draft closed ${formatDeadline(draft.deadline)}. Only you can change a roster now.`
          : "No draft deadline was set for this season. Only you can change a roster."}
      </p>
      <p role="status" className="notice-line">
        {notice}
      </p>
      <p className="count">
        {counts.missing} missing · {counts.incomplete} incomplete ·{" "}
        {counts.complete} complete · {counts.locked} locked
      </p>
      <section aria-labelledby="coaches-title" className="week-games">
        <h2 id="coaches-title">Coaches</h2>
        {draft.coaches.length === 0 && (
          <p>No coaches have been added to this season.</p>
        )}
        <ol>
          {draft.coaches.map((coach) => (
            <li
              key={coach.membershipId}
              className={`game coach-${coach.status}`}
            >
              <div className="game-line">
                <p className="teams">{coach.name}</p>
                <p className="status">
                  <strong>{statusText[coach.status]}</strong>
                  {coach.teamCount > 0
                    ? ` · ${coach.teamCount} of 9 teams`
                    : ""}
                  {coach.status === "missing" || coach.status === "incomplete"
                    ? draft.draftOpen
                      ? " · still drafting"
                      : " · needs your correction"
                    : ""}
                </p>
                {coach.teams.length > 0 && (
                  <p className="muted">{coach.teams.join(", ")}</p>
                )}
                {editing !== coach.membershipId && (
                  <div className="game-actions">
                    <button
                      type="button"
                      id={`correct-${coach.membershipId}`}
                      className="ghost"
                      onClick={() => {
                        setEditing(coach.membershipId);
                        setNotice("");
                      }}
                    >
                      Correct roster
                      <span className="sr-only"> for {coach.name}</span>
                    </button>
                  </div>
                )}
              </div>
              {editing === coach.membershipId && (
                <Correction
                  draft={draft}
                  coach={coach}
                  onCancel={() => {
                    setEditing(null);
                    queueMicrotask(() =>
                      document
                        .getElementById(`correct-${coach.membershipId}`)
                        ?.focus(),
                    );
                  }}
                  onDone={(next) => {
                    setLoaded({ status: "ready", draft: next });
                    setEditing(null);
                    setNotice(`Corrected ${coach.name}'s roster.`);
                  }}
                  onStale={() => {
                    setEditing(null);
                    void load();
                  }}
                />
              )}
            </li>
          ))}
        </ol>
      </section>
    </section>
  );
}

function Correction({
  draft,
  coach,
  onCancel,
  onDone,
  onStale,
}: {
  draft: Draft;
  coach: Coach;
  onCancel: () => void;
  onDone: (draft: Draft) => void;
  onStale: () => void;
}) {
  const [remove, setRemove] = useState("");
  const [add, setAdd] = useState("");
  const [reason, setReason] = useState("");
  const [problem, setProblem] = useState("");
  const [stale, setStale] = useState(false);
  const [busy, setBusy] = useState(false);
  const full = coach.teamCount >= 9;
  const onRoster = new Set(coach.teamIds);
  const names = new Map(draft.teams.map((team) => [team.teamId, team.name]));

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (full && !remove)
      return setProblem(
        "Choose which team to replace: this roster already has nine.",
      );
    if (!add) return setProblem("Choose the team to add.");
    if (!reason.trim()) return setProblem("Give a reason for the correction.");
    setBusy(true);
    setProblem("");
    try {
      const response = await fetch(
        "/api/commissioner/roster-draft/corrections",
        {
          method: "POST",
          credentials: "include",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            seasonId: draft.seasonId,
            membershipId: coach.membershipId,
            expectedRevision: coach.revision,
            removeTeamId: remove || null,
            addTeamId: add,
            reason: reason.trim(),
          }),
        },
      );
      if (!response.ok) {
        setStale(response.status === 409);
        setProblem(await problemOf(response));
        return;
      }
      onDone(await response.json());
    } catch {
      setProblem("Could not reach the server. Nothing was changed.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <form
      className="editor"
      aria-label={`Correct ${coach.name}'s roster`}
      noValidate
      onSubmit={(event) => void submit(event)}
      onKeyDown={(event) => event.key === "Escape" && onCancel()}
    >
      <label htmlFor={`remove-${coach.membershipId}`}>
        Replace
        <small>
          {full
            ? "This roster has nine teams, so a team must be replaced."
            : "Leave blank to add a team to this short roster."}
        </small>
      </label>
      <select
        id={`remove-${coach.membershipId}`}
        value={remove}
        autoFocus
        onChange={(event) => setRemove(event.target.value)}
      >
        <option value="">
          {full ? "Choose a team to replace" : "Nothing: add a team"}
        </option>
        {coach.teamIds.map((id) => (
          <option key={id} value={id}>
            {names.get(id) ?? "Unknown team"}
          </option>
        ))}
      </select>
      <label htmlFor={`add-${coach.membershipId}`}>With</label>
      <select
        id={`add-${coach.membershipId}`}
        value={add}
        onChange={(event) => setAdd(event.target.value)}
      >
        <option value="">Choose a team to add</option>
        {draft.teams
          .filter((team) => !onRoster.has(team.teamId))
          .map((team) => (
            <option key={team.teamId} value={team.teamId}>
              {team.name}
            </option>
          ))}
      </select>
      <label htmlFor={`reason-${coach.membershipId}`}>
        Reason
        <small>Recorded with your name and the time in the audit log.</small>
      </label>
      <textarea
        id={`reason-${coach.membershipId}`}
        rows={2}
        value={reason}
        onChange={(event) => setReason(event.target.value)}
      />
      {problem && (
        <p role="alert" className="problem">
          {problem}
        </p>
      )}
      <div className="editor-actions">
        <button type="submit" className="primary" disabled={busy}>
          Save correction
        </button>
        {stale && (
          <button type="button" onClick={onStale}>
            Reload rosters
          </button>
        )}
        <button type="button" className="ghost" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </form>
  );
}
