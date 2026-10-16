import { FormEvent, useContext, useEffect, useRef, useState } from "react";
import { DevelopmentTools, RefreshAccess } from "./AccessContext";
import { formatDeadline, goTo } from "./draftTime";

type Money = { amount: number; currency: string };
type Season = {
  year: number;
  name: string;
  isSimulation: boolean;
  settings: {
    buyIn: Money;
    teamPickDeadlineEastern: string | null;
  };
  pools: { name: string; stake: Money }[];
  setupStep: number;
};

type Notice = { kind: "saved" | "error"; text: string } | undefined;
type Loaded =
  | { status: "loading" }
  | { status: "none" }
  | { status: "failed"; error: string }
  | { status: "ready"; season: Season };

type TeamRow = {
  teamId: string;
  name: string;
  division: string;
  finishPosition: number | null;
  pointValue: number | null;
};
type TeamList = { teams: TeamRow[] };
const finishLabels = ["1st", "2nd", "3rd", "4th"];
const pickMap = (teams: TeamRow[]) =>
  Object.fromEntries(
    teams.map((team) => [
      team.teamId,
      team.finishPosition ? String(team.finishPosition) : "",
    ]),
  );

const stakeNames = ["Main", "Dog", "Regular Season", "Playoff"] as const;
const defaultDeadline = "13:00";

type Result<T> = { value: T } | { error: string; missing?: boolean };

async function send<T = Season>(
  method: string,
  url: string,
  body?: unknown,
): Promise<Result<T>> {
  try {
    const response = await fetch(url, {
      method,
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (response.ok) return { value: (await response.json()) as T };
    const error = (await response.json().catch(() => undefined)) as
      | { message?: string; title?: string }
      | undefined;
    return {
      error:
        error?.message ??
        error?.title ??
        `The request failed (${response.status}).`,
      missing: response.status === 404,
    };
  } catch {
    return { error: "Could not reach the server." };
  }
}

const toCents = (value: string) => Math.round(Number(value) * 100);
const dollars = (cents: number) => `$${(cents / 100).toFixed(2)}`;

// createOnly is a commissioner with no season in Setup: they may start one but cannot read a Setup season yet, so the page opens on the start form without asking for one.
export default function SeasonSetup({
  createOnly = false,
}: {
  createOnly?: boolean;
}) {
  const [loaded, setLoaded] = useState<Loaded>(
    createOnly ? { status: "none" } : { status: "loading" },
  );

  useEffect(() => {
    if (createOnly) return;
    void send("GET", "/api/admin/seasons/setup").then((result) =>
      setLoaded(
        "value" in result
          ? { status: "ready", season: result.value }
          : result.missing
            ? { status: "none" }
            : { status: "failed", error: result.error },
      ),
    );
  }, [createOnly]);

  const apply = (season: Season) => setLoaded({ status: "ready", season });
  const refreshAccess = useContext(RefreshAccess);
  const developmentTools = useContext(DevelopmentTools);
  const [resetDone, setResetDone] = useState(false);

  return (
    <section className="setup" aria-labelledby="setup-title">
      <h1 id="setup-title">Season setup</h1>
      <p>Set the foundation for the next Swan League season.</p>
      {loaded.status === "failed" && (
        <p role="alert">Could not load the Setup season: {loaded.error}</p>
      )}
      {resetDone && loaded.status === "none" && (
        <p role="status">
          Local season data was reset. Start a fresh season below.
        </p>
      )}
      {loaded.status === "none" && <StartCard onSeason={apply} />}
      {loaded.status === "ready" && (
        <SetupCards season={loaded.season} onSeason={apply} />
      )}
      {developmentTools && loaded.status !== "loading" && (
        <ResetTools
          onReset={() => {
            window.history.replaceState({}, "", "/setup");
            setResetDone(true);
            setLoaded({ status: "none" });
            void refreshAccess();
          }}
        />
      )}
    </section>
  );
}

function Card({
  title,
  notice,
  children,
  onSubmit,
  action,
  onBack,
  wide,
  actionDisabled,
}: {
  title: string;
  notice: Notice;
  children: React.ReactNode;
  onSubmit: () => void;
  action?: string;
  onBack?: () => void;
  wide?: boolean;
  actionDisabled?: boolean;
}) {
  return (
    <form
      className={wide ? "card wide" : "card"}
      noValidate
      aria-label={title}
      onSubmit={(event: FormEvent) => {
        event.preventDefault();
        onSubmit();
      }}
    >
      <h2>{title}</h2>
      {children}
      <div className="actions">
        {notice?.kind === "error" && <span role="alert">{notice.text}</span>}
        {notice?.kind === "saved" && <span role="status">{notice.text}</span>}
        {onBack && (
          <button type="button" className="secondary" onClick={onBack}>
            Back
          </button>
        )}
        {action && (
          <button type="submit" disabled={actionDisabled}>
            {action}
          </button>
        )}
      </div>
    </form>
  );
}

function Field({
  label,
  children,
}: {
  label: string;
  children: React.ReactNode;
}) {
  return (
    <label>
      {label}
      {children}
    </label>
  );
}

function StartCard({ onSeason }: { onSeason: (season: Season) => void }) {
  const [year, setYear] = useState(String(new Date().getFullYear()));
  const [name, setName] = useState("");
  const [isSimulation, setIsSimulation] = useState(false);
  const [notice, setNotice] = useState<Notice>();
  const refreshAccess = useContext(RefreshAccess);

  return (
    <Card
      title="Start a new season"
      notice={notice}
      action="Create season"
      onSubmit={() =>
        void send("POST", "/api/admin/seasons", {
          year: Number(year),
          name,
          isSimulation,
        }).then(async (result) => {
          if (!("value" in result))
            return setNotice({ kind: "error", text: result.error });
          // The creator is now this season's commissioner: read what that lets them reach before showing it.
          await refreshAccess();
          onSeason(result.value);
        })
      }
    >
      <Field label="Season year">
        <input
          type="number"
          value={year}
          onChange={(event) => setYear(event.target.value)}
        />
      </Field>
      <Field label="Season name">
        <input value={name} onChange={(event) => setName(event.target.value)} />
      </Field>
      <label className="check">
        <input
          type="checkbox"
          checked={isSimulation}
          onChange={(event) => setIsSimulation(event.target.checked)}
        />
        Simulation season
      </label>
    </Card>
  );
}

// Development builds only (the server maps the route and reports developmentTools only there): empties the local season data so setup can start over. Teams and development accounts stay, and nobody is signed out.
function ResetTools({ onReset }: { onReset: () => void }) {
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState("");
  const heading = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    if (confirming) heading.current?.focus();
  }, [confirming]);

  async function reset() {
    setBusy(true);
    setProblem("");
    const result = await send(
      "POST",
      "/api/admin/development/reset-season-data",
    );
    setBusy(false);
    if (!("value" in result)) return setProblem(result.error);
    onReset();
  }

  return (
    <details className="dev-tools">
      <summary>Development tools</summary>
      <p className="hint">
        Removes local seasons, rosters, schedules, picks, scores, and audit
        history. Teams and development accounts remain. This cannot be undone.
      </p>
      {!confirming && (
        <button
          type="button"
          className="secondary"
          onClick={() => setConfirming(true)}
        >
          Reset local season data
        </button>
      )}
      {confirming && (
        <div
          role="alertdialog"
          aria-labelledby="reset-confirm-title"
          className="confirm"
          onKeyDown={(event) => {
            if (event.key === "Escape") setConfirming(false);
          }}
        >
          <h3 id="reset-confirm-title" tabIndex={-1} ref={heading}>
            Reset all local season data?
          </h3>
          {problem && <span role="alert">{problem}</span>}
          <div className="actions">
            <button
              type="button"
              className="secondary"
              disabled={busy}
              onClick={() => setConfirming(false)}
            >
              Cancel
            </button>
            <button type="button" disabled={busy} onClick={() => void reset()}>
              Reset season data
            </button>
          </div>
        </div>
      )}
    </details>
  );
}

