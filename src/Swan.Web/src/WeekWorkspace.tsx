import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { StateChip, kindLabels, stageLabelsInOrder } from "./Swan";

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
};

type Week = {
  id: string;
  number: number;
  kind: string;
  state: string;
  games: Game[];
  includedGames: number;
  linesApproved: number;
  resultsConfirmed: number;
  nextAction: { kind: string; label: string; detail: string; canAct: boolean };
};

type Loaded =
  | { status: "loading" }
  | { status: "missing" }
  | { status: "failed"; error: string }
  | { status: "ready"; week: Week };

type Problem = { scope: string; message: string };
type Editing = { gameId: string; mode: "line" | "result" };

// The only calls the dock can make; each is an existing lifecycle endpoint and the server decides whether it is allowed.
const dockCalls: Record<string, string> = {
  "approve-lines": "lines/approve",
  publish: "publish",
  lock: "lock",
  finalize: "finalize",
  recompute: "recompute",
};

const shortName = (name: string) =>
  name === "Unknown team" ? name : (name.split(" ").at(-1) ?? name);

// Spread is stored from the home team's perspective: -3.5 means the home team is favored by 3.5.
function lineText(game: Game, value: number) {
  const home = shortName(game.homeTeamName);
  if (value === 0) return "Pick'em";
  return `${home} ${value > 0 ? "+" : "-"}${Math.abs(value)}`;
}

const kickoffFormat = new Intl.DateTimeFormat("en-US", {
  timeZone: "America/New_York",
  weekday: "short",
  hour: "numeric",
  minute: "2-digit",
});

async function readProblem(response: Response) {
  try {
    const body = await response.json();
    if (body && typeof body.message === "string") return body.message as string;
  } catch {
    // fall through to the status text below
  }
  return `The server answered ${response.status}.`;
}

export default function WeekWorkspace({
  weekId,
  onBack,
}: {
  weekId: string;
  onBack: () => void;
}) {
  const [loaded, setLoaded] = useState<Loaded>({ status: "loading" });
  const [editing, setEditing] = useState<Editing | null>(null);
  const [problem, setProblem] = useState<Problem | null>(null);
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const trigger = useRef<string | null>(null);

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
      setLoaded({ status: "ready", week: await response.json() });
    } catch {
      setLoaded({ status: "failed", error: "Could not reach the server." });
    }
  }, [weekId]);

  useEffect(() => {
    void load();
  }, [load]);

  // After an editor closes, focus goes back to the control that opened it.
  useEffect(() => {
    if (editing === null && trigger.current) {
      document.getElementById(trigger.current)?.focus();
      trigger.current = null;
    }
  }, [editing]);

  // Every action asks the server and then shows what the server returned; nothing is predicted here.
  async function act(
    scope: string,
    path: string,
    body: unknown,
    done: string,
    onSaved?: () => void,
  ) {
    setBusy(true);
    setNotice("");
    try {
      const response = await fetch(`/api/admin/weeks/${weekId}/${path}`, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: body === undefined ? undefined : JSON.stringify(body),
      });
      if (!response.ok) {
        setProblem({ scope, message: await readProblem(response) });
        return;
      }
      setProblem(null);
      setLoaded({ status: "ready", week: await response.json() });
      setNotice(done);
      onSaved?.();
    } catch {
      setProblem({ scope, message: "Could not reach the server." });
    } finally {
      setBusy(false);
    }
  }

  if (loaded.status === "loading")
    return (
      <section aria-busy="true" aria-labelledby="ws-title">
        <h1 id="ws-title">Week</h1>
        <p role="status">Loading the week…</p>
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
        <div role="alert" className="notice problem">
          <strong>Could not load this week.</strong> {loaded.error}
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

  const { week } = loaded;
  const next = week.nextAction;
  const call = dockCalls[next.kind];
  const dockProblem = problem?.scope === "dock" ? problem.message : "";

  return (
    <div className="workspace">
      <section aria-labelledby="ws-title" className="ws-main">
        <button type="button" className="back" onClick={onBack}>
          ‹ League desk
        </button>
        <header className="ws-head">
          <p className="eyebrow">{kindLabels[week.kind] ?? week.kind}</p>
          <h1 id="ws-title">
            <span className="numeral">Week {week.number}</span>
          </h1>
          <StateChip state={week.state} />
        </header>

        <p role="status" className="notice-line">
          {notice}
        </p>

        <section aria-labelledby="games-title" className="week-games">
          <h2 id="games-title">Games</h2>
          {week.games.length === 0 && <p>This week has no games.</p>}
          <ol>
            {week.games.map((game) => (
              <GameRow
                key={game.id}
                game={game}
                state={week.state}
                busy={busy}
                editing={editing?.gameId === game.id ? editing.mode : null}
                problem={problem?.scope === game.id ? problem.message : ""}
                onOpen={(mode, id) => {
                  trigger.current = id;
                  setProblem(null);
                  setNotice("");
                  setEditing({ gameId: game.id, mode });
                }}
                onClose={() => {
                  setProblem(null);
                  setEditing(null);
                }}
                onApproveLatest={() =>
                  void act(
                    game.id,
                    `games/${game.id}/lines/approve-latest`,
                    undefined,
                    "Line approved.",
                  )
                }
                onSaveLine={(spread, approve) =>
                  act(
                    game.id,
                    `games/${game.id}/lines`,
                    { spread, approve },
                    approve ? "Line saved and approved." : "Line saved.",
                    () => setEditing(null),
                  )
                }
                onSaveResult={(homeScore, awayScore) =>
                  act(
                    game.id,
                    `games/${game.id}/result`,
                    { homeScore, awayScore },
                    "Result saved.",
                    () => setEditing(null),
                  )
                }
              />
            ))}
          </ol>
        </section>
      </section>

      <aside className="pulse" aria-labelledby="pulse-title">
        <h2 id="pulse-title">Week pulse</h2>
        <ol className="stages" aria-label="Week stages">
          {stageLabelsInOrder.map((stage) => (
            <li
              key={stage.state}
              aria-current={stage.state === week.state ? "step" : undefined}
            >
              {stage.label}
              {stage.state === week.state && (
                <span className="here"> · current</span>
              )}
            </li>
          ))}
        </ol>
        <dl className="counts">
          <dt>Lines approved</dt>
          <dd>
            {week.linesApproved} / {week.includedGames}
          </dd>
          <dt>Results confirmed</dt>
          <dd>
            {week.resultsConfirmed} / {week.includedGames}
          </dd>
        </dl>
      </aside>

      <div className="dock" role="region" aria-label="Next action">
        <div className="dock-text">
          <strong>{call ? next.label : `Next: ${next.label}`}</strong>
          <span>{next.detail}</span>
          {dockProblem && (
            <span role="alert" className="problem">
              {dockProblem}
            </span>
          )}
        </div>
        {next.canAct && call && (
          <button
            type="button"
            className={next.kind === "recompute" ? "ghost" : "primary"}
            disabled={busy}
            onClick={() =>
              void act("dock", call, undefined, `${next.label}: done.`)
            }
          >
            {next.label}
          </button>
        )}
      </div>
    </div>
  );
}

