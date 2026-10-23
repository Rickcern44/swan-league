import {
  Dispatch,
  FormEvent,
  KeyboardEvent,
  SetStateAction,
  useCallback,
  useEffect,
  useRef,
  useState,
} from "react";
import PageState from "./PageState";
import { kindLabels, stageLabelsInOrder, stateLabel } from "./Swan";

type LatestLine = {
  spread: number;
  homeMoneyline: number | null;
  awayMoneyline: number | null;
  source: string;
  enteredAt: string;
  enteredBy: string;
};

type Game = {
  id: string;
  homeTeamId: string;
  awayTeamId: string;
  homeTeamName: string;
  awayTeamName: string;
  kickoff: string;
  excluded: boolean;
  approvedLine: number | null;
  lineHistory: number[];
  hasResult: boolean;
  homeScore: number | null;
  awayScore: number | null;
  latestLine?: LatestLine | null;
  proposedResult?: {
    homeScore: number;
    awayScore: number;
    source: string;
    updatedAt: string;
    reason: string | null;
  } | null;
};

type Week = {
  id: string;
  seasonId: string;
  number: number;
  kind: string;
  state: string;
  games: Game[];
  includedGames: number;
  linesApproved: number;
  resultsConfirmed: number;
  nextAction: { kind: string; label: string; detail: string; canAct: boolean };
  dogOptionTeamIds: string[];
  coachPickVisibilityOverride?: boolean;
  coachPickVisibilityOverrideReason?: string | null;
};

type Loaded =
  | { status: "loading" }
  | { status: "missing" }
  | { status: "failed"; error: string }
  | { status: "ready"; week: Week };

type Tab = "lines" | "dogs" | "picks" | "results" | "activity";
const tabs: { id: Tab; label: string }[] = [
  { id: "lines", label: "Lines" },
  { id: "dogs", label: "Dogs" },
  { id: "picks", label: "Picks" },
  { id: "results", label: "Results" },
  { id: "activity", label: "Activity" },
];

// Every call goes through here; the server's answer replaces the week, and a refusal comes back as its message.
type Send = (
  path: string,
  body?: unknown,
  method?: string,
) => Promise<{ ok: true } | { ok: false; message: string }>;

// The only calls the dock can make; each is an existing lifecycle endpoint and the server decides whether it is allowed.
const dockCalls: Record<string, string> = {
  "approve-lines": "lines/approve",
  publish: "publish",
  lock: "lock",
  finalize: "finalize",
  recompute: "recompute",
};

const statements: Record<string, (n: number) => string> = {
  Draft: (n) => `Review Week ${n} lines`,
  LinesApproved: (n) => `Publish Week ${n}`,
  Open: (n) => `Week ${n} is open for picks`,
  Locked: (n) => `Enter Week ${n} results`,
  Final: (n) => `Week ${n} is final`,
};

const plainNumber = /^[+-]?(\d+(\.\d*)?|\.\d+)$/;
const wholeNumber = /^\d+$/;
const americanOdds = /^[+-]?\d+$/;
const spreadProblem =
  "Enter the spread as a number from -30 to 30, such as -3.5.";

function parseSpread(text: string) {
  const trimmed = text.trim();
  const value = Number(trimmed);
  // Plain decimals only (no 1e3, 0x10 or Infinity), and no spread comes near 30. The server checks the same range.
  return plainNumber.test(trimmed) && Math.abs(value) <= 30 ? value : null;
}

const shortName = (name: string) =>
  name === "Unknown team" ? name : (name.split(" ").at(-1) ?? name);
const matchupOf = (game: Game) =>
  `${game.awayTeamName} at ${game.homeTeamName}`;
const signed = (value: number) => (value > 0 ? `+${value}` : `${value}`);

// Spread is stored from the home team's perspective: -3.5 means the home team is favored by 3.5.
function lineText(game: Game, value: number) {
  if (value === 0) return "Pick'em";
  return `${shortName(game.homeTeamName)} ${signed(value)}`;
}

const kickoffFormat = new Intl.DateTimeFormat("en-US", {
  timeZone: "America/New_York",
  weekday: "short",
  hour: "numeric",
  minute: "2-digit",
});
const momentFormat = new Intl.DateTimeFormat("en-US", {
  timeZone: "America/New_York",
  weekday: "short",
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit",
});
const moment = (instant: string) =>
  `${momentFormat.format(new Date(instant))} ET`;

type LineStatus = "excluded" | "approved" | "review" | "none";
function lineStatus(game: Game): LineStatus {
  if (game.excluded) return "excluded";
  const latest = game.lineHistory.at(-1);
  const awaiting =
    latest !== undefined &&
    (game.approvedLine === null || latest !== game.approvedLine);
  if (awaiting) return "review";
  if (game.approvedLine !== null) return "approved";
  return "none";
}
const statusLabels: Record<LineStatus, string> = {
  excluded: "Excluded",
  approved: "Approved",
  review: "Needs review",
  none: "No line yet",
};

async function readProblem(response: Response) {
  try {
    const body = await response.json();
    if (body && typeof body.message === "string") return body.message as string;
  } catch {
    // fall through to the status text below
  }
  return `The server answered ${response.status}.`;
}

function initialTab(state: string): Tab {
  const asked = new URLSearchParams(window.location.search).get("tab");
  if (tabs.some((tab) => tab.id === asked)) return asked as Tab;
  if (state === "Open") return "picks";
  if (state === "Locked" || state === "Final") return "results";
  return "lines";
}

type SeasonWeek = { id: string; number: number; kind: string; state: string };

