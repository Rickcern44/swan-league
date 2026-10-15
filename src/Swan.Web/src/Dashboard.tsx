import { useCallback, useEffect, useState } from "react";
import { StateChip, kindLabels } from "./Swan";

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

export default function Dashboard({
  onSeasonSetup,
  onOpenWeek,
}: {
  onSeasonSetup: () => void;
  onOpenWeek: (weekId: string) => void;
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
        <div className="band" role="alert">
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
        <div className="band">
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
  // Mid-sentence the stand-in name is lower case.
  const inSentence = seasonName === "This season" ? "this season" : seasonName;
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
        <section className="queue" aria-labelledby="queue-title">
          <h2 id="queue-title">Needs your decision</h2>
          {decisions.length > 0 ? (
            <ol className="attention">
              {decisions.map((item) => (
                <AttentionRow
                  key={`${item.kind}-${item.weekId}`}
                  item={item}
                  onOpenWeek={onOpenWeek}
                />
              ))}
            </ol>
          ) : caughtUp ? (
            <p>
              You are caught up. Every week of {inSentence} is final, so nothing
              needs a decision.
            </p>
          ) : currentWeek ? (
            <p>Nothing needs a decision right now.</p>
          ) : (
            <p>{inSentence} has no weeks yet, so there is nothing to decide.</p>
          )}
          {status.length > 0 && (
            <>
              <h3>For your information</h3>
              <ol className="attention">
                {status.map((item) => (
                  <AttentionRow
                    key={`${item.kind}-${item.weekId}`}
                    item={item}
                    onOpenWeek={onOpenWeek}
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
          <section className="pulse-card" aria-labelledby="week-title">
            <h2 id="week-title">This week</h2>
            <p className="week-line">
              Week {currentWeek.number} ·{" "}
              {kindLabels[currentWeek.kind] ?? currentWeek.kind} ·{" "}
              <StateChip state={currentWeek.state} />
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
            <a
              className="open-week"
              href={`/weeks/${currentWeek.id}`}
              onClick={(event) => {
                event.preventDefault();
                onOpenWeek(currentWeek.id);
              }}
            >
              Open Week {currentWeek.number}
            </a>
          </section>
        )}
      </div>
    </section>
  );
}

function AttentionRow({
  item,
  onOpenWeek,
}: {
  item: Attention;
  onOpenWeek: (weekId: string) => void;
}) {
  return (
    <li className={item.needsDecision ? "decision" : "info"}>
      <a
        href={`/weeks/${item.weekId}`}
        onClick={(event) => {
          event.preventDefault();
          onOpenWeek(item.weekId);
        }}
      >
        <strong>{item.title}</strong>
        <span>{item.reason}</span>
        <span className="where">
          Week {item.weekNumber} · <StateChip state={item.weekState} />
        </span>
      </a>
    </li>
  );
}
