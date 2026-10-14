import { useCallback, useEffect, useState } from "react";

type Attention = {
  kind: string;
  title: string;
  reason: string;
  weekId: string;
  weekNumber: number;
  weekState: string;
  count: number | null;
  of: number | null;
  needsDecision: boolean;
};

type DashboardData = {
  season: {
    id: string;
    name: string;
    year: number;
    isSimulation: boolean;
  } | null;
  currentWeek: {
    id: string;
    number: number;
    kind: string;
    state: string;
    games: number;
    includedGames: number;
    linesApproved: number;
    resultsConfirmed: number;
    isDoubleDogWeek: boolean;
  } | null;
  attention: Attention[];
  caughtUp: boolean;
};

type Loaded =
  | { status: "loading" }
  | { status: "failed"; error: string }
  | { status: "ready"; data: DashboardData };

// Every state is spelled out, so colour is never the only thing that says where a week is.
const stateLabels: Record<string, string> = {
  Draft: "Draft",
  LinesApproved: "Lines approved",
  Open: "Open for picks",
  Locked: "Locked",
  Final: "Final",
};
const kindLabels: Record<string, string> = {
  Regular: "Regular season",
  Playoff: "Playoffs",
  SuperBowl: "Super Bowl",
};

async function fetchDashboard(): Promise<Loaded> {
  try {
    const response = await fetch("/api/admin/dashboard", {
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

export function StateBadge({ state }: { state: string }) {
  return (
    <span className={`badge badge-${state.toLowerCase()}`}>
      {stateLabels[state] ?? state}
    </span>
  );
}

export default function Dashboard({
  onSeasonSetup,
}: {
  onSeasonSetup: () => void;
}) {
  const [loaded, setLoaded] = useState<Loaded>({ status: "loading" });

  const load = useCallback(async () => {
    setLoaded({ status: "loading" });
    setLoaded(await fetchDashboard());
  }, []);

  useEffect(() => {
    void fetchDashboard().then(setLoaded);
  }, []);

  if (loaded.status === "loading")
    return (
      <section aria-labelledby="dash-title" aria-busy="true">
        <h1 id="dash-title">Week dashboard</h1>
        <p role="status">Loading the league…</p>
      </section>
    );

  if (loaded.status === "failed")
    return (
      <section aria-labelledby="dash-title">
        <h1 id="dash-title">Week dashboard</h1>
        <div className="panel" role="alert">
          <h2>Could not load the dashboard</h2>
          <p>{loaded.error}</p>
          <button type="button" onClick={() => void load()}>
            Try again
          </button>
        </div>
      </section>
    );

  const { season, currentWeek, attention, caughtUp } = loaded.data;

  if (!season)
    return (
      <section aria-labelledby="dash-title">
        <h1 id="dash-title">Week dashboard</h1>
        <div className="panel">
          <h2>No season is live</h2>
          <p>
            There is no live season, so there is no week to run yet. Finish
            Season setup and activate the season to start the weekly cycle.
          </p>
          <button type="button" onClick={onSeasonSetup}>
            Go to Season setup
          </button>
        </div>
      </section>
    );

  // A season can have no name; the year always identifies it.
  const seasonName =
    season.name.trim() ||
    (season.year > 0 ? `${season.year} season` : "This season");
  const decisions = attention.filter((item) => item.needsDecision);
  const status = attention.filter((item) => !item.needsDecision);

  return (
    <section aria-labelledby="dash-title">
      <p className="eyebrow">
        {seasonName}
        {season.isSimulation ? " · Simulation" : ""}
      </p>
      <h1 id="dash-title">
        {currentWeek ? `Week ${currentWeek.number}` : "Week dashboard"}
      </h1>

      <div className="dash-grid">
        <section className="panel queue" aria-labelledby="queue-title">
          <h2 id="queue-title">Needs your decision</h2>
          {decisions.length > 0 ? (
            <ol className="attention">
              {decisions.map((item) => (
                <AttentionRow key={`${item.kind}-${item.weekId}`} item={item} />
              ))}
            </ol>
          ) : caughtUp ? (
            <p>
              You are caught up. Every week of {seasonName} is final, so nothing
              needs a decision.
            </p>
          ) : currentWeek ? (
            <p>Nothing needs a decision right now.</p>
          ) : (
            <p>{seasonName} has no weeks yet, so there is nothing to decide.</p>
          )}
          {status.length > 0 && (
            <>
              <h3>For your information</h3>
              <ol className="attention">
                {status.map((item) => (
                  <AttentionRow
                    key={`${item.kind}-${item.weekId}`}
                    item={item}
                  />
                ))}
              </ol>
            </>
          )}
          <button type="button" className="quiet" onClick={() => void load()}>
            Refresh
          </button>
        </section>

        {currentWeek && (
          <section className="panel" aria-labelledby="week-title">
            <h2 id="week-title">This week</h2>
            <p className="week-line">
              Week {currentWeek.number} ·{" "}
              {kindLabels[currentWeek.kind] ?? currentWeek.kind} ·{" "}
              <StateBadge state={currentWeek.state} />
            </p>
            <dl className="facts">
              <dt>Games</dt>
              <dd>
                {currentWeek.includedGames}
                {currentWeek.games > currentWeek.includedGames
                  ? ` included, ${currentWeek.games - currentWeek.includedGames} excluded`
                  : " included"}
              </dd>
              <dt>Lines approved</dt>
              <dd>
                {currentWeek.linesApproved} / {currentWeek.includedGames}
              </dd>
              <dt>Results confirmed</dt>
              <dd>
                {currentWeek.resultsConfirmed} / {currentWeek.includedGames}
              </dd>
              {currentWeek.isDoubleDogWeek && (
                <>
                  <dt>Double dogs</dt>
                  <dd>Week {currentWeek.number} is a double-dog week</dd>
                </>
              )}
            </dl>
          </section>
        )}
      </div>
    </section>
  );
}

function AttentionRow({ item }: { item: Attention }) {
  return (
    <li className={item.needsDecision ? "decision" : "info"}>
      <strong>{item.title}</strong>
      <span>{item.reason}</span>
      <span className="where">
        Week {item.weekNumber} · <StateBadge state={item.weekState} />
      </span>
    </li>
  );
}