export default function WeekWorkspace({
  weekId,
  onBack,
  onOpenWeek,
}: {
  weekId: string;
  onBack: () => void;
  onOpenWeek?: (weekId: string, tab: string) => void;
}) {
  const [loaded, setLoaded] = useState<Loaded>({ status: "loading" });
  const [tab, setTab] = useState<Tab | null>(null);
  const [notice, setNotice] = useState("");
  const [dockProblem, setDockProblem] = useState("");
  const [busy, setBusy] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [previewing, setPreviewing] = useState(false);
  const [seasonWeeks, setSeasonWeeks] = useState<SeasonWeek[]>([]);
  const returnToDock = useRef(false);

  const load = useCallback(async () => {
    try {
      const response = await fetch(`/api/admin/weeks/${weekId}`, {
        credentials: "include",
      });
      if (response.status === 404) return setLoaded({ status: "missing" });
      if (!response.ok)
        return setLoaded({
          status: "failed",
          error: `The server answered ${response.status}.`,
        });
      const week: Week = await response.json();
      setLoaded({ status: "ready", week });
      setTab((current) => current ?? initialTab(week.state));
    } catch {
      setLoaded({ status: "failed", error: "Could not reach the server." });
    }
  }, [weekId]);

  useEffect(() => {
    void load();
  }, [load]);

  // The picker needs the season's weeks; without them the workspace works as before, just without it.
  const seasonId = loaded.status === "ready" ? loaded.week.seasonId : null;
  useEffect(() => {
    if (!seasonId) return;
    let live = true;
    void (async () => {
      try {
        const response = await fetch(`/api/admin/seasons/${seasonId}/weeks`, {
          credentials: "include",
        });
        if (!response.ok) return;
        const weeks: SeasonWeek[] = await response.json();
        if (live && Array.isArray(weeks)) setSeasonWeeks(weeks);
      } catch {
        // no picker
      }
    })();
    return () => {
      live = false;
    };
  }, [seasonId]);

  // Cancelling the recompute confirmation puts focus back on the button that opened it.
  useEffect(() => {
    if (!confirming && returnToDock.current) {
      document.getElementById("dock-action")?.focus();
      returnToDock.current = false;
    }
  }, [confirming]);

  // Every action asks the server and then shows what the server returned; nothing is predicted here.
  const send: Send = useCallback(
    async (path, body, method = "POST") => {
      try {
        const response = await fetch(`/api/admin/weeks/${weekId}/${path}`, {
          method,
          credentials: "include",
          headers: { "Content-Type": "application/json" },
          body: body === undefined ? undefined : JSON.stringify(body),
        });
        if (!response.ok)
          return { ok: false, message: await readProblem(response) };
        setLoaded({ status: "ready", week: await response.json() });
        return { ok: true };
      } catch {
        return { ok: false, message: "Could not reach the server." };
      }
    },
    [weekId],
  );

  // Runs a panel's calls one at a time with the page marked busy.
  const run = useCallback(async <T,>(work: () => Promise<T>) => {
    setBusy(true);
    setNotice("");
    try {
      return await work();
    } finally {
      setBusy(false);
    }
  }, []);

  if (loaded.status === "loading")
    return (
      <section aria-labelledby="ws-title">
        <h1 id="ws-title">Week</h1>
        <PageState kind="loading" message="Loading the week…" />
      </section>
    );

  if (loaded.status === "missing")
    return (
      <section aria-labelledby="ws-title">
        <h1 id="ws-title">Week not found</h1>
        <p>This week does not exist, or the link is out of date.</p>
        <button type="button" className="ghost" onClick={onBack}>
          Back to the league desk
        </button>
      </section>
    );

  if (loaded.status === "failed")
    return (
      <section aria-labelledby="ws-title">
        <h1 id="ws-title">Week</h1>
        <PageState
          kind="error"
          title="Could not load this week."
          message={loaded.error}
          onRetry={() => {
            setLoaded({ status: "loading" });
            void load();
          }}
        />
      </section>
    );

  const { week } = loaded;
  const current = tab ?? initialTab(week.state);
  const next = week.nextAction;
  const call = dockCalls[next.kind];
  const actionLabel =
    next.kind === "publish" ? `Publish Week ${week.number}` : next.label;
  const stageIndex = stageLabelsInOrder.findIndex(
    (stage) => stage.state === week.state,
  );
  const preparing = week.state === "Draft" || week.state === "LinesApproved";
  const noDogs =
    week.state === "Draft" &&
    week.includedGames > 0 &&
    week.dogOptionTeamIds.length === 0;

  function chooseTab(id: Tab) {
    setTab(id);
    const url = new URL(window.location.href);
    url.searchParams.set("tab", id);
    window.history.replaceState(window.history.state, "", url);
  }

  function onTabKey(event: KeyboardEvent<HTMLDivElement>) {
    const index = tabs.findIndex((item) => item.id === current);
    const step =
      event.key === "ArrowRight" ? 1 : event.key === "ArrowLeft" ? -1 : 0;
    if (!step) return;
    event.preventDefault();
    const target = tabs[(index + step + tabs.length) % tabs.length];
    chooseTab(target.id);
    document.getElementById(`tab-${target.id}`)?.focus();
  }

  async function dock() {
    setDockProblem("");
    const result = await run(() => send(call));
    if (result.ok) setNotice(`${actionLabel}: done.`);
    else setDockProblem(result.message);
  }

  const say = (text: string) => setNotice(text);

  const here = seasonWeeks.findIndex((item) => item.id === week.id);
  const previous = here > 0 ? seasonWeeks[here - 1] : null;
  const following =
    here >= 0 && here < seasonWeeks.length - 1 ? seasonWeeks[here + 1] : null;
  const openWeek = (id: string) => onOpenWeek?.(id, current);

  return (
    <div className="week-desk">
      <div className="ws-top">
        <button type="button" className="back" onClick={onBack}>
          ‹ Today
        </button>
        {here >= 0 && onOpenWeek && (
          <nav className="week-picker" aria-label="Weeks">
            <button
              type="button"
              className="ghost small"
              disabled={!previous}
              aria-label={
                previous
                  ? `Previous week, Week ${previous.number}`
                  : "Previous week"
              }
              onClick={() => previous && openWeek(previous.id)}
            >
              ‹
            </button>
            <label htmlFor="week-select" className="sr-only">
              Week
            </label>
            <select
              id="week-select"
              value={week.id}
              onChange={(event) => openWeek(event.target.value)}
            >
              {seasonWeeks.map((item) => (
                <option key={item.id} value={item.id}>
                  Week {item.number} · {stateLabel(item.state)}
                </option>
              ))}
            </select>
            <button
              type="button"
              className="ghost small"
              disabled={!following}
              aria-label={
                following ? `Next week, Week ${following.number}` : "Next week"
              }
              onClick={() => following && openWeek(following.id)}
            >
              ›
            </button>
          </nav>
        )}
      </div>
      <header className="ws-head">
        <p className="eyebrow">
          Week {week.number} · {kindLabels[week.kind] ?? week.kind} ·{" "}
          {week.games.length} {week.games.length === 1 ? "game" : "games"}
        </p>
        <h1 id="ws-title">Week {week.number}</h1>
        <p className="lede">
          {statements[week.state]?.(week.number) ?? week.state}
        </p>
        <ol className="stage-pills" aria-label="Week stages">
          {stageLabelsInOrder.map((stage, index) => (
            <li
              key={stage.state}
              className={
                index < stageIndex ? "done" : index === stageIndex ? "now" : ""
              }
              aria-current={index === stageIndex ? "step" : undefined}
            >
              <span className="stage-num" aria-hidden="true">
                {index + 1}
              </span>
              {stage.label}
              {index === stageIndex && (
                <span className="sr-only">, current</span>
              )}
            </li>
          ))}
        </ol>
      </header>

      <p role="status" className="notice-line">
        {notice}
      </p>

      {previewing && preparing && (
        <CoachPreview week={week} onClose={() => setPreviewing(false)} />
      )}

      <div
        className="ws-tabs"
        role="tablist"
        aria-label={`Week ${week.number} sections`}
        onKeyDown={onTabKey}
      >
        {tabs.map((item) => (
          <button
            key={item.id}
            type="button"
            role="tab"
            id={`tab-${item.id}`}
            aria-selected={current === item.id}
            aria-controls={`panel-${item.id}`}
            tabIndex={current === item.id ? 0 : -1}
            onClick={() => chooseTab(item.id)}
          >
            {item.label}
            {item.id === "lines" && (
              <>
                {" "}
                <span className="tab-count">
                  {week.linesApproved}/{week.includedGames}
                </span>
              </>
            )}
            {item.id === "dogs" && (
              <>
                {" "}
                <span className="tab-count">
                  {week.dogOptionTeamIds.length}
                </span>
              </>
            )}
          </button>
        ))}
      </div>

      <div
        role="tabpanel"
        id={`panel-${current}`}
        aria-labelledby={`tab-${current}`}
        className="ws-panel"
      >
        {current === "lines" && (
          <LinesPanel week={week} busy={busy} send={send} run={run} say={say} />
        )}
        {current === "dogs" && (
          <DogsPanel week={week} busy={busy} send={send} run={run} say={say} />
        )}
        {current === "picks" && (
          <PicksPanel week={week} busy={busy} send={send} run={run} say={say} />
        )}
        {current === "results" && (
          <ResultsPanel
            week={week}
            busy={busy}
            send={send}
            run={run}
            say={say}
          />
        )}
        {current === "activity" && <ActivityPanel weekId={week.id} />}
      </div>

      <div className="dock ws-dock" role="region" aria-label="Next action">
        {!confirming && (
          <div className="dock-text">
            <strong>{call ? actionLabel : `Next: ${next.label}`}</strong>
            <span>{next.detail}</span>
            {noDogs && (
              <span className="dock-warn">
                No dog options yet. Generate them on the Dogs tab before
                approving the lines; they can't change afterwards.
              </span>
            )}
            {dockProblem && (
              <span role="alert" className="problem">
                {dockProblem}
              </span>
            )}
          </div>
        )}
        {preparing && !confirming && (
          <button
            type="button"
            className="ghost"
            aria-pressed={previewing}
            onClick={() => setPreviewing((open) => !open)}
          >
            Preview as coach
          </button>
        )}
        {next.canAct && call && !confirming && (
          <button
            type="button"
            id="dock-action"
            className={next.kind === "recompute" ? "ghost" : "primary"}
            disabled={busy}
            onClick={() => {
              setDockProblem("");
              if (next.kind === "recompute") setConfirming(true);
              else void dock();
            }}
          >
            {actionLabel}
          </button>
        )}
        {next.canAct && call && confirming && (
          <div
            role="group"
            aria-label="Confirm recompute"
            className="confirm"
            onKeyDown={(event) => {
              if (event.key === "Escape") {
                returnToDock.current = true;
                setConfirming(false);
              }
            }}
          >
            <p>
              Recompute Week {week.number}&apos;s scores? This replaces them
              using the current ballots and results.
            </p>
            <button
              type="button"
              className="primary"
              disabled={busy}
              autoFocus
              onClick={() => {
                setConfirming(false);
                void dock();
              }}
            >
              Confirm recompute
            </button>
            <button
              type="button"
              className="ghost"
              onClick={() => {
                returnToDock.current = true;
                setConfirming(false);
              }}
            >
              Cancel
            </button>
          </div>
        )}
      </div>
    </div>
  );
}