function GameRow({
  game,
  state,
  busy,
  editing,
  problem,
  onOpen,
  onClose,
  onApproveLatest,
  onSaveLine,
  onSaveResult,
}: {
  game: Game;
  state: string;
  busy: boolean;
  editing: "line" | "result" | null;
  problem: string;
  onOpen: (mode: "line" | "result", triggerId: string) => void;
  onClose: () => void;
  onApproveLatest: () => void;
  onSaveLine: (spread: number, approve: boolean) => Promise<void>;
  onSaveResult: (home: number, away: number) => Promise<void>;
}) {
  const latest = game.lineHistory.at(-1);
  const awaiting =
    latest !== undefined &&
    (game.approvedLine === null || latest !== game.approvedLine);
  const matchup = `${game.awayTeamName} at ${game.homeTeamName}`;
  const lineId = `line-${game.id}`;
  const resultId = `result-${game.id}`;

  let status: string;
  if (game.excluded) status = "Excluded from this week";
  else if (game.hasResult)
    status = `${shortName(game.awayTeamName)} ${game.awayScore} · ${shortName(game.homeTeamName)} ${game.homeScore}`;
  else if (game.approvedLine !== null && !awaiting)
    status = `${lineText(game, game.approvedLine)} · approved`;
  else if (awaiting)
    status = `${lineText(game, latest as number)} · awaiting approval`;
  else status = "No line yet";

  return (
    <li className={game.excluded ? "game excluded" : "game"}>
      <div className="game-line">
        <p className="kick">{kickoffFormat.format(new Date(game.kickoff))}</p>
        <p className="teams">
          <span className="sr-only">{matchup}</span>
          <span aria-hidden="true">
            {shortName(game.awayTeamName)} <i>@</i>{" "}
            {shortName(game.homeTeamName)}
          </span>
        </p>
        <p className="status">{status}</p>
        {!game.excluded && editing === null && (
          <div className="game-actions">
            {state === "Draft" && awaiting && (
              <button
                type="button"
                className="ghost"
                disabled={busy}
                onClick={onApproveLatest}
              >
                Approve line<span className="sr-only"> for {matchup}</span>
              </button>
            )}
            {state === "Draft" && (
              <button
                type="button"
                id={lineId}
                className="ghost"
                onClick={() => onOpen("line", lineId)}
              >
                {latest === undefined ? "Add line" : "Edit line"}
                <span className="sr-only"> for {matchup}</span>
              </button>
            )}
            {state === "Locked" && (
              <button
                type="button"
                id={resultId}
                className="ghost"
                onClick={() => onOpen("result", resultId)}
              >
                {game.hasResult ? "Edit result" : "Enter result"}
                <span className="sr-only"> for {matchup}</span>
              </button>
            )}
          </div>
        )}
      </div>
      {problem && editing === null && (
        <p role="alert" className="problem">
          {problem}
        </p>
      )}
      {editing === "line" && (
        <LineEditor
          game={game}
          matchup={matchup}
          busy={busy}
          problem={problem}
          initial={latest}
          onCancel={onClose}
          onSave={onSaveLine}
        />
      )}
      {editing === "result" && (
        <ResultEditor
          game={game}
          matchup={matchup}
          busy={busy}
          problem={problem}
          onCancel={onClose}
          onSave={onSaveResult}
        />
      )}
    </li>
  );
}

