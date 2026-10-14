import { FormEvent, useEffect, useRef, useState } from "react";

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

export default function SeasonSetup() {
  const [loaded, setLoaded] = useState<Loaded>({ status: "loading" });

  useEffect(() => {
    void send("GET", "/api/admin/seasons/setup").then((result) =>
      setLoaded(
        "value" in result
          ? { status: "ready", season: result.value }
          : result.missing
            ? { status: "none" }
            : { status: "failed", error: result.error },
      ),
    );
  }, []);

  const apply = (season: Season) => setLoaded({ status: "ready", season });

  return (
    <section className="setup">
      <h2>Season setup</h2>
      <p>Set the foundation for the next Swan League season.</p>
      {loaded.status === "failed" && (
        <p role="alert">Could not load the Setup season: {loaded.error}</p>
      )}
      {loaded.status === "none" && <StartCard onSeason={apply} />}
      {loaded.status === "ready" && (
        <SetupCards season={loaded.season} onSeason={apply} />
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
}: {
  title: string;
  notice: Notice;
  children: React.ReactNode;
  onSubmit: () => void;
  action?: string;
  onBack?: () => void;
}) {
  return (
    <form
      className="card"
      aria-label={title}
      onSubmit={(event: FormEvent) => {
        event.preventDefault();
        onSubmit();
      }}
    >
      <h3>{title}</h3>
      {children}
      <div className="actions">
        {notice?.kind === "error" && <span role="alert">{notice.text}</span>}
        {notice?.kind === "saved" && <span role="status">{notice.text}</span>}
        {onBack && (
          <button type="button" className="secondary" onClick={onBack}>
            Back
          </button>
        )}
        {action && <button type="submit">{action}</button>}
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
        }).then((result) =>
          "value" in result
            ? onSeason(result.value)
            : setNotice({ kind: "error", text: result.error }),
        )
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

const wizardSteps = [
  "Basics",
  "Money",
  "Weekly deadline",
  "Teams and point values",
  "Schedule import",
] as const;
const lastStep = wizardSteps.length - 1;
const stepSlugs = ["basics", "money", "deadline", "teams", "schedule"];
const upcoming = ["Weeks and dogs"];

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

  return (
    <>
      <p className="lead">Set up {season.name}</p>
      <p>
        <span>{`Step ${view + 1} of ${wizardSteps.length}`}</span>
        {" · Your progress is saved with this season."}
      </p>
      <ol className="progress" aria-label="Setup progress">
        {wizardSteps.map((title, index) => (
          <li key={title} aria-current={index === view ? "step" : undefined}>
            {index < Math.min(season.setupStep, wizardSteps.length)
              ? "✓ "
              : index === view
                ? "● "
                : "○ "}
            {title}
          </li>
        ))}
      </ol>
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
        <ScheduleStep season={season} onSeason={onSeason} onBack={back} />
      )}
      <p>Coming next: {upcoming.join(" → ")}</p>
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
}: {
  season: Season;
  onSeason: (season: Season) => void;
  onBack?: () => void;
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
        saved?.imported || !saved
          ? undefined
          : preview
            ? `Import ${preview.gameCount} games`
            : "Preview schedule"
      }
      onBack={onBack}
      onSubmit={() =>
        busy ? undefined : void (preview ? importSchedule() : previewSchedule())
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
      {shown?.weeks.map((week) => (
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