type PanelProps = {
  week: Week;
  busy: boolean;
  send: Send;
  run: <T>(work: () => Promise<T>) => Promise<T>;
  say: (text: string) => void;
};

function LinesPanel({ week, busy, send, run, say }: PanelProps) {
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [reviewOnly, setReviewOnly] = useState(false);
  const [problems, setProblems] = useState<string[]>([]);
  const [pasting, setPasting] = useState(false);
  const [excluding, setExcluding] = useState<string | null>(null);
  const editable = week.state === "Draft";
  const canExclude = week.state === "Draft" || week.state === "LinesApproved";

  const latestText = (game: Game) => {
    const latest = game.lineHistory.at(-1);
    return latest === undefined ? "" : String(latest);
  };
  const dirty = (game: Game) =>
    drafts[game.id] !== undefined &&
    drafts[game.id].trim() !== latestText(game);
  const selectable = (game: Game) =>
    editable &&
    !game.excluded &&
    (lineStatus(game) === "review" || dirty(game));
  const chosen = week.games.filter(
    (game) => selected.has(game.id) && selectable(game),
  );
  const candidates = week.games.filter(selectable);
  const reviewCount = week.games.filter(
    (game) => lineStatus(game) === "review" || lineStatus(game) === "none",
  ).length;
  const shown = reviewOnly
    ? week.games.filter(
        (game) =>
          lineStatus(game) === "review" ||
          lineStatus(game) === "none" ||
          dirty(game),
      )
    : week.games;

  function forget(ids: string[]) {
    setDrafts((current) => {
      const next = { ...current };
      for (const id of ids) delete next[id];
      return next;
    });
    setSelected((current) => {
      const next = new Set(current);
      for (const id of ids) next.delete(id);
      return next;
    });
  }

  async function saveOne(game: Game) {
    const spread = parseSpread(drafts[game.id] ?? "");
    if (spread === null) {
      setProblems([`${matchupOf(game)}: ${spreadProblem}`]);
      return;
    }
    setProblems([]);
    const result = await run(() =>
      send(`games/${game.id}/lines`, {
        spread,
        approve: false,
        source: "manual",
      }),
    );
    if (result.ok) {
      forget([game.id]);
      say("Line saved.");
    } else setProblems([`${matchupOf(game)}: ${result.message}`]);
  }

  async function approveSelected() {
    const failed: string[] = [];
    const done: string[] = [];
    await run(async () => {
      for (const game of chosen) {
        let result: Awaited<ReturnType<Send>>;
        if (dirty(game)) {
          const spread = parseSpread(drafts[game.id]);
          if (spread === null) {
            failed.push(`${matchupOf(game)}: ${spreadProblem}`);
            continue;
          }
          result = await send(`games/${game.id}/lines`, {
            spread,
            approve: true,
            source: "manual",
          });
        } else result = await send(`games/${game.id}/lines/approve-latest`);
        if (result.ok) done.push(game.id);
        else failed.push(`${matchupOf(game)}: ${result.message}`);
      }
    });
    forget(done);
    setProblems(failed);
    if (done.length > 0)
      say(`Approved ${done.length} ${done.length === 1 ? "line" : "lines"}.`);
  }

  const allChosen =
    candidates.length > 0 && candidates.every((game) => selected.has(game.id));

  return (
    <section aria-label="Lines" className="lines-panel">
      {editable && (
        <div className="lines-tools">
          <div className="lines-tools-left">
            <button
              type="button"
              className="ghost"
              aria-expanded={pasting}
              onClick={() => setPasting((open) => !open)}
            >
              Paste CSV
            </button>
            <button
              type="button"
              className={reviewOnly ? "ghost on" : "ghost"}
              aria-pressed={reviewOnly}
              onClick={() => setReviewOnly((on) => !on)}
            >
              Needs review ({reviewCount})
            </button>
          </div>
          <button
            type="button"
            className="primary"
            disabled={busy || chosen.length === 0}
            onClick={() => void approveSelected()}
          >
            Approve {chosen.length} selected
          </button>
        </div>
      )}
      {editable && pasting && (
        <CsvPanel
          week={week}
          busy={busy}
          send={send}
          run={run}
          onDone={(count, failed) => {
            setProblems(failed);
            if (count > 0) {
              say(`Saved ${count} ${count === 1 ? "line" : "lines"} from CSV.`);
              setPasting(false);
            }
          }}
          onCancel={() => setPasting(false)}
        />
      )}
      {problems.length > 0 && (
        <div role="alert" className="notice problem">
          {problems.map((text) => (
            <p key={text}>{text}</p>
          ))}
        </div>
      )}
      {week.games.length === 0 ? (
        <p>This week has no games.</p>
      ) : (
        <table className="lines-table" aria-label="Lines">
          <thead>
            <tr>
              {editable && (
                <th scope="col" className="pick-col">
                  <input
                    type="checkbox"
                    aria-label="Select all lines that need review"
                    checked={allChosen}
                    disabled={candidates.length === 0 || busy}
                    onChange={() =>
                      setSelected(
                        allChosen
                          ? new Set()
                          : new Set(candidates.map((game) => game.id)),
                      )
                    }
                  />
                </th>
              )}
              <th scope="col">Kickoff</th>
              <th scope="col">Game</th>
              <th scope="col">Home spread</th>
              <th scope="col">Moneyline (away / home)</th>
              <th scope="col">Source</th>
              <th scope="col">Status</th>
              {canExclude && (
                <th scope="col">
                  <span className="sr-only">Actions</span>
                </th>
              )}
            </tr>
          </thead>
          <tbody>
            {shown.map((game) => {
              const status = lineStatus(game);
              const matchup = matchupOf(game);
              const changed = dirty(game);
              const shownLine = game.lineHistory.at(-1) ?? null;
              const line = game.latestLine;
              return (
                <LineRow
                  key={game.id}
                  colSpan={(editable ? 1 : 0) + 6 + (canExclude ? 1 : 0)}
                  game={game}
                  week={week}
                  matchup={matchup}
                  busy={busy}
                  editable={editable}
                  canExclude={canExclude}
                  excluding={excluding === game.id}
                  onExclude={() => setExcluding(game.id)}
                  onExcluded={(text) => {
                    setExcluding(null);
                    say(text);
                  }}
                  onCancelExclude={() => setExcluding(null)}
                  send={send}
                  run={run}
                >
                  {editable && (
                    <td className="pick-col">
                      <input
                        type="checkbox"
                        aria-label={`Select ${matchup}`}
                        checked={selected.has(game.id) && selectable(game)}
                        disabled={!selectable(game) || busy}
                        onChange={() =>
                          setSelected((current) => {
                            const next = new Set(current);
                            if (!next.delete(game.id)) next.add(game.id);
                            return next;
                          })
                        }
                      />
                    </td>
                  )}
                  <td data-label="Kickoff">
                    {kickoffFormat.format(new Date(game.kickoff))}
                  </td>
                  <td data-label="Game" className="game-cell">
                    {matchup}
                  </td>
                  <td data-label="Home spread">
                    {editable && !game.excluded ? (
                      <span className="spread-edit">
                        <input
                          inputMode="decimal"
                          aria-label={`Spread for ${shortName(game.homeTeamName)} (${matchup})`}
                          placeholder="Add spread"
                          value={drafts[game.id] ?? latestText(game)}
                          disabled={busy}
                          onChange={(event) =>
                            setDrafts((current) => ({
                              ...current,
                              [game.id]: event.target.value,
                            }))
                          }
                        />
                        {changed && (
                          <button
                            type="button"
                            className="ghost small"
                            disabled={busy}
                            onClick={() => void saveOne(game)}
                          >
                            Save
                            <span className="sr-only"> line for {matchup}</span>
                          </button>
                        )}
                      </span>
                    ) : shownLine !== null && !game.excluded ? (
                      lineText(game, game.approvedLine ?? shownLine)
                    ) : (
                      "—"
                    )}
                  </td>
                  <td data-label="Moneyline">
                    {line &&
                    line.homeMoneyline !== null &&
                    line.awayMoneyline !== null
                      ? `${signed(line.awayMoneyline)} / ${signed(line.homeMoneyline)}`
                      : "—"}
                  </td>
                  <td data-label="Source">
                    {game.excluded
                      ? "Excluded by you"
                      : line
                        ? `${line.source === "csv" ? "CSV" : "Manual"} · ${kickoffFormat.format(new Date(line.enteredAt))}`
                        : "—"}
                  </td>
                  <td data-label="Status">
                    <span
                      className={`line-pill ${changed ? "unsaved" : status}`}
                    >
                      {changed ? "Unsaved" : statusLabels[status]}
                    </span>
                  </td>
                </LineRow>
              );
            })}
          </tbody>
        </table>
      )}
    </section>
  );
}

