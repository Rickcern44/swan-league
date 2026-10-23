import { useCallback, useEffect, useState } from "react";
import PageState from "./PageState";
import { kindLabels, StateChip, stageLabelsInOrder } from "./Swan";

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
    submissions?: Submissions | null;
  } | null;
  attention: Attention[];
  caughtUp: boolean;
  money?: {
    buyInsPaid: number;
    buyInsDue: number;
    finePoolCents: number;
    pendingFines: number;
    pendingFinesCents: number;
  } | null;
  jobs?: {
    healthy: boolean;
    lastScoreSync: string | null;
    nextReminder: string | null;
    failures: number;
  } | null;
};

type Submissions = {
  complete: number;
  of: number;
  incomplete: {
    membershipId: string;
    name: string;
    starts: number;
    startLimit: number;
    dogs: number;
    dogLimit: number;
  }[];
};

// What the week is doing, in words; the stage bar shows the same thing as a position.
const statements: Record<string, string> = {
  Draft: "Lines are being prepared",
  LinesApproved: "Lines approved, ready to publish",
  Open: "Open for picks",
  Locked: "Locked, results coming in",
  Final: "Final",
};

const shortStages: Record<string, string> = {
  Draft: "Draft",
  LinesApproved: "Lines",
  Open: "Open",
  Locked: "Locked",
  Final: "Final",
};