const wizardSteps = [
  "Basics",
  "Money",
  "Weekly deadline",
  "Teams and point values",
  "Schedule import",
  "Weeks and dogs",
  "Fines and payouts",
  "League settings",
  "Coaches",
  "Ready to launch",
] as const;
const lastStep = wizardSteps.length - 1;
const stepSlugs = [
  "basics",
  "money",
  "deadline",
  "teams",
  "schedule",
  "dogs",
  "payouts",
  "rules",
  "coaches",
  "review",
];
// Four milestones group the ten saved sub-steps. The server's saved step only ever moves forward, so each milestone is a contiguous run of sub-steps.
const milestones = [
  { name: "League foundation", first: 0, last: 2 },
  { name: "Schedule and scoring", first: 3, last: 5 },
  { name: "People and payouts", first: 6, last: 8 },
  { name: "Ready to launch", first: 9, last: 9 },
] as const;
const milestoneOf = (step: number) =>
  milestones.findIndex((m) => step >= m.first && step <= m.last);

function SetupCards({
  season,
  onSeason,
}: {
  season: Season;
  onSeason: (season: Season) => void;
}) {
  const stake = (name: string) =>
    String(season.pools.find((pool) => pool.name === name)?.stake.amount ?? 0);
  const [year, setYear] = useState(String(season.year));
  const [name, setName] = useState(season.name);
  const [isSimulation, setIsSimulation] = useState(season.isSimulation);
  const [buyIn, setBuyIn] = useState(String(season.settings.buyIn.amount));
  const [stakes, setStakes] = useState<Record<string, string>>(() =>
    Object.fromEntries(stakeNames.map((pool) => [pool, stake(pool)])),
  );
  const [deadline, setDeadline] = useState(
    (season.settings.teamPickDeadlineEastern ?? defaultDeadline).slice(0, 5),
  );
  const [notices, setNotices] = useState<Record<string, Notice>>({});
  const [teams, setTeams] = useState<TeamRow[]>();
  const [picks, setPicks] = useState<Record<string, string>>({});
  const [teamsError, setTeamsError] = useState<string>();
  // A fresh load opens at the saved resume point; the step after the last built one still shows the last page.
  const [view, setView] = useState(() => Math.min(season.setupStep, lastStep));
  const available = useRef(Math.min(season.setupStep, lastStep));
  available.current = Math.min(season.setupStep, lastStep);

  useEffect(() => {
    // Subpaths and history entries make the browser's Back/Forward move between pages; they never save anything.
    window.history.replaceState(
      { setupView: view },
      "",
      `/setup/${stepSlugs[view]}`,
    );
    const onPop = (event: PopStateEvent) => {
      const target = (event.state as { setupView?: number } | null)?.setupView;
      if (typeof target === "number" && target <= available.current)
        setView(target);
    };
    window.addEventListener("popstate", onPop);
    return () => window.removeEventListener("popstate", onPop);
    // The initial entry is recorded once.
  }, []);

  // The team catalog is loaded the first time the Teams page opens.
  useEffect(() => {
    if (view !== 3 || teams || teamsError) return;
    void send<TeamList>("GET", "/api/admin/seasons/setup/teams").then(
      (result) => {
        if ("value" in result) {
          setTeams(result.value.teams);
          setPicks(pickMap(result.value.teams));
        } else setTeamsError(result.error);
      },
    );
  }, [view, teams, teamsError]);

  function goTo(target: number) {
    window.history.pushState(
      { setupView: target },
      "",
      `/setup/${stepSlugs[target]}`,
    );
    setView(target);
  }

  // Only the saved page takes the reply's values: another page's unsaved edits are not the reply's to discard.
  function resync(saved: Season, step: number) {
    if (step === 0) {
      setYear(String(saved.year));
      setName(saved.name);
      setIsSimulation(saved.isSimulation);
    } else if (step === 1) {
      setBuyIn(String(saved.settings.buyIn.amount));
      setStakes(
        Object.fromEntries(
          stakeNames.map((pool) => [
            pool,
            String(saved.pools.find((p) => p.name === pool)?.stake.amount ?? 0),
          ]),
        ),
      );
    } else {
      setDeadline(
        (saved.settings.teamPickDeadlineEastern ?? defaultDeadline).slice(0, 5),
      );
    }
  }

  // Save the page, then record the next step as the resume point (never moving it backwards), then move on.
  async function saveAndContinue(step: number, url: string, body: unknown) {
    const fail = (text: string) =>
      setNotices({ [step]: { kind: "error", text } });
    const saved = await send("PUT", url, body);
    if (!("value" in saved)) return fail(saved.error);
    resync(saved.value, step);
    onSeason(saved.value);
    const target = Math.max(saved.value.setupStep, step + 1);
    let latest = saved.value;
    if (target !== latest.setupStep) {
      const recorded = await send("PUT", "/api/admin/seasons/setup/step", {
        step: target,
      });
      if (!("value" in recorded))
        return fail(
          `Saved, but your progress could not be recorded: ${recorded.error}`,
        );
      latest = recorded.value;
      onSeason(latest);
    }
    if (step < lastStep) {
      setNotices({});
      goTo(step + 1);
    } else setNotices({ [step]: { kind: "saved", text: "Deadline saved" } });
  }

  async function saveTeams() {
    if (!teams) return;
    const fail = (text: string) => setNotices({ 3: { kind: "error", text } });
    if (teams.some((team) => !picks[team.teamId]))
      return fail("Choose a finish for every team.");
    for (const division of new Set(teams.map((team) => team.division)))
      if (
        teams
          .filter((team) => team.division === division)
          .map((team) => picks[team.teamId])
          .sort()
          .join() !== "1,2,3,4"
      )
        return fail(
          `${division} needs exactly one team at each of 1st, 2nd, 3rd and 4th.`,
        );
    const saved = await send<TeamList>(
      "PUT",
      "/api/admin/seasons/setup/teams",
      {
        teams: teams.map((team) => ({
          teamId: team.teamId,
          finishPosition: Number(picks[team.teamId]),
        })),
      },
    );
    if (!("value" in saved)) return fail(saved.error);
    setTeams(saved.value.teams);
    setPicks(pickMap(saved.value.teams));
    // The next unbuilt step (Schedule import) becomes the resume point, never moving it backwards.
    const target = Math.max(season.setupStep, 4);
    if (target !== season.setupStep) {
      const recorded = await send("PUT", "/api/admin/seasons/setup/step", {
        step: target,
      });
      if (!("value" in recorded))
        return fail(
          `Saved, but your progress could not be recorded: ${recorded.error}`,
        );
      onSeason(recorded.value);
    }
    setNotices({});
    goTo(4);
  }

  const poolTotal = stakeNames.reduce(
    (sum, pool) => sum + toCents(stakes[pool]),
    0,
  );
  const back = view > 0 ? () => goTo(view - 1) : undefined;

  const resumeStep = Math.min(season.setupStep, lastStep);
  const finished = season.setupStep > lastStep;
  const current = milestoneOf(view);
  const part = view - milestones[current].first + 1;
  const parts = milestones[current].last - milestones[current].first + 1;
  const openMilestone = (index: number) => {
    const m = milestones[index];
    goTo(
      season.setupStep > m.last
        ? m.first
        : Math.max(m.first, Math.min(resumeStep, m.last)),
    );
  };

  return (
    <>
      <p className="lead">Set up {season.name}</p>
      <p>
        <span>{`Milestone ${current + 1} of ${milestones.length}: ${milestones[current].name}`}</span>
        {parts > 1 && (
          <>
            {" · "}
            <span>{`Part ${part} of ${parts}: ${wizardSteps[view]}`}</span>
          </>
        )}
        {" · Your progress is saved with this season."}
      </p>
      <nav aria-label="Setup milestones">
        <ol className="progress milestones">
          {milestones.map((m, index) => {
            const done = season.setupStep > m.last;
            const reached = season.setupStep >= m.first;
            const mark = done ? "✓ " : index === current ? "● " : "○ ";
            return (
              <li
                key={m.name}
                aria-current={index === current ? "step" : undefined}
              >
                {reached ? (
                  <button
                    type="button"
                    className="link"
                    onClick={() => openMilestone(index)}
                  >
                    {mark}
                    {m.name}
                  </button>
                ) : (
                  <span>
                    {mark}
                    {m.name}
                  </span>
                )}
              </li>
            );
          })}
        </ol>
      </nav>
      {!finished && milestoneOf(resumeStep) !== current && (
        <p className="resume">
          <span>{`Next up: ${wizardSteps[resumeStep]}`}</span>
          <button type="button" onClick={() => goTo(resumeStep)}>
            Continue setup
          </button>
        </p>
      )}
      {view === 0 && (
        <Card
          title="Basics"
          notice={notices[0]}
          action="Save & continue"
          onSubmit={() =>
            void saveAndContinue(0, "/api/admin/seasons/setup", {
              year: Number(year),
              name,
              isSimulation,
            })
          }
        >
          <Field label="Season year">
            <input
              type="number"
              value={year}
              onChange={(event) => setYear(event.target.value)}
            />
          </Field>
          <Field label="Season name">
            <input
              value={name}
              onChange={(event) => setName(event.target.value)}
            />
          </Field>
          <label className="check">
            <input
              type="checkbox"
              checked={isSimulation}
              onChange={(event) => setIsSimulation(event.target.checked)}
            />
            Simulation season
          </label>
        </Card>
      )}
      {view === 1 && (
        <Card
          title="Money"
          notice={notices[1]}
          action="Save & continue"
          onBack={back}
          onSubmit={() =>
            void saveAndContinue(1, "/api/admin/seasons/setup/money", {
              buyIn: Number(buyIn),
              main: Number(stakes.Main),
              dog: Number(stakes.Dog),
              regularSeason: Number(stakes["Regular Season"]),
              playoff: Number(stakes.Playoff),
            })
          }
        >
          <Field label="Buy-in">
            <input
              type="number"
              step="0.01"
              value={buyIn}
              onChange={(event) => setBuyIn(event.target.value)}
            />
          </Field>
          {stakeNames.map((pool) => (
            <Field key={pool} label={pool}>
              <input
                type="number"
                step="0.01"
                value={stakes[pool]}
                onChange={(event) => {
                  const { value } = event.target;
                  setStakes((current) => ({ ...current, [pool]: value }));
                }}
              />
            </Field>
          ))}
          <span className="total">{`Pool total ${dollars(poolTotal)}`}</span>
        </Card>
      )}
      {view === 2 && (
        <Card
          title="Weekly deadline"
          notice={notices[2]}
          action="Save & continue"
          onBack={back}
          onSubmit={() =>
            void saveAndContinue(2, "/api/admin/seasons/setup/deadline", {
              teamPickDeadlineEastern: `${deadline}:00`,
            })
          }
        >
          <Field label="Team picks lock Sundays at">
            <input
              type="time"
              value={deadline}
              onChange={(event) => setDeadline(event.target.value)}
            />
          </Field>
          <span>Eastern Time</span>
        </Card>
      )}
      {view === 3 && (
        <Card
          title="Teams and point values"
          notice={
            notices[3] ??
            (teamsError
              ? {
                  kind: "error",
                  text: `Could not load the teams: ${teamsError}`,
                }
              : undefined)
          }
          action={teams ? "Save & continue" : undefined}
          onBack={back}
          onSubmit={() => void saveTeams()}
        >
          <span className="hint">
            Set each team's prior-season division finish. Point values derive
            automatically.
          </span>
          {!teams && !teamsError && <span>Loading teams…</span>}
          {teams &&
            [...new Set(teams.map((team) => team.division))].map((division) => (
              <fieldset key={division} className="division">
                <legend>{division}</legend>
                {teams
                  .filter((team) => team.division === division)
                  .map((team) => (
                    <div key={team.teamId} className="team-row">
                      <span>{team.name}</span>
                      <select
                        aria-label={team.name}
                        value={picks[team.teamId] ?? ""}
                        onChange={(event) => {
                          const { value } = event.target;
                          setPicks((current) => ({
                            ...current,
                            [team.teamId]: value,
                          }));
                        }}
                      >
                        <option value="">Finish…</option>
                        {finishLabels.map((label, index) => (
                          <option key={label} value={String(index + 1)}>
                            {label}
                          </option>
                        ))}
                      </select>
                      <span>{`Point value ${picks[team.teamId] || "–"}`}</span>
                    </div>
                  ))}
              </fieldset>
            ))}
        </Card>
      )}
      {view === 4 && (
        <ScheduleStep
          season={season}
          onSeason={onSeason}
          onBack={back}
          onContinue={() => goTo(5)}
        />
      )}
      {view === 5 && (
        <DogsStep
          season={season}
          onSeason={onSeason}
          onBack={back}
          onContinue={() => goTo(6)}
        />
      )}
      {view === 6 && (
        <FinesStep
          season={season}
          onSeason={onSeason}
          onBack={back}
          onContinue={() => goTo(7)}
        />
      )}
      {view === 7 && (
        <RulesStep
          season={season}
          onSeason={onSeason}
          onBack={back}
          onContinue={() => goTo(8)}
        />
      )}
      {view === 8 && (
        <CoachesStep
          season={season}
          onSeason={onSeason}
          onBack={back}
          onContinue={() => goTo(9)}
        />
      )}
      {view === 9 && (
        <ReviewStep
          season={season}
          onSeason={onSeason}
          onBack={back}
          onFix={goTo}
        />
      )}
    </>
  );
}