function LineRow({
  game,
  matchup,
  busy,
  canExclude,
  excluding,
  colSpan,
  onExclude,
  onExcluded,
  onCancelExclude,
  send,
  run,
  children,
}: {
  game: Game;
  week: Week;
  matchup: string;
  busy: boolean;
  editable: boolean;
  canExclude: boolean;
  excluding: boolean;
  colSpan: number;
  onExclude: () => void;
  onExcluded: (text: string) => void;
  onCancelExclude: () => void;
  send: Send;
  run: PanelProps["run"];
  children: React.ReactNode;
}) {
  const [reason, setReason] = useState("");
  const [problem, setProblem] = useState("");
  const verb = game.excluded ? "Include" : "Exclude";
  const triggerId = `exclude-${game.id}`;

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!reason.trim()) return setProblem("Give a reason for the change.");
    setProblem("");
    const result = await run(() =>
      send(`games/${game.id}/exclusion`, {
        excluded: !game.excluded,
        reason: reason.trim(),
      }),
    );
    if (result.ok) {
      setReason("");
      onExcluded(game.excluded ? "Game included." : "Game excluded.");
    } else setProblem(result.message);
  }

  function cancel() {
    setProblem("");
    onCancelExclude();
    queueMicrotask(() => document.getElementById(triggerId)?.focus());
  }

  return (
    <>
      <tr className={`line-row ${lineStatus(game)}`}>
        {children}
        {canExclude && (
          <td className="row-actions">
            {!excluding && (
              <button
                type="button"
                id={triggerId}
                className="ghost small"
                disabled={busy}
                onClick={onExclude}
              >
                {verb}
                <span className="sr-only"> {matchup}</span>
              </button>
            )}
          </td>
        )}
      </tr>
      {excluding && (
        <tr className="line-editor-row">
          <td colSpan={colSpan}>
            <form
              className="editor exclusion"
              aria-label={`${verb} ${matchup}`}
              noValidate
              onSubmit={(event) => void submit(event)}
              onKeyDown={(event) => event.key === "Escape" && cancel()}
            >
              <label htmlFor={`reason-${game.id}`}>
                Reason
                <small>Recorded in the week's activity.</small>
              </label>
              <input
                id={`reason-${game.id}`}
                value={reason}
                aria-invalid={problem ? true : undefined}
                aria-describedby={
                  problem ? `reason-problem-${game.id}` : undefined
                }
                autoFocus
                onChange={(event) => setReason(event.target.value)}
              />
              {problem && (
                <p
                  role="alert"
                  id={`reason-problem-${game.id}`}
                  className="problem"
                >
                  {problem}
                </p>
              )}
              <div className="editor-actions">
                <button type="submit" className="primary" disabled={busy}>
                  {verb} game
                </button>
                <button type="button" className="ghost" onClick={cancel}>
                  Cancel
                </button>
              </div>
            </form>
          </td>
        </tr>
      )}
    </>
  );
}

