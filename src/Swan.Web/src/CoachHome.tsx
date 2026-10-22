import { Lock, Plus } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import {
  closedMessages,
  type DeskDog,
  type DeskGame,
  fetchDesk,
  type Loaded,
  points,
  when,
} from "./coachDesk";
import { goTo, timeLeft } from "./draftTime";
import {
  fetchStandings,
  ordinal,
  type Pool,
  poolLabel,
  pts,
  type StandingsData,
} from "./standingsData";

const ordinals = ["first", "second", "third", "fourth", "fifth"];

// An in-app link: a real address for new tabs and screen readers, without a page load.
function AppLink({
  to,
  className,
  children,
}: {
  to: string;
  className?: string;
  children: React.ReactNode;
}) {
  return (
    <a
      href={to}
      className={className}
      onClick={(event) => {
        event.preventDefault();
        goTo(to);
      }}
    >
      {children}
    </a>
  );
}

// Main and Dog pool positions, shown only when both load and the season has scores; Home never fails because of them.
function WhereYouStand() {
  const [pools, setPools] = useState<StandingsData[] | null>(null);
  useEffect(() => {
    let live = true;
    void Promise.all(
      (["Main", "Dog"] as Pool[]).map((pool) => fetchStandings(pool)),
    ).then((results) => {
      if (!live) return;
      const ready = results.flatMap((result) =>
        result.status === "ready" &&
        result.data.state === "ready" &&
        result.data.you
          ? [result.data]
          : [],
      );
      setPools(ready.length === results.length ? ready : null);
    });
    return () => {
      live = false;
    };
  }, []);

  if (!pools) return null;
  return (
    <section className="where-you-stand" aria-labelledby="stand-title">
      <div className="stand-head">
        <h2 id="stand-title">Where you stand</h2>
        <AppLink to="/standings">All standings</AppLink>
      </div>
      <ul className="stand-cards">
        {pools.map((pool) => (
          <li key={pool.pool}>
            <span className="stand-pool">{`${poolLabel(pool.pool)} pool`}</span>
            <span className="stand-rank">
              {ordinal(pool.you?.rank ?? 0)}{" "}
              <span>{`of ${pool.coachCount}`}</span>
            </span>
            <span className="stand-points">{pts(pool.you?.points ?? 0)}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}

const byLock = (a: { lockTime: string }, b: { lockTime: string }) =>
  a.lockTime.localeCompare(b.lockTime);

// What is left this week, read from saved picks. Changing picks happens on the Picks page.
export default function CoachHome() {
  const [loaded, setLoaded] = useState<Loaded>({ status: "loading" });
  const load = useCallback(async () => {
    setLoaded({ status: "loading" });
    setLoaded(await fetchDesk());
  }, []);

  useEffect(() => {
    void fetchDesk().then(setLoaded);
  }, []);

  if (loaded.status === "loading")
    return (
      <section aria-busy="true" aria-labelledby="home-title">
        <h1 id="home-title">Your week</h1>
        <p role="status">Loading your week…</p>
      </section>
    );

  if (loaded.status === "failed")
    return (
      <section aria-labelledby="home-title">
        <h1 id="home-title">Your week</h1>
        <div role="alert" className="notice problem">
          <strong>Could not load your week.</strong> {loaded.error}
        </div>
        <button type="button" onClick={() => void load()}>
          Try again
        </button>
      </section>
    );

  const { desk } = loaded;
  if (desk.state !== "open" || !desk.week)
    return (
      <section aria-labelledby="home-title">
        {desk.seasonName && <p className="eyebrow">{desk.seasonName}</p>}
        <h1 id="home-title">Your week</h1>
        <div className="band">
          <h2>Nothing to pick yet</h2>
          <p>{closedMessages[desk.state as keyof typeof closedMessages]}</p>
          {(desk.state === "no-roster" ||
            desk.state === "roster-incomplete") && (
            <AppLink to="/roster" className="button-link">
              Open your draft
            </AppLink>
          )}
          <button type="button" className="ghost" onClick={() => void load()}>
            Check again
          </button>
        </div>
        <WhereYouStand />
      </section>
    );

  const week = desk.week;
  const starts = desk.games.filter((g) => g.selected).sort(byLock);
  const dogs = desk.dogs.filter((d) => d.selected).sort(byLock);
  const startsLeft = Math.max(0, week.pickLimit - starts.length);
  const dogsLeft = Math.max(0, week.dogLimit - dogs.length);
  const owed = [
    startsLeft > 0 && `${startsLeft} ${startsLeft === 1 ? "start" : "starts"}`,
    dogsLeft > 0 && `${dogsLeft} ${dogsLeft === 1 ? "dog" : "dogs"}`,
  ].filter(Boolean);
  const title =
    owed.length > 0
      ? `${owed.join(" and ")} left`
      : `Your Week ${week.number} card is set`;
  const now = desk.serverTime ?? new Date().toISOString();
  const left = timeLeft(week.deadline, now);
  // The next lock that matters: a saved pick first, otherwise anything still open.
  const unlocked = (rows: (DeskGame | DeskDog)[]) =>
    rows.filter((row) => !row.locked);
  const nextLock =
    [...unlocked(starts), ...unlocked(dogs)].sort(byLock)[0] ??
    [...unlocked(desk.games), ...unlocked(desk.dogs)].sort(byLock)[0];
  // One label per empty slot, which also keys it: "Pick your third start", or "Pick your dog" when there is only one.
  const emptySlots = (taken: number, limit: number, kind: string) =>
    Array.from({ length: Math.max(0, limit - taken) }, (_, offset) =>
      limit === 1
        ? `Pick your ${kind}`
        : `Pick your ${ordinals[taken + offset]} ${kind}`,
    );

  return (
    <div className="coach-home">
      <header className="week-hero">
        <p className="eyebrow">
          {`Week ${week.number}${desk.seasonName ? ` · ${desk.seasonName}` : ""}${week.isDoubleDogWeek ? " · Double-dog week" : ""}`}
        </p>
        <h1>{title}</h1>
        <p className="hero-line">
          Everything locks {when(week.deadline)} ET; earlier games lock at
          kickoff.
        </p>
        <div className="hero-clocks">
          <div className="clock" role="group" aria-label="Weekly deadline">
            <span>Weekly deadline in</span>
            <strong>{left === "none" ? "Locked" : left}</strong>
          </div>
          {nextLock && (
            <div className="clock next" role="group" aria-label="Next lock">
              <span>{`Next lock · ${nextLock.team}`}</span>
              <strong>{when(nextLock.lockTime)}</strong>
            </div>
          )}
        </div>
      </header>

      <section className="week-card" aria-labelledby="card-title">
        <h2 id="card-title">{`Your Week ${week.number} card`}</h2>
        <p className="slot-label">
          {`Starts · ${starts.length} of ${week.pickLimit}`}
        </p>
        <ul className="slots" aria-label="Starts">
          {starts.map((game) => (
            <li key={game.teamId} className="slot">
              <span
                className={`tier tier-${game.pointValue}`}
                role="img"
                aria-label={points(game.pointValue)}
              >
                {game.pointValue}
              </span>
              <span className="slot-text">
                <strong>{game.team}</strong>
                <span>{`${game.isHome ? "vs" : "at"} ${game.opponent} · ${when(game.kickoff)} · ${points(game.pointValue)}`}</span>
              </span>
              <span className="slot-state">
                {game.locked ? (
                  <>
                    <Lock aria-hidden="true" size={14} /> Locked
                  </>
                ) : (
                  "Open"
                )}
              </span>
            </li>
          ))}
          {emptySlots(starts.length, week.pickLimit, "start").map((label) => (
            <li key={label} className="slot">
              <AppLink to="/picks" className="slot-empty">
                <Plus aria-hidden="true" size={18} />
                {label}
              </AppLink>
            </li>
          ))}
        </ul>
        <p className="slot-label">{`Dogs · ${dogs.length} of ${week.dogLimit}`}</p>
        <ul className="slots" aria-label="Dogs">
          {dogs.map((pick) => (
            <li key={pick.teamId} className="slot">
              <span className="spread">{`+${pick.spread}`}</span>
              <span className="slot-text">
                <strong>{pick.team}</strong>
                <span>{`${pick.isHome ? "vs" : "at"} ${pick.opponent} · ${when(pick.kickoff)} · pays ${Math.ceil(pick.spread)}`}</span>
              </span>
              <span className="slot-state">
                {pick.locked ? (
                  <>
                    <Lock aria-hidden="true" size={14} /> Locked
                  </>
                ) : (
                  "Open"
                )}
              </span>
            </li>
          ))}
          {emptySlots(dogs.length, week.dogLimit, "dog").map((label) => (
            <li key={label} className="slot">
              <AppLink to="/picks" className="slot-empty">
                <Plus aria-hidden="true" size={18} />
                {label}
              </AppLink>
            </li>
          ))}
        </ul>
        <AppLink to="/picks" className="button-link primary-link">
          {owed.length > 0
            ? `Finish Week ${week.number} picks`
            : `Review Week ${week.number} picks`}
        </AppLink>
      </section>
      <WhereYouStand />
    </div>
  );
}