type ScheduleGame = {
  providerGameId: string;
  homeTeam: string;
  awayTeam: string;
  kickoff: string;
};
type SchedulePlan = {
  imported: boolean;
  fingerprint: string | null;
  season: number;
  gameCount: number;
  weeks: { week: number; games: ScheduleGame[] }[];
};

const easternKickoff = new Intl.DateTimeFormat("en-US", {
  timeZone: "America/New_York",
  weekday: "short",
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit",
  timeZoneName: "short",
});
const manualNote = "Manual/CSV import will remain available.";
const weekCount = (plan: SchedulePlan) => plan.weeks.length;

// Reads the saved schedule on open; the provider is contacted only when the admin asks for a preview, and nothing is saved until the preview is confirmed.
function ScheduleStep({
  season,
  onSeason,
  onBack,
  onContinue,
}: {
  season: Season;
  onSeason: (season: Season) => void;
  onBack?: () => void;
  onContinue: () => void;
}) {
  const [saved, setSaved] = useState<SchedulePlan>();
  const [preview, setPreview] = useState<SchedulePlan>();
  const [notice, setNotice] = useState<Notice>();
  const [busy, setBusy] = useState(false);
  const [empty, setEmpty] = useState(false);

  useEffect(() => {
    void send<SchedulePlan>("GET", "/api/admin/seasons/setup/schedule").then(
      (result) =>
        "value" in result
          ? setSaved(result.value)
          : setNotice({
              kind: "error",
              text: `Could not load the saved schedule: ${result.error}`,
            }),
    );
  }, []);

  async function previewSchedule() {
    setBusy(true);
    setNotice(undefined);
    setEmpty(false);
    const result = await send<SchedulePlan>(
      "POST",
      "/api/admin/seasons/setup/schedule/preview",
    );
    setBusy(false);
    if (!("value" in result))
      return setNotice({
        kind: "error",
        text: `${result.error} ${manualNote}`,
      });
    if (result.value.gameCount === 0) setEmpty(true);
    else setPreview(result.value);
  }

  async function importSchedule() {
    if (!preview?.fingerprint) return;
    setBusy(true);
    setNotice(undefined);
    const result = await send<SchedulePlan>(
      "POST",
      "/api/admin/seasons/setup/schedule/import",
      { fingerprint: preview.fingerprint },
    );
    if (!("value" in result)) {
      setBusy(false);
      return setNotice({ kind: "error", text: result.error });
    }
    setSaved(result.value);
    setPreview(undefined);
    // Weeks and dogs is the next unbuilt step; the resume point never moves backwards.
    const target = Math.max(season.setupStep, 5);
    if (target !== season.setupStep) {
      const recorded = await send("PUT", "/api/admin/seasons/setup/step", {
        step: target,
      });
      if ("value" in recorded) onSeason(recorded.value);
      else
        setNotice({
          kind: "error",
          text: `Imported, but your progress could not be recorded: ${recorded.error}`,
        });
    }
    setBusy(false);
  }

  const shown = saved?.imported ? saved : preview;
  return (
    <Card
      title="Schedule import"
      notice={notice}
      action={
        !saved
          ? undefined
          : saved.imported
            ? "Continue"
            : preview
              ? `Import ${preview.gameCount} games`
              : "Preview schedule"
      }
      onBack={onBack}
      onSubmit={() =>
        busy
          ? undefined
          : saved?.imported
            ? onContinue()
            : void (preview ? importSchedule() : previewSchedule())
      }
    >
      {!saved && !notice && <span>Checking the saved schedule…</span>}
      {saved && !saved.imported && !preview && (
        <span className="hint">
          {`Swan will fetch the ${season.year} regular-season schedule from BALLDONTLIE. Nothing is saved until you confirm the preview.`}
        </span>
      )}
      {busy && <span>Working…</span>}
      {empty && (
        <span role="status" className="hint">
          {`No schedule has been published for ${season.year} yet. ${manualNote}`}
        </span>
      )}
      {saved?.imported && (
        <span role="status" className="hint">
          {`Schedule imported: ${saved.gameCount} games in ${weekCount(saved)} weeks.`}
        </span>
      )}
      {preview && !saved?.imported && (
        <span role="status" className="hint">
          {`Preview: ${preview.gameCount} games in ${weekCount(preview)} weeks. Nothing is saved until you import.`}
        </span>
      )}
      {shown && shown.weeks.length > 0 && (
        <details className="schedule-detail">
          <summary>{`Review games by week (${shown.gameCount})`}</summary>
          {shown.weeks.map((week) => (
            <fieldset key={week.week} className="division">
              <legend>{`Week ${week.week}`}</legend>
              <ul className="games">
                {week.games.map((game) => (
                  <li key={game.providerGameId}>
                    {`${game.awayTeam} at ${game.homeTeam} · ${easternKickoff.format(new Date(game.kickoff))}`}
                  </li>
                ))}
              </ul>
            </fieldset>
          ))}
        </details>
      )}
      {preview && (
        <button
          type="button"
          className="secondary"
          onClick={() => setPreview(undefined)}
        >
          Discard preview
        </button>
      )}
    </Card>
  );
}