function LineEditor({
  game,
  matchup,
  busy,
  problem,
  initial,
  onCancel,
  onSave,
}: {
  game: Game;
  matchup: string;
  busy: boolean;
  problem: string;
  initial: number | undefined;
  onCancel: () => void;
  onSave: (spread: number, approve: boolean) => Promise<void>;
}) {
  const [text, setText] = useState(
    initial === undefined ? "" : String(initial),
  );
  const [invalid, setInvalid] = useState("");
  const home = shortName(game.homeTeamName);

  function save(approve: boolean) {
    const spread = Number(text);
    if (text.trim() === "" || Number.isNaN(spread)) {
      setInvalid("Enter the spread as a number, such as -3.5.");
      return;
    }
    setInvalid("");
    void onSave(spread, approve);
  }

  return (
    <form
      className="editor"
      aria-label={`Line for ${matchup}`}
      noValidate
      onKeyDown={(event) => event.key === "Escape" && onCancel()}
      onSubmit={(event: FormEvent) => {
        event.preventDefault();
        save(false);
      }}
    >
      <label htmlFor={`spread-${game.id}`}>
        Spread for {home}
        <small>
          Negative means {home} is favored; positive makes {home} the underdog.
        </small>
      </label>
      <input
        id={`spread-${game.id}`}
        inputMode="decimal"
        value={text}
        autoFocus
        onChange={(event) => setText(event.target.value)}
      />
      {(invalid || problem) && (
        <p role="alert" className="problem">
          {invalid || problem}
        </p>
      )}
      <div className="editor-actions">
        <button type="submit" disabled={busy}>
          Save line
        </button>
        <button
          type="button"
          className="primary"
          disabled={busy}
          onClick={() => save(true)}
        >
          Save and approve
        </button>
        <button type="button" className="ghost" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </form>
  );
}

function ResultEditor({
  game,
  matchup,
  busy,
  problem,
  onCancel,
  onSave,
}: {
  game: Game;
  matchup: string;
  busy: boolean;
  problem: string;
  onCancel: () => void;
  onSave: (home: number, away: number) => Promise<void>;
}) {
  const [home, setHome] = useState(
    game.homeScore === null ? "" : String(game.homeScore),
  );
  const [away, setAway] = useState(
    game.awayScore === null ? "" : String(game.awayScore),
  );
  const [invalid, setInvalid] = useState("");

  function submit(event: FormEvent) {
    event.preventDefault();
    const h = Number(home);
    const a = Number(away);
    if (
      home.trim() === "" ||
      away.trim() === "" ||
      !Number.isInteger(h) ||
      !Number.isInteger(a)
    ) {
      setInvalid("Enter both scores as whole numbers.");
      return;
    }
    setInvalid("");
    void onSave(h, a);
  }

  return (
    <form
      className="editor"
      aria-label={`Result for ${matchup}`}
      noValidate
      onKeyDown={(event) => event.key === "Escape" && onCancel()}
      onSubmit={submit}
    >
      <label htmlFor={`away-${game.id}`}>
        {shortName(game.awayTeamName)} score
      </label>
      <input
        id={`away-${game.id}`}
        inputMode="numeric"
        value={away}
        autoFocus
        onChange={(event) => setAway(event.target.value)}
      />
      <label htmlFor={`home-${game.id}`}>
        {shortName(game.homeTeamName)} score
      </label>
      <input
        id={`home-${game.id}`}
        inputMode="numeric"
        value={home}
        onChange={(event) => setHome(event.target.value)}
      />
      {(invalid || problem) && (
        <p role="alert" className="problem">
          {invalid || problem}
        </p>
      )}
      <div className="editor-actions">
        <button type="submit" className="primary" disabled={busy}>
          Save result
        </button>
        <button type="button" className="ghost" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </form>
  );
}