type CsvRow = {
  line: number;
  text: string;
  game?: Game;
  spread?: number;
  moneyline?: { home: number; away: number };
  problem?: string;
};

const normalize = (text: string) =>
  text.toLowerCase().replace(/\s+/g, " ").trim();
// A team is named by its full name, its city or its nickname.
function names(team: string, token: string) {
  const full = normalize(team);
  const given = normalize(token);
  return (
    given.length > 0 &&
    (full === given ||
      full.endsWith(` ${given}`) ||
      full.startsWith(`${given} `))
  );
}

function parseCsv(text: string, games: Game[]): CsvRow[] {
  const lines = text
    .split(/\r?\n/)
    .map((line, index) => ({ text: line.trim(), line: index + 1 }))
    .filter((line) => line.text.length > 0);
  const first = lines[0]?.text.split(",").map((cell) => cell.trim()) ?? [];
  if (
    first.length > 2 &&
    !plainNumber.test(first[2]) &&
    /spread/i.test(first[2])
  )
    lines.shift();
  return lines.map(({ text: raw, line }) => {
    const [away = "", home = "", spreadText = "", awayMl = "", homeMl = ""] =
      raw.split(",").map((cell) => cell.trim());
    const row: CsvRow = { line, text: `${away} at ${home}` };
    const game = games.find(
      (candidate) =>
        names(candidate.awayTeamName, away) &&
        names(candidate.homeTeamName, home),
    );
    if (!game)
      return {
        ...row,
        problem: `No game this week matches ${away} at ${home}`,
      };
    if (game.excluded)
      return { ...row, game, problem: "This game is excluded" };
    const spread = parseSpread(spreadText);
    if (spread === null)
      return {
        ...row,
        game,
        problem: "The spread must be a number from -30 to 30",
      };
    if (!awayMl && !homeMl) return { ...row, game, spread };
    if (
      !americanOdds.test(awayMl) ||
      !americanOdds.test(homeMl) ||
      Number(awayMl) === 0 ||
      Number(homeMl) === 0
    )
      return {
        ...row,
        game,
        spread,
        problem: "Moneylines must be whole American odds, like -140 or +120",
      };
    return {
      ...row,
      game,
      spread,
      moneyline: { home: Number(homeMl), away: Number(awayMl) },
    };
  });
}