type DogWeek = {
  week: number;
  selected: boolean;
  required: boolean;
  suggested: boolean;
  reason: string;
};
type DogPlan = { weeks: DogWeek[]; selected: number[] };

// The weeks come from the imported schedule only. A saved selection is shown as saved; before one exists the required week and the holiday suggestions are pre-checked for the admin to confirm.
function DogsStep({
  season,
  onSeason,
  onBack,
  onContinue,
}: {
  season: Season;
  onSeason: (season: Season) => void;
  onBack?: () => void;
  onContinue: () => void;
}) {
  const [plan, setPlan] = useState<DogPlan>();
  const [chosen, setChosen] = useState<number[]>([]);
  const [notice, setNotice] = useState<Notice>();

  const choose = (loaded: DogPlan) =>
    loaded.selected.length > 0
      ? loaded.selected
      : loaded.weeks
          .filter((week) => week.required || week.suggested)
          .map((week) => week.week);

  useEffect(() => {
    void send<DogPlan>("GET", "/api/admin/seasons/setup/double-dog-weeks").then(
      (result) => {
        if ("value" in result) {
          setPlan(result.value);
          setChosen(choose(result.value));
        } else setNotice({ kind: "error", text: result.error });
      },
    );
  }, []);

  async function save() {
    if (chosen.length < 3)
      return setNotice({
        kind: "error",
        text: `Choose at least 3 weeks: Week 2 plus ${3 - Math.max(chosen.length, 1)} more.`,
      });
    const saved = await send<DogPlan>(
      "PUT",
      "/api/admin/seasons/setup/double-dog-weeks",
      { weeks: [...chosen].sort((a, b) => a - b) },
    );
    if (!("value" in saved))
      return setNotice({ kind: "error", text: saved.error });
    setPlan(saved.value);
    // Fines and payouts is the next page; the resume point never moves backwards.
    const target = Math.max(season.setupStep, 6);
    if (target !== season.setupStep) {
      const recorded = await send("PUT", "/api/admin/seasons/setup/step", {
        step: target,
      });
      if (!("value" in recorded))
        return setNotice({
          kind: "error",
          text: `Saved, but your progress could not be recorded: ${recorded.error}`,
        });
      onSeason(recorded.value);
    }
    onContinue();
  }

  const full = chosen.length >= 4;
  return (
    <Card
      title="Weeks and dogs"
      notice={notice}
      action={plan ? "Save & continue" : undefined}
      onBack={onBack}
      onSubmit={() => void save()}
    >
      {!plan && !notice && <span>Loading the imported weeks…</span>}
      {plan && (
        <>
          <span className="hint" role="status">
            {`${chosen.length} selected · choose 3–4`}
          </span>
          <ul className="weeks">
            {plan.weeks.map((week) => (
              <li key={week.week}>
                <input
                  type="checkbox"
                  aria-label={`Week ${week.week}`}
                  checked={chosen.includes(week.week)}
                  disabled={
                    week.required || (full && !chosen.includes(week.week))
                  }
                  onChange={(event) => {
                    const { checked } = event.target;
                    setChosen((current) =>
                      checked
                        ? [...current, week.week]
                        : current.filter((value) => value !== week.week),
                    );
                  }}
                />
                <span>{`Week ${week.week}`}</span>
                <span className="reason">{week.reason}</span>
              </li>
            ))}
          </ul>
        </>
      )}
    </Card>
  );
}

type PoolPayout = {
  pool: string;
  stake: number;
  pot: number;
  places: { position: number; percent: number; amount: number }[];
};
type FinesPlan = {
  configured: boolean;
  missedPickFine: number | null;
  activeCoaches: number;
  pools: PoolPayout[];
};
type PlaceRow = { position: number; percent: string };

const ordinal = (n: number) => {
  const teen = n % 100 >= 11 && n % 100 <= 13;
  const suffix = ["th", "st", "nd", "rd"][teen || n % 10 > 3 ? 0 : n % 10];
  return `${n}${suffix}`;
};
const hundredths = (value: string) => Math.round(Number(value) * 100);
const validPercent = (value: string) =>
  value.trim() !== "" &&
  Number.isFinite(Number(value)) &&
  Number(value) > 0 &&
  Math.abs(Number(value) * 100 - hundredths(value)) < 1e-6;

// The same sharing rule the server applies: every placing but the first is its percent of the pot rounded down to the cent, and first place takes the remainder.
function shares(potCents: number, rows: PlaceRow[]) {
  const ordered = [...rows].sort((a, b) => a.position - b.position);
  const amounts = ordered.map((row) =>
    Math.floor((potCents * hundredths(row.percent)) / 10000),
  );
  if (amounts.length > 0)
    amounts[0] =
      potCents - amounts.slice(1).reduce((sum, cents) => sum + cents, 0);
  return ordered.map((row, index) => ({
    position: row.position,
    cents: amounts[index],
  }));
}

const totalHundredths = (rows: PlaceRow[]) =>
  rows.reduce(
    (sum, row) =>
      sum +
      (Number.isFinite(Number(row.percent)) ? hundredths(row.percent) : 0),
    0,
  );
const rowsValid = (rows: PlaceRow[]) =>
  rows.length > 0 &&
  rows.every((row) => validPercent(row.percent)) &&
  totalHundredths(rows) === 10000;

