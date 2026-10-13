import { FormEvent, useEffect, useState } from "react";

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
};

type Notice = { kind: "saved" | "error"; text: string } | undefined;
type Loaded =
  | { status: "loading" }
  | { status: "none" }
  | { status: "failed"; error: string }
  | { status: "ready"; season: Season };

const stakeNames = ["Main", "Dog", "Regular Season", "Playoff"] as const;
const defaultDeadline = "13:00";

type Result = { season: Season } | { error: string; missing?: boolean };

async function send(
  method: string,
  url: string,
  body?: unknown,
): Promise<Result> {
  try {
    const response = await fetch(url, {
      method,
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (response.ok) return { season: (await response.json()) as Season };
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
        "season" in result
          ? { status: "ready", season: result.season }
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
      <p>
        Next up: teams and point values. Double-dog weeks are set after schedule
        import.
      </p>
    </section>
  );
}

function Card({
  title,
  notice,
  children,
  onSubmit,
  action,
}: {
  title: string;
  notice: Notice;
  children: React.ReactNode;
  onSubmit: () => void;
  action: string;
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
        <button type="submit">{action}</button>
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
          "season" in result
            ? onSeason(result.season)
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

  // A saved reply is the truth for every card, so the whole form state is rebuilt from it.
  function resync(saved: Season, card: string, text: string) {
    setYear(String(saved.year));
    setName(saved.name);
    setIsSimulation(saved.isSimulation);
    setBuyIn(String(saved.settings.buyIn.amount));
    setStakes(
      Object.fromEntries(
        stakeNames.map((pool) => [
          pool,
          String(saved.pools.find((p) => p.name === pool)?.stake.amount ?? 0),
        ]),
      ),
    );
    setDeadline(
      (saved.settings.teamPickDeadlineEastern ?? defaultDeadline).slice(0, 5),
    );
    setNotices({ [card]: { kind: "saved", text } });
    onSeason(saved);
  }

  const save = (
    card: string,
    text: string,
    method: string,
    url: string,
    body: unknown,
  ) =>
    void send(method, url, body).then((result) =>
      "season" in result
        ? resync(result.season, card, text)
        : setNotices((current) => ({
            ...current,
            [card]: { kind: "error", text: result.error },
          })),
    );

  const poolTotal = stakeNames.reduce(
    (sum, pool) => sum + toCents(stakes[pool]),
    0,
  );

  return (
    <>
      <Card
        title="Basics"
        notice={notices.basics}
        action="Save basics"
        onSubmit={() =>
          save("basics", "Basics saved", "PUT", "/api/admin/seasons/setup", {
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
      <Card
        title="Money"
        notice={notices.money}
        action="Save money"
        onSubmit={() =>
          save(
            "money",
            "Money saved",
            "PUT",
            "/api/admin/seasons/setup/money",
            {
              buyIn: Number(buyIn),
              main: Number(stakes.Main),
              dog: Number(stakes.Dog),
              regularSeason: Number(stakes["Regular Season"]),
              playoff: Number(stakes.Playoff),
            },
          )
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
              onChange={(event) =>
                setStakes({ ...stakes, [pool]: event.target.value })
              }
            />
          </Field>
        ))}
        <output>{`Pool total ${dollars(poolTotal)}`}</output>
      </Card>
      <Card
        title="Weekly deadline"
        notice={notices.deadline}
        action="Save deadline"
        onSubmit={() =>
          save(
            "deadline",
            "Deadline saved",
            "PUT",
            "/api/admin/seasons/setup/deadline",
            { teamPickDeadlineEastern: `${deadline}:00` },
          )
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
    </>
  );
}