function CsvPanel({
  week,
  busy,
  send,
  run,
  onDone,
  onCancel,
}: {
  week: Week;
  busy: boolean;
  send: Send;
  run: PanelProps["run"];
  onDone: (count: number, failed: string[]) => void;
  onCancel: () => void;
}) {
  const [text, setText] = useState("");
  const [rows, setRows] = useState<CsvRow[] | null>(null);
  const good = rows?.filter((row) => !row.problem && row.game) ?? [];

  async function save() {
    const failed: string[] = [];
    let saved = 0;
    await run(async () => {
      for (const row of good) {
        const game = row.game as Game;
        const result = await send(`games/${game.id}/lines`, {
          spread: row.spread,
          approve: false,
          source: "csv",
          ...(row.moneyline ? { moneyline: row.moneyline } : {}),
        });
        if (result.ok) saved++;
        else failed.push(`${matchupOf(game)}: ${result.message}`);
      }
    });
    onDone(saved, failed);
  }

  return (
    <section className="csv-panel" aria-label="Paste CSV">
      <label htmlFor="csv-text">
        CSV lines
        <small>
          One game per line: away team, home team, home spread, then optionally
          the away and home moneylines. A header row is skipped.
        </small>
      </label>
      <textarea
        id="csv-text"
        rows={5}
        value={text}
        placeholder="Jets,Patriots,-6.5,+240,-290"
        onChange={(event) => {
          setText(event.target.value);
          setRows(null);
        }}
      />
      <div className="editor-actions">
        <button
          type="button"
          disabled={!text.trim()}
          onClick={() => setRows(parseCsv(text, week.games))}
        >
          Preview CSV
        </button>
        {rows && (
          <button
            type="button"
            className="primary"
            disabled={busy || good.length === 0}
            onClick={() => void save()}
          >
            Save {good.length} {good.length === 1 ? "line" : "lines"}
          </button>
        )}
        <button type="button" className="ghost" onClick={onCancel}>
          Cancel
        </button>
      </div>
      {rows && (
        <table className="csv-preview" aria-label="CSV preview">
          <thead>
            <tr>
              <th scope="col">Row</th>
              <th scope="col">Game</th>
              <th scope="col">Line</th>
              <th scope="col">Moneyline (away / home)</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.line} className={row.problem ? "bad" : "good"}>
                <td>{row.line}</td>
                <td>{row.game ? matchupOf(row.game) : row.text}</td>
                <td>
                  {row.problem
                    ? row.problem
                    : row.game && row.spread !== undefined
                      ? lineText(row.game, row.spread)
                      : ""}
                </td>
                <td>
                  {row.moneyline
                    ? `${signed(row.moneyline.away)} / ${signed(row.moneyline.home)}`
                    : "—"}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}

// Each team of the week with its game and its spread from its own side.
function teamsOf(week: Week) {
  const teams = new Map<
    string,
    { name: string; game: Game; spread: number | null }
  >();
  for (const game of week.games) {
    const line = game.approvedLine ?? game.lineHistory.at(-1) ?? null;
    teams.set(game.homeTeamId, {
      name: game.homeTeamName,
      game,
      spread: line,
    });
    teams.set(game.awayTeamId, {
      name: game.awayTeamName,
      game,
      spread: line === null ? null : -line,
    });
  }
  return teams;
}

function DogsPanel({ week, busy, send, run, say }: PanelProps) {
  const [adding, setAdding] = useState("");
  const [problem, setProblem] = useState("");
  const editable = week.state === "Draft";
  const teams = teamsOf(week);
  const options = week.dogOptionTeamIds;
  const eligible = week.games
    .filter((game) => !game.excluded)
    .flatMap((game) => [game.awayTeamId, game.homeTeamId])
    .filter((id) => !options.includes(id));

  async function call(
    path: string,
    body: unknown,
    method: string,
    done: string,
  ) {
    setProblem("");
    const result = await run(() => send(path, body, method));
    if (result.ok) say(done);
    else setProblem(result.message);
    return result.ok;
  }

  return (
    <section aria-label="Dogs" className="dogs-panel">
      {editable ? (
        <div className="lines-tools">
          <p className="hint">
            Coaches pick their dog from this list. It builds from the approved
            lines and is fixed once the lines are approved.
          </p>
          <button
            type="button"
            disabled={busy}
            onClick={() =>
              void call(
                "dog-options/generate",
                undefined,
                "POST",
                "Dog options generated.",
              )
            }
          >
            Generate from approved lines
          </button>
        </div>
      ) : (
        <p className="hint">
          Dog options are fixed once the lines are approved.
        </p>
      )}
      {problem && (
        <p role="alert" className="problem">
          {problem}
        </p>
      )}
      {options.length === 0 ? (
        <p>No dog options yet.</p>
      ) : (
        <ul className="dog-list" aria-label="Dog options">
          {options.map((id) => {
            const team = teams.get(id);
            return (
              <li key={id}>
                <span className="dog-name">
                  {team?.name ?? "Unknown team"}
                  {team?.spread !== null && team?.spread !== undefined
                    ? ` ${signed(team.spread)}`
                    : ""}
                </span>
                {team && (
                  <span className="dog-game">{matchupOf(team.game)}</span>
                )}
                {editable && (
                  <button
                    type="button"
                    className="ghost small"
                    disabled={busy}
                    onClick={() =>
                      void call(
                        "dog-options",
                        { teamIds: options.filter((other) => other !== id) },
                        "PUT",
                        "Dog removed.",
                      )
                    }
                  >
                    Remove
                    <span className="sr-only">
                      {" "}
                      {team?.name ?? "Unknown team"}
                    </span>
                  </button>
                )}
              </li>
            );
          })}
        </ul>
      )}
      {editable && eligible.length > 0 && (
        <div className="dog-add">
          <label htmlFor="add-dog">Add a dog</label>
          <select
            id="add-dog"
            value={adding}
            onChange={(event) => setAdding(event.target.value)}
          >
            <option value="">Choose a team</option>
            {eligible.map((id) => {
              const team = teams.get(id);
              return (
                <option key={id} value={id}>
                  {team?.name}
                  {team?.spread !== null && team?.spread !== undefined
                    ? ` ${signed(team.spread)}`
                    : ""}
                </option>
              );
            })}
          </select>
          <button
            type="button"
            disabled={busy || !adding}
            onClick={() =>
              void call(
                "dog-options",
                { teamIds: [...options, adding] },
                "PUT",
                "Dog added.",
              ).then((ok) => ok && setAdding(""))
            }
          >
            Add dog
          </button>
        </div>
      )}
    </section>
  );
}

type AdminPick = {
  teamId: string;
  team: string;
  gameId: string;
  pointValue: number | null;
  spread: number | null;
  locked: boolean;
  result: string | null;
};
type CoachCard = {
  membershipId: string;
  name: string;
  starts: AdminPick[];
  dogs: AdminPick[];
  complete: boolean;
};

function useFetched<T>(url: string) {
  const [state, setState] = useState<
    | { status: "loading" }
    | { status: "failed"; error: string }
    | { status: "ready"; data: T }
  >({ status: "loading" });
  const load = useCallback(async () => {
    setState({ status: "loading" });
    try {
      const response = await fetch(url, { credentials: "include" });
      if (!response.ok)
        return setState({
          status: "failed",
          error: `The server answered ${response.status}.`,
        });
      setState({ status: "ready", data: await response.json() });
    } catch {
      setState({ status: "failed", error: "Could not reach the server." });
    }
  }, [url]);
  useEffect(() => {
    void load();
  }, [load]);

  return [state, load] as const;
}

function Loading({
  state,
  what,
  retry,
}: {
  state: { status: "loading" } | { status: "failed"; error: string };
  what: string;
  retry: () => void;
}) {
  if (state.status === "loading")
    return <PageState kind="loading" message={`Loading ${what}…`} />;
  return (
    <PageState
      kind="error"
      title={`Could not load ${what}.`}
      message={state.error}
      onRetry={retry}
    />
  );
}

function CoachPickOverride({
  coach,
  busy,
  send,
  run,
  say,
  onDone,
}: {
  coach: CoachCard;
  busy: boolean;
  send: Send;
  run: PanelProps["run"];
  say: PanelProps["say"];
  onDone: () => void;
}) {
  const [editing, setEditing] = useState(false);
  const [teamIds, setTeamIds] = useState(
    () => new Set(coach.starts.map((pick) => pick.teamId)),
  );
  const [dogTeamIds, setDogTeamIds] = useState(
    () => new Set(coach.dogs.map((pick) => pick.teamId)),
  );
  const [reason, setReason] = useState("");
  const [problem, setProblem] = useState("");
  const toggle = (set: Dispatch<SetStateAction<Set<string>>>, id: string) =>
    set((selected) => {
      const next = new Set(selected);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  async function save() {
    if (!reason.trim()) {
      setProblem("A commissioner override requires a reason.");
      return;
    }
    setProblem("");
    const result = await run(() =>
      send(
        `coaches/${coach.membershipId}/picks`,
        {
          teamIds: [...teamIds],
          dogTeamIds: [...dogTeamIds],
          reason: reason.trim(),
        },
        "PUT",
      ),
    );
    if (result.ok) {
      say(`${coach.name}'s picks were overridden.`);
      setEditing(false);
      onDone();
    } else setProblem(result.message);
  }
  if (!editing)
    return (
      <button
        type="button"
        className="ghost small"
        disabled={busy}
        onClick={() => setEditing(true)}
      >
        Override picks
      </button>
    );
  return (
    <form
      className="override-form"
      aria-label={`Override ${coach.name}'s picks`}
      onSubmit={(event) => {
        event.preventDefault();
        void save();
      }}
    >
      <p className="hint">
        Select the submitted picks that should remain, then record why this
        exceptional change is needed.
      </p>
      {coach.starts.map((pick) => (
        <label key={pick.teamId}>
          <input
            type="checkbox"
            checked={teamIds.has(pick.teamId)}
            disabled={busy}
            onChange={() => toggle(setTeamIds, pick.teamId)}
          />{" "}
          Start {pick.team}
        </label>
      ))}
      {coach.dogs.map((pick) => (
        <label key={pick.teamId}>
          <input
            type="checkbox"
            checked={dogTeamIds.has(pick.teamId)}
            disabled={busy}
            onChange={() => toggle(setDogTeamIds, pick.teamId)}
          />{" "}
          Dog {pick.team}
        </label>
      ))}
      <label>
        Reason
        <textarea
          value={reason}
          disabled={busy}
          onChange={(event) => setReason(event.target.value)}
        />
      </label>
      {problem && (
        <p role="alert" className="problem">
          {problem}
        </p>
      )}
      <div className="editor-actions">
        <button type="submit" className="primary" disabled={busy}>
          Save coach-pick override
        </button>
        <button
          type="button"
          className="ghost"
          disabled={busy}
          onClick={() => setEditing(false)}
        >
          Cancel
        </button>
      </div>
    </form>
  );
}

function PicksPanel({ week, busy, send, run, say }: PanelProps) {
  const [visibilityEnabled, setVisibilityEnabled] = useState(
    week.coachPickVisibilityOverride ?? false,
  );
  const [visibilityReason, setVisibilityReason] = useState(
    week.coachPickVisibilityOverrideReason ?? "",
  );
  const [visibilityProblem, setVisibilityProblem] = useState("");
  const [state, load] = useFetched<{ coaches: CoachCard[] }>(
    `/api/admin/weeks/${week.id}/picks`,
  );

  async function saveVisibility() {
    const reason = visibilityReason.trim();
    if (visibilityEnabled && !reason) {
      setVisibilityProblem("A visibility override requires a reason.");
      return;
    }
    setVisibilityProblem("");
    const result = await run(() =>
      send(
        "coach-pick-visibility",
        {
          enabled: visibilityEnabled,
          reason: visibilityEnabled ? reason : null,
        },
        "PUT",
      ),
    );
    if (result.ok)
      say(
        visibilityEnabled
          ? "Coach-pick visibility override saved."
          : "Coach-pick visibility override cleared.",
      );
    else setVisibilityProblem(result.message);
  }
  if (state.status !== "ready")
    return (
      <section aria-label="Picks">
        <Loading state={state} what="the picks" retry={() => void load()} />
      </section>
    );
  const { coaches } = state.data;
  const complete = coaches.filter((coach) => coach.complete).length;
  const pick = (item: AdminPick, dog: boolean) =>
    [
      dog && item.spread !== null
        ? `${item.team} ${signed(item.spread)}`
        : item.team,
      !dog && item.pointValue !== null
        ? `${item.pointValue} ${item.pointValue === 1 ? "pt" : "pts"}`
        : null,
      item.locked ? "locked" : null,
      item.result,
    ]
      .filter(Boolean)
      .join(" · ");
  return (
    <section aria-label="Picks" className="picks-panel">
      <form
        className="override-form"
        aria-label="Coach pick visibility"
        onSubmit={(event) => {
          event.preventDefault();
          void saveVisibility();
        }}
      >
        <h2>Coach-pick visibility</h2>
        <p className="hint">
          Use only for an exceptional week. Coaches can normally see other
          coaches’ picks only when the week rules allow it.
        </p>
        <label>
          <input
            type="checkbox"
            checked={visibilityEnabled}
            disabled={busy}
            onChange={(event) => setVisibilityEnabled(event.target.checked)}
          />{" "}
          Show coach picks early for this week
        </label>
        {visibilityEnabled && (
          <label>
            Reason
            <textarea
              value={visibilityReason}
              disabled={busy}
              onChange={(event) => setVisibilityReason(event.target.value)}
            />
          </label>
        )}
        {visibilityProblem && (
          <p role="alert" className="problem">
            {visibilityProblem}
          </p>
        )}
        <button type="submit" className="ghost" disabled={busy}>
          Save visibility override
        </button>
      </form>
      <p className="count">
        {complete} of {coaches.length} cards complete
      </p>
      {coaches.length === 0 ? (
        <p>No coaches are in this season.</p>
      ) : (
        <ul className="coach-cards" aria-label="Coach cards">
          {coaches.map((coach) => (
            <li
              key={coach.membershipId}
              className={coach.complete ? "complete" : "incomplete"}
            >
              <div className="card-head">
                <strong>{coach.name}</strong>
                <span
                  className={`line-pill ${coach.complete ? "approved" : "review"}`}
                >
                  {coach.complete ? "Complete" : "Incomplete"}
                </span>
              </div>
              {coach.starts.length + coach.dogs.length === 0 ? (
                <p className="muted-text">No picks yet</p>
              ) : (
                <>
                  {coach.starts.map((item) => (
                    <p key={`s-${item.teamId}`} className="pick-line">
                      <span className="pick-kind">Start</span>{" "}
                      {pick(item, false)}
                    </p>
                  ))}
                  {coach.dogs.map((item) => (
                    <p key={`d-${item.teamId}`} className="pick-line">
                      <span className="pick-kind">Dog</span> {pick(item, true)}
                    </p>
                  ))}
                </>
              )}
              <CoachPickOverride
                coach={coach}
                busy={busy}
                send={send}
                run={run}
                say={say}
                onDone={() => void load()}
              />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

type AuditEntry = {
  id: { value: string } | string;
  action: string;
  at: string;
  details: string;
};

const actionLabels: Record<string, string> = {
  "week.line.added": "Line entered",
  "week.line.approved": "Line approved",
  "week.lines.approved": "Lines approved",
  "week.published": "Week published",
  "week.locked": "Week locked",
  "week.result.confirmed": "Result entered",
  "week.finalized": "Week finalized",
  "week.recomputed": "Scores recomputed",
  "week.dog-options.generated": "Dog options generated",
  "week.dog-options.replaced": "Dog options changed",
  "week.game.exclusion.changed": "Game included or excluded",
};

function reasonOf(details: string) {
  try {
    const parsed = JSON.parse(details);
    return typeof parsed?.reason === "string" ? parsed.reason : null;
  } catch {
    return null;
  }
}

function ActivityPanel({ weekId }: { weekId: string }) {
  const [state, load] = useFetched<AuditEntry[]>(
    `/api/admin/audit?weekId=${weekId}`,
  );
  if (state.status !== "ready")
    return (
      <section aria-label="Activity">
        <Loading state={state} what="the activity" retry={() => void load()} />
      </section>
    );
  return (
    <section aria-label="Activity" className="activity-panel">
      {state.data.length === 0 ? (
        <p>Nothing has happened to this week yet.</p>
      ) : (
        <ol className="activity" aria-label="Week activity">
          {state.data.map((entry) => {
            const reason = reasonOf(entry.details);
            const key =
              typeof entry.id === "string" ? entry.id : entry.id.value;
            return (
              <li key={key}>
                <strong>{actionLabels[entry.action] ?? entry.action}</strong>
                <span className="when">{moment(entry.at)}</span>
                {reason && <span className="reason">{reason}</span>}
              </li>
            );
          })}
        </ol>
      )}
    </section>
  );
}

type ScoreDraft = { away: string; home: string };
const scoreProblem = "Enter both scores as whole numbers from 0 to 100.";
const parseScore = (text: string) => {
  const trimmed = text.trim();
  const value = Number(trimmed);
  return wholeNumber.test(trimmed) && value <= 100 ? value : null;
};

// Scores are entered in a table like the lines: both scores inline, Save per row or for every complete row.
function ResultsPanel({ week, busy, send, run, say }: PanelProps) {
  const [drafts, setDrafts] = useState<Record<string, ScoreDraft>>({});
  const [problems, setProblems] = useState<string[]>([]);
  const entering = week.state === "Locked";
  const proposed = week.games.filter((game) => game.proposedResult);

  async function importScores() {
    const result = await run(() => send("results/import"));
    if (result.ok) say("Imported scores are ready for review.");
    else setProblems([result.message]);
  }

  async function confirmDrafts() {
    const result = await run(() => send("results/confirm"));
    if (result.ok) say("Reviewed results confirmed and week finalized.");
    else setProblems([result.message]);
  }

  const saved = (game: Game): ScoreDraft => ({
    away: game.awayScore === null ? "" : String(game.awayScore),
    home: game.homeScore === null ? "" : String(game.homeScore),
  });
  const draftOf = (game: Game) => drafts[game.id] ?? saved(game);
  const dirty = (game: Game) => {
    const draft = drafts[game.id];
    if (!draft) return false;
    const was = saved(game);
    return draft.away.trim() !== was.away || draft.home.trim() !== was.home;
  };
  const complete = (game: Game) => {
    const draft = draftOf(game);
    return draft.away.trim() !== "" && draft.home.trim() !== "";
  };
  const ready = week.games.filter(
    (game) => !game.excluded && dirty(game) && complete(game),
  );

  function edit(game: Game, side: keyof ScoreDraft, value: string) {
    setDrafts((current) => ({
      ...current,
      [game.id]: { ...draftOf(game), [side]: value },
    }));
  }

  function revert(game: Game) {
    setDrafts((current) => {
      const next = { ...current };
      delete next[game.id];
      return next;
    });
  }

  async function save(games: Game[]) {
    const failed: string[] = [];
    const done: string[] = [];
    await run(async () => {
      for (const game of games) {
        const draft = draftOf(game);
        const away = parseScore(draft.away);
        const home = parseScore(draft.home);
        if (away === null || home === null) {
          failed.push(`${matchupOf(game)}: ${scoreProblem}`);
          continue;
        }
        const result = await send(`games/${game.id}/result`, {
          homeScore: home,
          awayScore: away,
        });
        if (result.ok) done.push(game.id);
        else failed.push(`${matchupOf(game)}: ${result.message}`);
      }
    });
    setDrafts((current) => {
      const next = { ...current };
      for (const id of done) delete next[id];
      return next;
    });
    setProblems(failed);
    if (done.length > 0)
      say(
        games.length === 1
          ? "Result saved."
          : `Saved ${done.length} ${done.length === 1 ? "result" : "results"}.`,
      );
  }

  return (
    <section aria-label="Results" className="results-panel">
      {entering ? (
        <div className="lines-tools">
          <p className="hint">
            Enter both scores for each game. Finalize the week once every
            included game has a result.
          </p>
          <button
            type="button"
            className="primary"
            disabled={busy || ready.length === 0}
            onClick={() => void save(ready)}
          >
            Save {ready.length} {ready.length === 1 ? "result" : "results"}
          </button>
          <button
            type="button"
            disabled={busy}
            onClick={() => void importScores()}
          >
            Import final scores
          </button>
          {proposed.length > 0 && (
            <button
              type="button"
              className="primary"
              disabled={busy}
              onClick={() => void confirmDrafts()}
            >
              Confirm {proposed.length} reviewed{" "}
              {proposed.length === 1 ? "result" : "results"}
            </button>
          )}
        </div>
      ) : (
        week.state !== "Final" && (
          <p className="hint">Results are entered once the week locks.</p>
        )
      )}
      {problems.length > 0 && (
        <div role="alert" className="notice problem">
          {problems.map((text) => (
            <p key={text}>{text}</p>
          ))}
        </div>
      )}
      {week.games.length === 0 ? (
        <p>This week has no games.</p>
      ) : (
        <table className="lines-table results-table" aria-label="Results">
          <thead>
            <tr>
              <th scope="col">Kickoff</th>
              <th scope="col">Game</th>
              <th scope="col">Score (away · home)</th>
              <th scope="col">Line</th>
              <th scope="col">Status</th>
              {entering && (
                <th scope="col">
                  <span className="sr-only">Actions</span>
                </th>
              )}
            </tr>
          </thead>
          <tbody>
            {week.games.map((game) => {
              const matchup = matchupOf(game);
              const away = shortName(game.awayTeamName);
              const home = shortName(game.homeTeamName);
              const draft = draftOf(game);
              const changed = dirty(game);
              const editable = entering && !game.excluded;
              const status = game.excluded
                ? "excluded"
                : changed
                  ? "unsaved"
                  : game.hasResult
                    ? "approved"
                    : "none";
              const label = game.excluded
                ? "Excluded"
                : changed
                  ? "Unsaved"
                  : game.hasResult
                    ? "Saved"
                    : "No result yet";
              const line = game.approvedLine ?? game.lineHistory.at(-1) ?? null;
              const proposedScore = game.proposedResult;
              return (
                <tr
                  key={game.id}
                  className={`line-row ${game.excluded ? "excluded" : ""}`}
                >
                  <td data-label="Kickoff">
                    {kickoffFormat.format(new Date(game.kickoff))}
                  </td>
                  <td data-label="Game" className="game-cell">
                    {matchup}
                  </td>
                  <td data-label="Score">
                    {editable ? (
                      <span
                        className="score-edit"
                        onKeyDown={(event) => {
                          if (event.key === "Escape") revert(game);
                        }}
                      >
                        <input
                          inputMode="numeric"
                          aria-label={`${away} score (${matchup})`}
                          value={draft.away}
                          disabled={busy}
                          onChange={(event) =>
                            edit(game, "away", event.target.value)
                          }
                        />
                        <span aria-hidden="true">·</span>
                        <input
                          inputMode="numeric"
                          aria-label={`${home} score (${matchup})`}
                          value={draft.home}
                          disabled={busy}
                          onChange={(event) =>
                            edit(game, "home", event.target.value)
                          }
                        />
                      </span>
                    ) : game.hasResult ? (
                      `${away} ${game.awayScore} · ${home} ${game.homeScore}`
                    ) : (
                      "—"
                    )}
                  </td>
                  <td data-label="Line">
                    {line === null || game.excluded
                      ? "—"
                      : lineText(game, line)}
                  </td>
                  <td data-label="Status">
                    <span className={`line-pill ${status}`}>{label}</span>
                    {proposedScore && (
                      <p className="hint">
                        Review: {away} {proposedScore.awayScore} · {home}{" "}
                        {proposedScore.homeScore} ({proposedScore.source})
                        {proposedScore.reason
                          ? ` — ${proposedScore.reason}`
                          : ""}
                      </p>
                    )}
                  </td>
                  {entering && (
                    <td className="row-actions">
                      {editable && changed && complete(game) && (
                        <button
                          type="button"
                          className="ghost small"
                          disabled={busy}
                          onClick={() => void save([game])}
                        >
                          Save
                          <span className="sr-only"> result for {matchup}</span>
                        </button>
                      )}
                    </td>
                  )}
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
    </section>
  );
}

// What coaches will see once the week is published: each included game with its line, then the dog list.
function CoachPreview({ week, onClose }: { week: Week; onClose: () => void }) {
  const teams = teamsOf(week);
  const included = week.games.filter((game) => !game.excluded);
  return (
    <section className="coach-preview" aria-label="Coach preview">
      <div className="preview-head">
        <h2>What coaches will see</h2>
        <button type="button" className="ghost small" onClick={onClose}>
          Close preview
        </button>
      </div>
      <h3>Games</h3>
      <ul>
        {included.map((game) => {
          const line = game.approvedLine ?? game.lineHistory.at(-1) ?? null;
          return (
            <li key={game.id}>
              {`${shortName(game.awayTeamName)} at ${shortName(game.homeTeamName)} · ${line === null ? "No line yet" : lineText(game, line)}`}
            </li>
          );
        })}
      </ul>
      <h3>Dogs</h3>
      {week.dogOptionTeamIds.length === 0 ? (
        <p>No dog options yet.</p>
      ) : (
        <ul>
          {week.dogOptionTeamIds.map((id) => {
            const team = teams.get(id);
            return (
              <li key={id}>
                {`${team?.name ?? "Unknown team"}${team?.spread !== null && team?.spread !== undefined ? ` ${signed(team.spread)}` : ""}`}
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