// The fine and each pool's payout percentages; the calculator uses this season's active coach count and moves no money.
function FinesStep({
  season,
  onSeason,
  onBack,
  onContinue,
}: {
  season: Season;
  onSeason: (season: Season) => void;
  onBack?: () => void;
  onContinue: () => void;
}) {
  const [plan, setPlan] = useState<FinesPlan>();
  const [fine, setFine] = useState("5.00");
  const [rows, setRows] = useState<Record<string, PlaceRow[]>>({});
  const [notice, setNotice] = useState<Notice>();

  const load = (loaded: FinesPlan) => {
    setPlan(loaded);
    setFine((loaded.missedPickFine ?? 5).toFixed(2));
    setRows(
      Object.fromEntries(
        loaded.pools.map((pool) => [
          pool.pool,
          pool.places.length > 0
            ? pool.places.map((place) => ({
                position: place.position,
                percent: place.percent.toFixed(2),
              }))
            : [{ position: 1, percent: "100.00" }],
        ]),
      ),
    );
  };

  useEffect(() => {
    void send<FinesPlan>(
      "GET",
      "/api/admin/seasons/setup/fines-and-payouts",
    ).then((result) =>
      "value" in result
        ? load(result.value)
        : setNotice({ kind: "error", text: result.error }),
    );
  }, []);

  const change = (pool: string, update: (current: PlaceRow[]) => PlaceRow[]) =>
    setRows((current) => ({ ...current, [pool]: update(current[pool]) }));

  async function save() {
    if (!plan) return;
    const fail = (text: string) => setNotice({ kind: "error", text });
    const fineValue = Number(fine);
    if (fine.trim() === "" || !validPercent(fine) || fineValue <= 0)
      return fail(
        "The missed-pick fine must be greater than zero and in whole cents.",
      );
    for (const pool of plan.pools)
      if (!rowsValid(rows[pool.pool]))
        return fail(
          `${pool.pool}: every placing needs a percentage above zero, and they must total exactly 100.00 (they total ${(totalHundredths(rows[pool.pool]) / 100).toFixed(2)}).`,
        );

    const saved = await send<FinesPlan>(
      "PUT",
      "/api/admin/seasons/setup/fines-and-payouts",
      {
        missedPickFine: fineValue,
        payouts: plan.pools.map((pool) => ({
          pool: pool.pool,
          places: [...rows[pool.pool]]
            .sort((a, b) => a.position - b.position)
            .map((row) => ({
              position: row.position,
              percent: Number(row.percent),
            })),
        })),
      },
    );
    if (!("value" in saved)) return fail(saved.error);
    load(saved.value);
    // League settings is the next page; the resume point never moves backwards.
    const target = Math.max(season.setupStep, 7);
    if (target !== season.setupStep) {
      const recorded = await send("PUT", "/api/admin/seasons/setup/step", {
        step: target,
      });
      if (!("value" in recorded))
        return fail(
          `Saved, but your progress could not be recorded: ${recorded.error}`,
        );
      onSeason(recorded.value);
    }
    onContinue();
  }

  const coaches = plan?.activeCoaches ?? 0;
  return (
    <Card
      title="Fines and payouts"
      wide
      notice={notice}
      action={plan ? "Save & continue" : undefined}
      onBack={onBack}
      onSubmit={() => void save()}
    >
      {!plan && !notice && <span>Loading the payout settings…</span>}
      {plan && (
        <>
          <span className="hint">{`${coaches} active ${coaches === 1 ? "coach" : "coaches"}`}</span>
          <label>
            Missed team-pick fine
            <input
              type="number"
              step="0.01"
              value={fine}
              onChange={(event) => setFine(event.target.value)}
            />
          </label>
          {plan.pools.map((pool) => {
            const current = rows[pool.pool] ?? [];
            const total = totalHundredths(current);
            const potCents = Math.round(pool.stake * 100) * coaches;
            return (
              <fieldset key={pool.pool} className="division">
                <legend>{`${pool.pool} pool`}</legend>
                <span className="hint">{`${pool.pool} pool · ${dollars(Math.round(pool.stake * 100))} per coach · current pot ${dollars(potCents)}`}</span>
                {[...current]
                  .sort((a, b) => a.position - b.position)
                  .map((row) => (
                    <div key={row.position} className="place-row">
                      <span>{`${ordinal(row.position)} place`}</span>
                      <input
                        type="number"
                        step="0.01"
                        aria-label={`${pool.pool} ${ordinal(row.position)} place percent`}
                        value={row.percent}
                        onChange={(event) => {
                          const { value } = event.target;
                          change(pool.pool, (existing) =>
                            existing.map((other) =>
                              other.position === row.position
                                ? { ...other, percent: value }
                                : other,
                            ),
                          );
                        }}
                      />
                      <span>%</span>
                      {current.length > 1 && (
                        <button
                          type="button"
                          className="secondary"
                          aria-label={`Remove ${pool.pool} ${ordinal(row.position)} place`}
                          onClick={() =>
                            change(pool.pool, (existing) =>
                              existing.filter(
                                (other) => other.position !== row.position,
                              ),
                            )
                          }
                        >
                          Remove
                        </button>
                      )}
                    </div>
                  ))}
                <button
                  type="button"
                  className="secondary"
                  aria-label={`Add ${pool.pool} placing`}
                  onClick={() =>
                    change(pool.pool, (existing) => [
                      ...existing,
                      {
                        position:
                          Math.max(0, ...existing.map((row) => row.position)) +
                          1,
                        percent: "",
                      },
                    ])
                  }
                >
                  + Add placing
                </button>
                <span className="hint">
                  {`${pool.pool} total ${(total / 100).toFixed(2)}%${rowsValid(current) ? "" : " — must be 100.00%"}`}
                </span>
              </fieldset>
            );
          })}
          <div className="calculator">
            <strong>Calculator</strong>
            {coaches === 0 && (
              <span role="status" className="hint">
                No active coaches yet. Payouts update as coaches are added.
              </span>
            )}
            <ul>
              {plan.pools.map((pool) => {
                const current = rows[pool.pool] ?? [];
                const potCents = Math.round(pool.stake * 100) * coaches;
                return (
                  <li key={pool.pool}>
                    {rowsValid(current)
                      ? `${pool.pool}: ${shares(potCents, current)
                          .map(
                            (share) =>
                              `${ordinal(share.position)} ${dollars(share.cents)}`,
                          )
                          .join(" · ")}`
                      : `${pool.pool}: fix the percentages to see payouts`}
                  </li>
                );
              })}
            </ul>
          </div>
        </>
      )}
    </Card>
  );
}

type PointColorRow = { points: number; color: string };
type RulesPlan = {
  teamMayAlsoBeDog: boolean;
  pointColors: PointColorRow[];
  recordHolder: string;
};

const colorChoices = ["Green", "Blue", "Yellow", "Red"];
// The fixed rules, in the PRD's words (R17 to R19 are the settings above them).
const fixedRules: [string, string][] = [
  ["R1", "Each coach has exactly 9 NFL teams on their season roster."],
  ["R2", "Each week a coach starts exactly 3 of their 9 teams."],
  [
    "R3",
    "A team's point value is its division finish last season: 1st = 1, 2nd = 2, 3rd = 3, 4th = 4.",
  ],
  [
    "R4",
    "A started team that wins scores its point value; a loss or tie scores 0.",
  ],
  [
    "R5",
    "Perfect Week: +3 bonus when all 3 started teams win; the dog is excluded.",
  ],
  [
    "R6",
    "Each team pick locks at the earlier of that team's kickoff and the weekly deadline.",
  ],
  [
    "R7",
    "Each week a coach picks 1 underdog (the +spread side); the double-dog weeks allow 2.",
  ],
  [
    "R8",
    "A dog pick must be submitted before that game's kickoff; Monday-night dogs are allowed until then.",
  ],
  [
    "R9",
    "A dog that wins outright scores its spread rounded up (+6.5 pays 7).",
  ],
  ["R10", "A missed team-pick submission is a fine, added to the fine pool."],
  [
    "R11",
    "Going Perf = all 3 team picks and the dog win in one week; 0fer = all 3 picks and the dog lose.",
  ],
  [
    "R12",
    "Playoff teams earn 3 points for their coaches for qualifying, counted only in the playoff pool.",
  ],
  [
    "R13",
    "Playoff points use the team's season point value times the round multiplier: Wild Card x2, Divisional x4, Conference x6, Super Bowl x8.",
  ],
  [
    "R14",
    "The conference winner that held the #1 seed pays 10 points to coaches holding it.",
  ],
  [
    "R15",
    "Dog points count only in the dog pool, never in the main pool or the regular-season pool.",
  ],
  [
    "R16",
    "A coach may use the same team as a dog any number of times in a season; there is no repeated team or game within one double-dog week.",
  ],
  [
    "R20",
    "A team whose game kicks off before the Sunday deadline must be picked before that kickoff; the other picks stay open until the deadline.",
  ],
  [
    "R21",
    "A coach sees another coach's locked picks only after all of their own picks for that week are locked; admins always see everything.",
  ],
];

