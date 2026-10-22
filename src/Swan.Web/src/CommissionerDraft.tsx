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
  teams: {
    teamId: string;
    name: string;
    division: string;
    pointValue?: number | null;
  }[];
  coaches: Coach[];
  picksRemoved?: number;
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

type Filter = "not-locked" | "locked" | "all";

// The datetime-local value for a day N days after the server's Eastern date, at 8:00 PM Eastern.
function daysFromNow(serverTime: string, days: number) {
  const parts = Object.fromEntries(
    new Intl.DateTimeFormat("en-US", {
      timeZone: "America/New_York",
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
    })
      .formatToParts(new Date(serverTime))
      .map((part) => [part.type, part.value]),
  );
  const day = new Date(
    Date.UTC(Number(parts.year), Number(parts.month) - 1, Number(parts.day)),
  );
  day.setUTCDate(day.getUTCDate() + days);
  return `${day.toISOString().slice(0, 10)}T20:00`;
}

// A datetime-local value is already Eastern wall-clock time, so it is formatted as-is.
const wallClock = new Intl.DateTimeFormat("en-US", {
  timeZone: "UTC",
  weekday: "short",
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit",
});
const formatEastern = (local: string) => {
  const date = new Date(`${local}:00Z`);
  return Number.isNaN(date.getTime()) ? "" : `${wallClock.format(date)} ET`;
};

const plural = (n: number, one: string, many = `${one}s`) =>
  `${n} ${n === 1 ? one : many}`;