const dollars = (cents: number) => `$${(cents / 100).toFixed(2)}`;
const eastern = new Intl.DateTimeFormat("en-US", {
  timeZone: "America/New_York",
  weekday: "short",
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit",
});
const plural = (n: number, one: string, many = `${one}s`) =>
  `${n} ${n === 1 ? one : many}`;

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
  onNavigate,
  onDecisions,
  canDraft,
  canSetup,
}: {
  onSeasonSetup: () => void;
  onOpenWeek: (weekId: string, tab?: string) => void;
  onNavigate: (path: string) => void;
  onDecisions?: (count: number) => void;
  canDraft: boolean;
  canSetup: boolean;
}) {
  const [loaded, setLoaded] = useState<Loaded>({ status: "loading" });

  const load = useCallback(async () => {
    setLoaded({ status: "loading" });
    setLoaded(await fetchDashboard());
  }, []);

  useEffect(() => {
    void fetchDashboard().then(setLoaded);
  }, []);

  const decisionCount =
    loaded.status === "ready"
      ? loaded.data.attention.filter((item) => item.needsDecision).length
      : null;
  useEffect(() => {
    if (decisionCount !== null) onDecisions?.(decisionCount);
  }, [decisionCount, onDecisions]);

  if (loaded.status === "loading")
    return (
      <section aria-labelledby="dash-title" aria-busy="true">
        <h1 id="dash-title">Today</h1>
        <PageState kind="loading" message="Loading the league…" />
      </section>
    );

  if (loaded.status === "failed")
    return (
      <section aria-labelledby="dash-title">
        <h1 id="dash-title">Today</h1>
        <PageState
          kind="error"
          title="Could not load the dashboard."
          message={loaded.error}
          onRetry={() => void load()}
        />
      </section>
    );

  const { season, currentWeek, attention, caughtUp, money, jobs } = loaded.data;

  if (!season)
    return (
      <section aria-labelledby="dash-title">
        <h1 id="dash-title">Today</h1>
        <PageState kind="empty" title="No season is live">
          <p>
            There is no live season, so there is no week to run yet. Finish
            Season setup and activate the season to start the weekly cycle.
          </p>
          <button type="button" onClick={onSeasonSetup}>
            Go to Season setup
          </button>
        </PageState>
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
  const submissions = currentWeek?.submissions ?? null;
  const owing = submissions ? submissions.of - submissions.complete : 0;
  const top = decisions[0];

  // One next action: the top-ranked decision, else coaches still owing picks on an open week, else where the week stands.
  const next = top
    ? {
        title: top.title,
        detail: top.reason,
        weekId: top.weekId,
        weekNumber: top.weekNumber,
      }
    : currentWeek && currentWeek.state === "Open" && owing > 0
      ? {
          title: `${plural(owing, "coach", "coaches")} still ${owing === 1 ? "owes" : "owe"} picks`,
          detail: `${submissions?.complete} of ${submissions?.of} cards are complete. Picks lock on their own at each kickoff.`,
          weekId: currentWeek.id,
          weekNumber: currentWeek.number,
        }
      : caughtUp
        ? {
            title: "All caught up",
            detail: `Every week of ${inSentence} is final.`,
            weekId: currentWeek?.id,
            weekNumber: currentWeek?.number,
          }
        : currentWeek
          ? {
              title: "Nothing to decide right now",
              detail: `Week ${currentWeek.number} is ${(statements[currentWeek.state] ?? currentWeek.state).toLowerCase()}.`,
              weekId: currentWeek.id,
              weekNumber: currentWeek.number,
            }
          : null;
  const rest = decisions.slice(1);
  const stageIndex = currentWeek
    ? stageLabelsInOrder.findIndex((stage) => stage.state === currentWeek.state)
    : -1;
  const link = (path: string) => ({
    href: path,
    onClick: (event: React.MouseEvent) => {
      event.preventDefault();
      onNavigate(path);
    },
  });

  return (
    <section aria-labelledby="dash-title" className="today">
      <header className="today-head">
        <p className="eyebrow">
          {[
            seasonName,
            season.isSimulation ? "Simulation" : null,
            currentWeek
              ? (kindLabels[currentWeek.kind] ?? currentWeek.kind)
              : null,
            currentWeek?.isDoubleDogWeek ? "Double dogs" : null,
          ]
            .filter(Boolean)
            .join(" · ")}
        </p>
        <h1 id="dash-title">
          {currentWeek ? `Week ${currentWeek.number}` : "Today"}
        </h1>
        {currentWeek && (
          <p className="lede today-state">
            {statements[currentWeek.state] ?? currentWeek.state}
          </p>
        )}
      </header>

      {currentWeek && (
        <>
          <ol
            className="stage-bar"
            aria-label={`Week ${currentWeek.number} stages`}
          >
            {stageLabelsInOrder.map((stage, index) => (
              <li
                key={stage.state}
                className={
                  index < stageIndex
                    ? "done"
                    : index === stageIndex
                      ? "now"
                      : "later"
                }
                aria-current={index === stageIndex ? "step" : undefined}
              >
                <span className="stage-dot" aria-hidden="true" />
                <span className="stage-long">{stage.label}</span>
                <span className="stage-short" aria-hidden="true">
                  {shortStages[stage.state]}
                </span>
                {index < stageIndex && <span className="sr-only">, done</span>}
              </li>
            ))}
          </ol>
          <p className="week-facts">
            <span>{plural(currentWeek.includedGames, "game")}</span>
            <span>
              {currentWeek.linesApproved} of {currentWeek.includedGames} lines
              approved
            </span>
            {(currentWeek.state === "Locked" ||
              currentWeek.state === "Final") && (
              <span>
                {currentWeek.resultsConfirmed} of {currentWeek.includedGames}{" "}
                results in
              </span>
            )}
          </p>
        </>
      )}

      {next && (
        <section className="next-card" aria-label="Next action">
          <p className="next-eyebrow" aria-hidden="true">
            Next action
          </p>
          <h2>{next.title}</h2>
          <p>{next.detail}</p>
          {next.weekId && (
            <a
              className="button-link next-link"
              href={`/commish/weeks/${next.weekId}`}
              onClick={(event) => {
                event.preventDefault();
                onOpenWeek(next.weekId as string);
              }}
            >
              Open Week {next.weekNumber}
            </a>
          )}
        </section>
      )}

      <div className="today-grid">
        <section className="queue" aria-labelledby="queue-title">
          <h2 id="queue-title">Needs your decision</h2>
          {rest.length > 0 ? (
            <ol className="attention">
              {rest.map((item) => (
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
          ) : top ? (
            <p>Nothing else needs a decision.</p>
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
        </section>

        {currentWeek &&
          submissions &&
          (currentWeek.state === "Open" || currentWeek.state === "Locked") && (
            <section className="subs-card" aria-labelledby="subs-title">
              <div className="subs-head">
                <h2 id="subs-title">Week {currentWeek.number} submissions</h2>
                <span>
                  {submissions.complete} of {submissions.of} complete
                </span>
              </div>
              <div
                className="subs-bar"
                role="progressbar"
                aria-label="Complete cards"
                aria-valuemin={0}
                aria-valuemax={submissions.of}
                aria-valuenow={submissions.complete}
              >
                <span
                  style={{
                    width: `${submissions.of ? (submissions.complete / submissions.of) * 100 : 0}%`,
                  }}
                />
              </div>
              {submissions.incomplete.length === 0 ? (
                <p>Every coach has a full card.</p>
              ) : (
                <ul className="owing">
                  {submissions.incomplete.slice(0, 5).map((coach) => (
                    <li key={coach.membershipId}>
                      <span className="owing-name">{coach.name}</span>
                      <span className="owing-count">
                        {coach.starts} of {coach.startLimit} starts ·{" "}
                        {coach.dogs} of {coach.dogLimit}{" "}
                        {coach.dogLimit === 1 ? "dog" : "dogs"}
                      </span>
                    </li>
                  ))}
                </ul>
              )}
              <p className="subs-foot">
                {submissions.incomplete.length > 5 && (
                  <span>+ {submissions.incomplete.length - 5} more</span>
                )}
                <a
                  href={`/commish/weeks/${currentWeek.id}?tab=picks`}
                  onClick={(event) => {
                    event.preventDefault();
                    onOpenWeek(currentWeek.id, "picks");
                  }}
                >
                  See all picks
                </a>
              </p>
            </section>
          )}
      </div>

      {(money || jobs) && (
        <div className="stat-cards">
          {money && (
            <section className="stat-card" aria-labelledby="money-title">
              <h2 id="money-title">Money</h2>
              <p>
                {money.buyInsPaid} of {money.buyInsDue} buy-ins paid
              </p>
              <p>
                {dollars(money.finePoolCents)} fine pool
                {money.pendingFines > 0
                  ? ` · ${dollars(money.pendingFinesCents)} pending (${money.pendingFines})`
                  : ""}
              </p>
            </section>
          )}
          {jobs && (
            <section className="stat-card" aria-labelledby="jobs-title">
              <h2 id="jobs-title">Jobs</h2>
              <p className={jobs.healthy ? "ok" : "bad"}>
                {jobs.healthy
                  ? "All healthy"
                  : plural(jobs.failures, "failure")}
              </p>
              {jobs.lastScoreSync && (
                <p>
                  Score sync {eastern.format(new Date(jobs.lastScoreSync))} ET
                </p>
              )}
            </section>
          )}
        </div>
      )}

      <section className="quick-actions" aria-labelledby="quick-title">
        <h2 id="quick-title">Quick actions</h2>
        <div>
          {currentWeek && (
            <a {...link(`/commish/weeks/${currentWeek.id}`)}>
              Open Week {currentWeek.number}
            </a>
          )}
          {canDraft && <a {...link("/commish/draft")}>Draft status</a>}
          {canSetup && <a {...link("/commish/setup")}>Season setup</a>}
        </div>
      </section>

      <button type="button" className="quiet" onClick={() => void load()}>
        Refresh
      </button>
    </section>
  );
}

function AttentionRow({
  item,
  onOpenWeek,
}: {
  item: Attention;
  onOpenWeek: (weekId: string, tab?: string) => void;
}) {
  return (
    <li className={item.needsDecision ? "decision" : "info"}>
      <a
        href={`/commish/weeks/${item.weekId}`}
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