// R17 to R19 are the only owner-controlled rules. Only R17 is used by anything yet (score recomputes); R18 and R19 are stored for later features.
function RulesStep({
  season,
  onSeason,
  onBack,
  onContinue,
}: {
  season: Season;
  onSeason: (season: Season) => void;
  onBack?: () => void;
  onContinue: () => void;
}) {
  const [plan, setPlan] = useState<RulesPlan>();
  const [notice, setNotice] = useState<Notice>();

  useEffect(() => {
    void send<RulesPlan>(
      "GET",
      "/api/admin/seasons/setup/league-settings",
    ).then((result) =>
      "value" in result
        ? setPlan(result.value)
        : setNotice({ kind: "error", text: result.error }),
    );
  }, []);

  const duplicate = plan
    ? new Set(plan.pointColors.map((entry) => entry.color)).size !==
      plan.pointColors.length
    : false;

  async function save() {
    if (!plan) return;
    if (duplicate)
      return setNotice({
        kind: "error",
        text: "Each color can be used for only one point value.",
      });
    const saved = await send<RulesPlan>(
      "PUT",
      "/api/admin/seasons/setup/league-settings",
      plan,
    );
    if (!("value" in saved))
      return setNotice({ kind: "error", text: saved.error });
    setPlan(saved.value);
    // Coaches is the next page; the resume point never moves backwards.
    const target = Math.max(season.setupStep, 8);
    if (target !== season.setupStep) {
      const recorded = await send("PUT", "/api/admin/seasons/setup/step", {
        step: target,
      });
      if (!("value" in recorded))
        return setNotice({
          kind: "error",
          text: `Saved, but your progress could not be recorded: ${recorded.error}`,
        });
      onSeason(recorded.value);
    }
    onContinue();
  }

  return (
    <Card
      title="League settings"
      wide
      notice={notice}
      action={plan ? "Save & continue" : undefined}
      onBack={onBack}
      onSubmit={() => void save()}
    >
      {!plan && !notice && <span>Loading the league settings…</span>}
      {plan && (
        <>
          <label className="check">
            <input
              type="checkbox"
              checked={plan.teamMayAlsoBeDog}
              onChange={(event) =>
                setPlan({ ...plan, teamMayAlsoBeDog: event.target.checked })
              }
            />
            R17: A team may be both a started team and the same week's dog
          </label>
          <span className="hint">
            R17 takes effect the next time a week is scored or recomputed; weeks
            that are already scored are not changed.
          </span>
          <fieldset className="division">
            <legend>R18: Tiebreaker colors for point values</legend>
            {plan.pointColors.map((entry) => (
              <label key={entry.points}>
                {`${entry.points} point color`}
                <select
                  value={entry.color}
                  onChange={(event) => {
                    const { value } = event.target;
                    setPlan({
                      ...plan,
                      pointColors: plan.pointColors.map((other) =>
                        other.points === entry.points
                          ? { ...other, color: value }
                          : other,
                      ),
                    });
                  }}
                >
                  {colorChoices.map((color) => (
                    <option key={color} value={color}>
                      {color}
                    </option>
                  ))}
                </select>
              </label>
            ))}
            {duplicate && (
              <span className="hint">
                Each color can be used for only one point value.
              </span>
            )}
          </fieldset>
          <fieldset className="division">
            <legend>R19: Who holds a Going Perf or 0fer record</legend>
            {[
              ["FirstOccurrence", "First occurrence"],
              ["MostOccurrences", "Most occurrences"],
            ].map(([value, label]) => (
              <label key={value} className="check">
                <input
                  type="radio"
                  name="recordHolder"
                  checked={plan.recordHolder === value}
                  onChange={() => setPlan({ ...plan, recordHolder: value })}
                />
                {label}
              </label>
            ))}
            <span className="hint">First occurrence is the default.</span>
          </fieldset>
          <span className="hint">
            R18 and R19 are saved now; the tiebreaker and records features that
            will use them come later.
          </span>
          <div className="fixed-rules">
            <strong>Fixed rules (not settings)</strong>
            <ul aria-label="Fixed rules">
              {fixedRules.map(([id, text]) => (
                <li key={id}>
                  <strong>{id}</strong> {text}
                </li>
              ))}
            </ul>
          </div>
        </>
      )}
    </Card>
  );
}

type CoachPerson = { userId: string; name: string; email: string };
type CoachesPlan = {
  prior: { id: string; year: number; name: string } | null;
  coaches: (CoachPerson & { membershipId: string; active: boolean })[];
  candidates: (CoachPerson & { selected: boolean })[];
  activeCoaches: number;
};
type NewCoach = { key: number; email: string; name: string };

// The same limits the server enforces (CoachRoster).
const maxCoachName = 200;
const maxCoachEmail = 320;
const looksLikeEmail = (value: string) =>
  /^[^\s@,<>]+@[^\s@,<>]+\.[^\s@,<>]+$/.test(value.trim());