// Wide screens show the correction form in a side panel; phones keep it under the coach.
const WIDE = "(min-width: 64rem)";
function useWide() {
  const [wide, setWide] = useState(
    () =>
      typeof window.matchMedia === "function" &&
      window.matchMedia(WIDE).matches,
  );
  useEffect(() => {
    if (typeof window.matchMedia !== "function") return;
    const query = window.matchMedia(WIDE);
    const change = () => setWide(query.matches);
    query.addEventListener?.("change", change);
    return () => query.removeEventListener?.("change", change);
  }, []);
  return wide;
}

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
  const wide = useWide();
  const [notice, setNotice] = useState("");
  const [deadline, setDeadline] = useState("");
  const [startProblem, setStartProblem] = useState("");
  const [starting, setStarting] = useState(false);
  const [filter, setFilter] = useState<Filter>("not-locked");
  const [reminding, setReminding] = useState(false);
  const [remindProblem, setRemindProblem] = useState("");

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
        <h1 id="draft-title">Draft</h1>
        <p role="status">Loading the draft…</p>
      </section>
    );

  if (loaded.status === "failed")
    return (
      <section aria-labelledby="draft-title">
        <h1 id="draft-title">Draft</h1>
        <div role="alert" className="notice problem">
          <strong>Could not load the draft.</strong> {loaded.error}
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
        <h1 id="draft-title">Draft</h1>
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
        <h1 id="draft-title">Draft</h1>
        <div className="band">
          <h2>No season is live yet</h2>
          <p>The draft starts when the season is started from Season setup.</p>
          <button type="button" onClick={() => goTo("/commish/setup")}>
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

  async function startDraft(event: FormEvent) {
    event.preventDefault();
    if (!deadline) return setStartProblem("Choose the draft deadline first.");
    setStarting(true);
    setStartProblem("");
    try {
      const response = await fetch("/api/commissioner/roster-draft/start", {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          seasonId: draft.seasonId,
          rosterDraftDeadlineEastern: deadline,
        }),
      });
      if (!response.ok) return setStartProblem(await problemOf(response));
      setLoaded({ status: "ready", draft: await response.json() });
      setNotice("The draft is open.");
    } catch {
      setStartProblem("Could not reach the server. The draft was not opened.");
    } finally {
      setStarting(false);
    }
  }

  async function remind() {
    setReminding(true);
    setRemindProblem("");
    setNotice("");
    try {
      const response = await fetch("/api/commissioner/roster-draft/reminders", {
        method: "POST",
        credentials: "include",
      });
      if (!response.ok) return setRemindProblem(await problemOf(response));
      const { queued } = (await response.json()) as { queued: number };
      setNotice(
        queued === 0
          ? "Every coach has locked, so no reminders were needed."
          : `Reminder emails are on their way to ${plural(queued, "coach", "coaches")}.`,
      );
    } catch {
      setRemindProblem("Could not reach the server. No reminders were sent.");
    } finally {
      setReminding(false);
    }
  }

  const unlocked = draft.coaches.length - counts.locked;
  const shown = draft.coaches.filter((coach) =>
    filter === "all"
      ? true
      : filter === "locked"
        ? coach.status === "locked"
        : coach.status !== "locked",
  );
  const pill = draft.deadline
    ? draft.draftOpen
      ? {
          text: `Open · ${timeLeft(draft.deadline, draft.serverTime)} left`,
          tone: "open",
        }
      : { text: "Closed", tone: "closed" }
    : { text: "Not opened", tone: "unopened" };
  const tally = [
    { key: "locked", label: "Locked", count: counts.locked },
    { key: "complete", label: "9 saved, not locked", count: counts.complete },
    { key: "incomplete", label: "Short of 9", count: counts.incomplete },
    { key: "missing", label: "Not started", count: counts.missing },
  ];
  const filters: { key: Filter; label: string; count: number }[] = [
    { key: "not-locked", label: "Not locked", count: unlocked },
    { key: "locked", label: "Locked", count: counts.locked },
    { key: "all", label: "All", count: draft.coaches.length },
  ];

  const correcting = draft.coaches.find(
    (coach) => coach.membershipId === editing,
  );
  const correction = (coach: Coach) => (
    <Correction
      draft={draft}
      coach={coach}
      onCancel={() => {
        setEditing(null);
        queueMicrotask(() =>
          document.getElementById(`correct-${coach.membershipId}`)?.focus(),
        );
      }}
      onDone={(next) => {
        setLoaded({ status: "ready", draft: next });
        setEditing(null);
        setNotice(
          `Corrected ${coach.name}'s roster.${
            next.picksRemoved
              ? ` ${next.picksRemoved === 1 ? "Their pick of the removed team was" : `${next.picksRemoved} of their picks of the removed team were`} also taken off their ballot.`
              : ""
          }`,
        );
      }}
      onStale={() => {
        setEditing(null);
        void load();
      }}
    />
  );

  return (
    <section aria-labelledby="draft-title" className="commish-draft">
      <header className="draft-head">
        <div className="draft-head-line">
          <p className="eyebrow">{draft.seasonName} · Draft</p>
          <span className={`state-pill ${pill.tone}`}>{pill.text}</span>
        </div>
        <h1 id="draft-title">Draft</h1>
        <p className="lede">
          {draft.deadline
            ? draft.draftOpen
              ? `Coaches can choose and lock nine teams until ${formatDeadline(draft.deadline)} (${timeLeft(draft.deadline, draft.serverTime)} left).`
              : `The draft closed ${formatDeadline(draft.deadline)}. Only you can change a roster now.`
            : "No draft deadline was set for this season. Only you can change a roster."}
        </p>
      </header>
      {!draft.deadline && (
        <form
          className="editor open-draft"
          onSubmit={(event) => void startDraft(event)}
          noValidate
        >
          <h2>Open the draft</h2>
          <p>
            {plural(draft.coaches.length, "coach", "coaches")} will choose nine
            teams. Pick when the draft closes; times are Eastern Time.
          </p>
          <label>
            Draft deadline (Eastern Time)
            <input
              type="datetime-local"
              value={deadline}
              disabled={starting}
              onChange={(event) => {
                setDeadline(event.target.value);
                setStartProblem("");
              }}
            />
          </label>
          <div className="quick-picks" role="group" aria-label="Quick picks">
            {[3, 7].map((days) => (
              <button
                key={days}
                type="button"
                className="ghost"
                disabled={starting}
                onClick={() => {
                  setDeadline(daysFromNow(draft.serverTime, days));
                  setStartProblem("");
                }}
              >
                In {days} days
              </button>
            ))}
          </div>
          {deadline && formatEastern(deadline) && (
            <p className="consequence">
              Coaches can choose and lock their nine teams until{" "}
              {formatEastern(deadline)}. Once the draft is open you can't move
              the deadline; after it, only you can change a roster.
            </p>
          )}
          {startProblem && <p role="alert">{startProblem}</p>}
          <button type="submit" className="primary" disabled={starting}>
            Open draft
          </button>
        </form>
      )}
      <p role="status" className="notice-line">
        {notice}
      </p>
      {draft.coaches.length > 0 && (
        <div className="draft-tally">
          <div className="tally-bar" aria-hidden="true">
            {tally
              .filter((item) => item.count > 0)
              .map((item) => (
                <span
                  key={item.key}
                  className={`tally-${item.key}`}
                  style={{ flexGrow: item.count }}
                />
              ))}
          </div>
          <ul aria-label="Draft status" className="tally-list">
            {tally.map((item) => (
              <li key={item.key} className={`tally-${item.key}`}>
                <span>{item.label}</span>
                <strong>{item.count}</strong>
              </li>
            ))}
          </ul>
        </div>
      )}
      <div className="coach-tools">
        <div className="filter-chips" role="group" aria-label="Filter coaches">
          {filters.map((item) => (
            <button
              key={item.key}
              type="button"
              aria-pressed={filter === item.key}
              className={filter === item.key ? "on" : ""}
              onClick={() => setFilter(item.key)}
            >
              {item.label} <span className="chip-count">{item.count}</span>
            </button>
          ))}
        </div>
        {draft.draftOpen && unlocked > 0 && (
          <button
            type="button"
            className="ghost"
            disabled={reminding}
            onClick={() => void remind()}
          >
            Remind {plural(unlocked, "coach", "coaches")}
          </button>
        )}
      </div>
      {remindProblem && (
        <p role="alert" className="problem">
          {remindProblem}
        </p>
      )}
      <div className={`coach-area${wide && correcting ? " with-panel" : ""}`}>
        <section aria-labelledby="coaches-title" className="coach-list">
          <h2 id="coaches-title" className="sr-only">
            Coaches
          </h2>
          {draft.coaches.length === 0 && (
            <p>No coaches have been added to this season.</p>
          )}
          {draft.coaches.length > 0 && shown.length === 0 && (
            <p className="empty-filter">
              {filter === "locked"
                ? "No coach has locked a roster yet."
                : "Every coach has locked a roster."}
            </p>
          )}
          <ol>
            {shown.map((coach) => (
              <li
                key={coach.membershipId}
                className={`coach-row coach-${coach.status}${
                  editing === coach.membershipId ? " correcting" : ""
                }`}
              >
                <div className="coach-line">
                  <p className="coach-name">{coach.name}</p>
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
                    <p className="coach-teams">{coach.teams.join(", ")}</p>
                  )}
                  {editing !== coach.membershipId && (
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
                  )}
                </div>
                {editing === coach.membershipId && !wide && correction(coach)}
              </li>
            ))}
          </ol>
        </section>
        {wide && correcting && (
          <aside
            className="correction-panel"
            aria-label={`Correct ${correcting.name}'s roster`}
          >
            <h2>Correct {correcting.name}'s roster</h2>
            {correction(correcting)}
          </aside>
        )}
      </div>
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
  // Which field a problem is about, so the field can point at it; server problems belong to the whole form.
  const [field, setField] = useState<"remove" | "add" | "reason" | null>(null);
  const [stale, setStale] = useState(false);
  const [busy, setBusy] = useState(false);
  const full = coach.teamCount >= 9;
  const onRoster = new Set(coach.teamIds);
  const names = new Map(draft.teams.map((team) => [team.teamId, team.name]));
  const problemId = `correction-problem-${coach.membershipId}`;

  async function submit(event: FormEvent) {
    event.preventDefault();
    const refuse = (which: "remove" | "add" | "reason", text: string) => {
      setField(which);
      setProblem(text);
    };
    if (full && !remove)
      return refuse(
        "remove",
        "Choose which team to replace: this roster already has nine.",
      );
    if (!add) return refuse("add", "Choose the team to add.");
    if (!reason.trim())
      return refuse("reason", "Give a reason for the correction.");
    setBusy(true);
    setField(null);
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
        aria-invalid={field === "remove" || undefined}
        aria-describedby={field === "remove" ? problemId : undefined}
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
        aria-invalid={field === "add" || undefined}
        aria-describedby={field === "add" ? problemId : undefined}
        onChange={(event) => setAdd(event.target.value)}
      >
        <option value="">Choose a team to add</option>
        {draft.teams
          .filter((team) => !onRoster.has(team.teamId))
          .map((team) => (
            <option key={team.teamId} value={team.teamId}>
              {team.name} · {team.division}
              {team.pointValue
                ? ` · ${team.pointValue} ${team.pointValue === 1 ? "pt" : "pts"}`
                : ""}
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
        aria-invalid={field === "reason" || undefined}
        aria-describedby={field === "reason" ? problemId : undefined}
        onChange={(event) => setReason(event.target.value)}
      />
      {problem && (
        <p role="alert" id={problemId} className="problem">
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