// Prepares the coach list only: people carried forward from the previous season, coaches already saved, and new email/name rows. No invitation or email is sent from here.
function CoachesStep({
  season,
  onSeason,
  onBack,
  onContinue,
}: {
  season: Season;
  onSeason: (season: Season) => void;
  onBack?: () => void;
  onContinue: () => void;
}) {
  const [plan, setPlan] = useState<CoachesPlan>();
  const [chosen, setChosen] = useState<string[]>([]);
  const [kept, setKept] = useState<CoachPerson[]>([]);
  const [added, setAdded] = useState<NewCoach[]>([]);
  const [notice, setNotice] = useState<Notice>();
  const nextKey = useRef(1);

  const load = (loaded: CoachesPlan) => {
    setPlan(loaded);
    setChosen(
      loaded.candidates
        .filter((candidate) => candidate.selected)
        .map((candidate) => candidate.userId),
    );
    // Saved active coaches who are not offered as carry-forward people stay visible so they can be removed.
    setKept(
      loaded.coaches.filter(
        (coach) =>
          coach.active &&
          !loaded.candidates.some(
            (candidate) => candidate.userId === coach.userId,
          ),
      ),
    );
    setAdded([]);
  };

  useEffect(() => {
    void send<CoachesPlan>("GET", "/api/admin/seasons/setup/coaches").then(
      (result) =>
        "value" in result
          ? load(result.value)
          : setNotice({ kind: "error", text: result.error }),
    );
  }, []);

  const picked = plan
    ? plan.candidates.filter((candidate) => chosen.includes(candidate.userId))
    : [];
  const count = picked.length + kept.length + added.length;

  async function save() {
    if (!plan) return;
    const fail = (text: string) => setNotice({ kind: "error", text });
    const list = [
      ...picked.map((person) => ({ email: person.email, name: person.name })),
      ...kept.map((person) => ({ email: person.email, name: person.name })),
      ...added.map((row) => ({
        email: row.email.trim().toLowerCase(),
        name: row.name.trim(),
      })),
    ];
    for (const [index, row] of added.entries()) {
      if (!looksLikeEmail(row.email) || row.email.trim().length > maxCoachEmail)
        return fail(
          `New coach ${index + 1}: enter a valid email address (up to ${maxCoachEmail} characters).`,
        );
      if (row.name.trim() === "" || row.name.trim().length > maxCoachName)
        return fail(
          `New coach ${index + 1}: enter a display name (up to ${maxCoachName} characters).`,
        );
    }
    const emails = list.map((entry) => entry.email.toLowerCase());
    const repeated = emails.find(
      (email, index) => emails.indexOf(email) !== index,
    );
    if (repeated) return fail(`${repeated} is listed more than once.`);

    const saved = await send<CoachesPlan>(
      "PUT",
      "/api/admin/seasons/setup/coaches",
      { coaches: list },
    );
    if (!("value" in saved)) return fail(saved.error);
    load(saved.value);
    // Review and create is the next page; the resume point never moves backwards.
    const target = Math.max(season.setupStep, 9);
    if (target !== season.setupStep) {
      const recorded = await send("PUT", "/api/admin/seasons/setup/step", {
        step: target,
      });
      if (!("value" in recorded))
        return fail(
          `Saved, but your progress could not be recorded: ${recorded.error}`,
        );
      onSeason(recorded.value);
    }
    onContinue();
  }

  return (
    <Card
      title="Coaches"
      wide
      notice={notice}
      action={plan ? "Save & continue" : undefined}
      onBack={onBack}
      onSubmit={() => void save()}
    >
      {!plan && !notice && <span>Loading the coach list…</span>}
      {plan && (
        <>
          <span className="hint">
            This only prepares the coach list for this season. No invitations or
            emails are sent from here.
          </span>
          <span className="hint" role="status">
            {`${count} ${count === 1 ? "coach" : "coaches"} selected`}
          </span>
          {count === 0 && (
            <span className="hint">
              No coaches yet. You can continue; the Review step will warn you
              before the season is created.
            </span>
          )}
          {plan.prior && plan.candidates.length > 0 && (
            <fieldset className="division">
              <legend>{`Carry forward from ${plan.prior.name || (plan.prior.year ? `the ${plan.prior.year} season` : "the previous season")}`}</legend>
              {plan.candidates.map((candidate) => (
                <label key={candidate.userId} className="check">
                  <input
                    type="checkbox"
                    checked={chosen.includes(candidate.userId)}
                    onChange={(event) => {
                      const { checked } = event.target;
                      setChosen((current) =>
                        checked
                          ? [...current, candidate.userId]
                          : current.filter((id) => id !== candidate.userId),
                      );
                    }}
                  />
                  {`${candidate.name} (${candidate.email})`}
                </label>
              ))}
            </fieldset>
          )}
          {kept.length > 0 && (
            <fieldset className="division">
              <legend>Already on this season's list</legend>
              {kept.map((person) => (
                <div key={person.userId} className="kept-row">
                  <span>{`${person.name} (${person.email})`}</span>
                  <button
                    type="button"
                    className="secondary"
                    aria-label={`Remove ${person.name}`}
                    onClick={() =>
                      setKept((current) =>
                        current.filter(
                          (other) => other.userId !== person.userId,
                        ),
                      )
                    }
                  >
                    Remove
                  </button>
                </div>
              ))}
            </fieldset>
          )}
          <fieldset className="division">
            <legend>New coaches</legend>
            {added.map((row, index) => (
              <div key={row.key} className="coach-row">
                <input
                  type="email"
                  aria-label={`New coach ${index + 1} email`}
                  value={row.email}
                  onChange={(event) => {
                    const { value } = event.target;
                    setAdded((current) =>
                      current.map((other) =>
                        other.key === row.key
                          ? { ...other, email: value }
                          : other,
                      ),
                    );
                  }}
                />
                <input
                  aria-label={`New coach ${index + 1} name`}
                  value={row.name}
                  onChange={(event) => {
                    const { value } = event.target;
                    setAdded((current) =>
                      current.map((other) =>
                        other.key === row.key
                          ? { ...other, name: value }
                          : other,
                      ),
                    );
                  }}
                />
                <button
                  type="button"
                  className="secondary"
                  aria-label={`Remove new coach ${index + 1}`}
                  onClick={() =>
                    setAdded((current) =>
                      current.filter((other) => other.key !== row.key),
                    )
                  }
                >
                  Remove
                </button>
              </div>
            ))}
            <button
              type="button"
              className="secondary"
              onClick={() =>
                setAdded((current) => [
                  ...current,
                  { key: nextKey.current++, email: "", name: "" },
                ])
              }
            >
              Add coach
            </button>
          </fieldset>
        </>
      )}
    </Card>
  );
}

type ReviewIssue = { code: string; message: string; step: string };
type Review = {
  completed: boolean;
  ready: boolean;
  blockers: ReviewIssue[];
  warnings: ReviewIssue[];
  basics: { year: number; name: string; isSimulation: boolean };
  money: { buyIn: number; pools: { name: string; stake: number }[] };
  payouts: FinesPlan;
  teamPickDeadlineEastern: string | null;
  teams: { entered: number; total: number; teams: TeamRow[] };
  schedule: { weeks: number; games: number; complete: boolean };
  doubleDogWeeks: number[];
  leagueSettings: RulesPlan;
  activeCoaches: number;
};

// The wizard page each issue's `step` name belongs to.
const stepPages: Record<string, number> = {
  Basics: 0,
  Money: 1,
  WeeklyDeadline: 2,
  TeamsAndPoints: 3,
  ScheduleImport: 4,
  WeeksAndDogs: 5,
  FinesAndPayouts: 6,
  LeagueSettings: 7,
  Coaches: 8,
};

const clock = (value: string | null) => {
  if (!value) return "the default time";
  const [hours, minutes] = value.split(":").map(Number);
  return `${hours % 12 === 0 ? 12 : hours % 12}:${String(minutes).padStart(2, "0")} ${hours < 12 ? "AM" : "PM"}`;
};

// Shown as the Eastern wall-clock time the commissioner typed; the string is read as UTC only so the browser's own zone never shifts it.
const deadlineText = (value: string) =>
  `${new Intl.DateTimeFormat("en-US", {
    timeZone: "UTC",
    weekday: "short",
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  }).format(new Date(`${value}:00Z`))} Eastern Time`;

// Everything here is read from saved data. Opening the roster draft finishes setup if it is not finished yet, then starts the season.
// The commissioner's final decision: the roster draft deadline (Eastern Time), then a confirmation. The server checks the caller really is its commissioner.
function ReviewStep({
  season,
  onSeason,
  onBack,
  onFix,
}: {
  season: Season;
  onSeason: (season: Season) => void;
  onBack?: () => void;
  onFix: (page: number) => void;
}) {
  const refreshAccess = useContext(RefreshAccess);
  const [review, setReview] = useState<Review>();
  const [notice, setNotice] = useState<Notice>();
  const [deadline, setDeadline] = useState("");
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState("");
  const [started, setStarted] = useState<string>();
  const confirmHeading = useRef<HTMLHeadingElement>(null);
  const openButton = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    void send<Review>("GET", "/api/admin/seasons/setup/review").then(
      (result) =>
        "value" in result
          ? setReview(result.value)
          : setNotice({ kind: "error", text: result.error }),
    );
  }, []);

  const cancelled = useRef(false);
  useEffect(() => {
    if (confirming) confirmHeading.current?.focus();
    // The button is remounted once the confirmation closes, so focus goes back after that render.
    else if (cancelled.current) {
      cancelled.current = false;
      openButton.current?.focus();
    }
  }, [confirming]);

  function cancel() {
    cancelled.current = true;
    setConfirming(false);
    setProblem("");
  }

  function ask() {
    if (!deadline) return setProblem("Choose the roster draft deadline first.");
    setProblem("");
    setConfirming(true);
  }

  async function launch() {
    if (!review) return;
    setBusy(true);
    setProblem("");
    if (!review.completed) {
      const finished = await send<Review>(
        "POST",
        "/api/admin/seasons/setup/review/finish",
      );
      if (!("value" in finished)) {
        setBusy(false);
        return setProblem(finished.error);
      }
      setReview(finished.value);
      onSeason({ ...season, setupStep: 10 });
    }
    const result = await send<{ rosterDraftDeadline: string }>(
      "POST",
      "/api/admin/seasons/setup/activate",
      { rosterDraftDeadlineEastern: deadline },
    );
    setBusy(false);
    if (!("value" in result)) return setProblem(result.error);
    setConfirming(false);
    setStarted(formatDeadline(result.value.rosterDraftDeadline));
  }

  const issue = (entry: ReviewIssue, kind: "blocker" | "warning") => {
    const page = stepPages[entry.step];
    return (
      <li key={entry.code} className={`issue ${kind}`}>
        <span>{entry.message}</span>
        {page !== undefined && (
          <button
            type="button"
            className="secondary"
            aria-label={`Fix: ${wizardSteps[page]}`}
            onClick={() => onFix(page)}
          >
            Fix
          </button>
        )}
      </li>
    );
  };

  const blocked = review ? review.blockers.length > 0 : true;
  const edit = (label: string, page: number) => (
    <button
      type="button"
      className="secondary"
      aria-label={`Edit ${label}`}
      onClick={() => onFix(page)}
    >
      Edit
    </button>
  );
  return (
    <Card
      title="Ready to launch"
      wide
      notice={notice}
      onBack={onBack}
      onSubmit={() => (confirming || blocked ? undefined : ask())}
    >
      {!review && !notice && <span>Loading the review…</span>}
      {review && started && (
        <div role="status" className="done">
          <span>{`The season is live. The roster draft is open until ${started}.`}</span>{" "}
          <a
            href="/draft"
            onClick={(event) => {
              event.preventDefault();
              // Starting ends this person's Setup access and opens the roster draft, so read what they can reach now, then go there.
              void refreshAccess().then(() => goTo("/draft"));
            }}
          >
            Open the roster draft
          </a>
        </div>
      )}
      {review && !started && (
        <>
          {review.completed && blocked && (
            <span className="hint">
              Setup was finished earlier, but something has changed since.
            </span>
          )}
          {review.completed && !blocked && (
            <span role="status" className="done">
              Setup is finished. Choose the roster draft deadline to open it.
            </span>
          )}
          {review.blockers.length > 0 && (
            <fieldset className="division">
              <legend>Needs fixing</legend>
              <ul className="issues">
                {review.blockers.map((entry) => issue(entry, "blocker"))}
              </ul>
              <span className="hint">Fix these to open the roster draft.</span>
            </fieldset>
          )}
          {review.warnings.length > 0 && (
            <fieldset className="division">
              <legend>Worth a look</legend>
              <ul className="issues">
                {review.warnings.map((entry) => issue(entry, "warning"))}
              </ul>
            </fieldset>
          )}
          <ul className="summary-cards" aria-label="Setup summary">
            <li>
              <strong>League foundation</strong>
              <span>{`${review.basics.name} (${review.basics.year})${review.basics.isSimulation ? " · simulation" : ""}`}</span>
              <span>{`Buy-in ${dollars(Math.round(review.money.buyIn * 100))} · picks lock Sundays at ${clock(review.teamPickDeadlineEastern)} Eastern`}</span>
              {edit("League foundation", 0)}
            </li>
            <li>
              <strong>Schedule and scoring</strong>
              <span>{`${review.schedule.weeks} weeks, ${review.schedule.games} games imported`}</span>
              <span>{`${review.teams.entered} of ${review.teams.total} teams have a division finish · double-dog weeks: ${review.doubleDogWeeks.length > 0 ? review.doubleDogWeeks.join(", ") : "none chosen"}`}</span>
              {edit("Schedule and scoring", 3)}
            </li>
            <li>
              <strong>People and payouts</strong>
              <span>{`${review.activeCoaches} active ${review.activeCoaches === 1 ? "coach" : "coaches"}`}</span>
              <span>{`Missed team-pick fine ${review.payouts.missedPickFine === null ? "not set" : dollars(Math.round(review.payouts.missedPickFine * 100))}`}</span>
              {edit("People and payouts", 6)}
            </li>
          </ul>
          <details className="schedule-detail">
            <summary>View all details</summary>
            <fieldset className="division">
              <legend>Basics</legend>
              <span>{`${review.basics.name} (${review.basics.year})`}</span>
              <span>{`Simulation season: ${review.basics.isSimulation ? "yes" : "no"}`}</span>
            </fieldset>
            <fieldset className="division">
              <legend>Money</legend>
              <span>{`Buy-in ${dollars(Math.round(review.money.buyIn * 100))}`}</span>
              <span>
                {review.money.pools
                  .map(
                    (pool) =>
                      `${pool.name} ${dollars(Math.round(pool.stake * 100))}`,
                  )
                  .join(" · ")}
              </span>
              <span>{`Missed team-pick fine ${review.payouts.missedPickFine === null ? "not set" : dollars(Math.round(review.payouts.missedPickFine * 100))}`}</span>
              <ul className="issues">
                {review.payouts.pools.map((pool) => (
                  <li key={pool.pool}>
                    {pool.places.length === 0
                      ? `${pool.pool}: not set`
                      : `${pool.pool}: ${pool.places.map((place) => `${ordinal(place.position)} ${place.percent.toFixed(2)}%`).join(" · ")}`}
                  </li>
                ))}
              </ul>
            </fieldset>
            <fieldset className="division">
              <legend>Schedule and teams</legend>
              <span>{`Team picks lock Sundays at ${clock(review.teamPickDeadlineEastern)} Eastern`}</span>
              <span>{`${review.teams.entered} of ${review.teams.total} teams have a division finish`}</span>
              <span>{`${review.schedule.weeks} weeks, ${review.schedule.games} games imported`}</span>
              <span>{`Double-dog weeks: ${review.doubleDogWeeks.length > 0 ? review.doubleDogWeeks.join(", ") : "none chosen"}`}</span>
            </fieldset>
            <fieldset className="division">
              <legend>Teams and point values</legend>
              <ul className="issues" aria-label="Teams by division">
                {[
                  ...new Set(review.teams.teams.map((team) => team.division)),
                ].map((division) => (
                  <li key={division}>
                    {`${division}: ${review.teams.teams
                      .filter((team) => team.division === division)
                      .sort(
                        (a, b) =>
                          (a.finishPosition ?? 99) - (b.finishPosition ?? 99) ||
                          a.name.localeCompare(b.name),
                      )
                      .map((team) =>
                        team.finishPosition === null
                          ? `${team.name} not set`
                          : `${ordinal(team.finishPosition)} ${team.name} (${team.pointValue} ${team.pointValue === 1 ? "pt" : "pts"})`,
                      )
                      .join(" · ")}`}
                  </li>
                ))}
              </ul>
            </fieldset>
            <fieldset className="division">
              <legend>League settings and coaches</legend>
              <span>{`R17 ${review.leagueSettings.teamMayAlsoBeDog ? "on" : "off"} · R18 ${review.leagueSettings.pointColors.map((entry) => `${entry.points} ${entry.color}`).join(", ")} · R19 ${review.leagueSettings.recordHolder === "FirstOccurrence" ? "first occurrence" : "most occurrences"}`}</span>
              <span>{`${review.activeCoaches} active ${review.activeCoaches === 1 ? "coach" : "coaches"}`}</span>
            </fieldset>
          </details>
          <fieldset
            className="division launch"
            aria-label="Open the roster draft"
          >
            <legend>Open the roster draft</legend>
            <span className="hint">
              This is the last step. It starts the season and cannot be undone.
              Times are Eastern Time.
            </span>
            <label>
              Roster draft deadline (Eastern Time)
              <input
                type="datetime-local"
                value={deadline}
                disabled={confirming}
                onChange={(event) => {
                  setDeadline(event.target.value);
                  setProblem("");
                }}
              />
            </label>
            {!confirming && problem && <span role="alert">{problem}</span>}
            {!confirming && (
              <button
                type="button"
                ref={openButton}
                disabled={blocked}
                onClick={ask}
              >
                Open roster draft
              </button>
            )}
            {confirming && (
              <div
                role="alertdialog"
                aria-labelledby="launch-confirm-title"
                className="confirm"
                onKeyDown={(event) => {
                  if (event.key === "Escape") cancel();
                }}
              >
                <h3
                  id="launch-confirm-title"
                  tabIndex={-1}
                  ref={confirmHeading}
                >
                  Open the roster draft?
                </h3>
                <p>
                  {`Coaches choose and lock their nine teams until ${deadlineText(deadline)}. After that only you, the commissioner, can correct a roster.`}
                </p>
                {problem && <span role="alert">{problem}</span>}
                <div className="actions">
                  <button
                    type="button"
                    className="secondary"
                    disabled={busy}
                    onClick={cancel}
                  >
                    Cancel
                  </button>
                  <button
                    type="button"
                    disabled={busy}
                    onClick={() => void launch()}
                  >
                    Confirm and open
                  </button>
                </div>
              </div>
            )}
          </fieldset>
        </>
      )}
    </Card>
  );
}
