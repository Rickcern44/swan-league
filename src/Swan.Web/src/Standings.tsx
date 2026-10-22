import { useCallback, useEffect, useState } from "react";
import {
  fetchStandings,
  type LoadedStandings,
  ordinal,
  type Pool,
  poolLabel,
  pools,
  pts,
  type StandingsData,
} from "./standingsData";

const TOP = 10;

function initialPool(): Pool {
  const asked = new URLSearchParams(window.location.search).get("pool");
  return pools.find((item) => item.id === asked)?.id ?? "Main";
}

export default function Standings() {
  const [pool, setPool] = useState<Pool>(initialPool);
  const [loaded, setLoaded] = useState<LoadedStandings>({ status: "loading" });
  const [showAll, setShowAll] = useState(false);

  const load = useCallback(async (which: Pool) => {
    setLoaded({ status: "loading" });
    setLoaded(await fetchStandings(which));
  }, []);

  useEffect(() => {
    void load(pool);
  }, [load, pool]);

  function choose(next: Pool) {
    if (next === pool) return;
    setPool(next);
    setShowAll(false);
    const url = new URL(window.location.href);
    url.searchParams.set("pool", next);
    window.history.replaceState(window.history.state, "", url);
  }

  const data = loaded.status === "ready" ? loaded.data : null;
  const eyebrow = [
    data?.seasonName,
    data?.throughWeek ? `Through Week ${data.throughWeek}` : null,
  ]
    .filter(Boolean)
    .join(" · ");

  return (
    <div className="standings">
      <header className="standings-head">
        {eyebrow && <p className="eyebrow">{eyebrow}</p>}
        <h1>Standings</h1>
        <div className="filter-chips" role="group" aria-label="Pool">
          {pools.map((item) => (
            <button
              key={item.id}
              type="button"
              aria-pressed={pool === item.id}
              className={pool === item.id ? "on" : ""}
              onClick={() => choose(item.id)}
            >
              {item.label}
            </button>
          ))}
        </div>
      </header>

      {loaded.status === "loading" && (
        <p role="status" aria-busy="true">
          Loading the standings…
        </p>
      )}
      {loaded.status === "failed" && (
        <div role="alert" className="notice problem">
          <strong>Could not load the standings.</strong> {loaded.error}{" "}
          <button type="button" onClick={() => void load(pool)}>
            Try again
          </button>
        </div>
      )}
      {data && <Board data={data} showAll={showAll} onToggle={setShowAll} />}
    </div>
  );
}

function Board({
  data,
  showAll,
  onToggle,
}: {
  data: StandingsData;
  showAll: boolean;
  onToggle: (all: boolean) => void;
}) {
  if (data.state === "no-membership")
    return (
      <div className="band">
        <h2>No standings for you yet</h2>
        <p>You're not a coach in a live season.</p>
      </div>
    );

  if (data.state === "no-scores" || data.rows.length === 0)
    return (
      <div className="band">
        <h2>Nothing scored yet</h2>
        <p>
          {`Standings start once Week 1 is final. ${data.coachCount} ${data.coachCount === 1 ? "coach is" : "coaches are"} in the league.`}
        </p>
      </div>
    );

  const { you, rows } = data;
  const top = rows.slice(0, TOP);
  // Your own row always shows, even below the top ten.
  const mine = rows.find((row) => row.isYou);
  const shown = showAll
    ? rows
    : mine && !top.includes(mine)
      ? [...top, mine]
      : top;
  const tiedLead = rows.filter((row) => row.rank === 1).length > 1;

  return (
    <>
      {you && (
        <section className="you-card" aria-label="Your standing">
          <span className="you-rank">{ordinal(you.rank)}</span>
          <span className="you-text">
            <strong>{you.name}</strong>
            <span>
              {`${pts(you.points)} · ${
                you.behindLeader > 0
                  ? `${you.behindLeader} behind 1st`
                  : tiedLead
                    ? "Tied for the lead"
                    : "Leading"
              }`}
            </span>
          </span>
        </section>
      )}

      <div className="standings-card">
        <table className="standings-table">
          <caption>{`${poolLabel(data.pool)} pool standings`}</caption>
          <thead>
            <tr>
              <th scope="col">#</th>
              <th scope="col">Coach</th>
              <th scope="col" className="num">
                <abbr title="Wins">W</abbr>
              </th>
              <th scope="col" className="num">
                Pts
              </th>
            </tr>
          </thead>
          <tbody>
            {shown.map((row, index) => (
              <tr
                key={row.membershipId}
                className={`${row.isYou ? "you" : ""}${
                  !showAll && index === TOP && row.isYou ? " gap" : ""
                }`}
              >
                <td>{row.rank}</td>
                <td>
                  {row.name}
                  {row.isYou && <span className="you-tag"> (you)</span>}
                </td>
                <td className="num">{row.wins}</td>
                <td className="num">{row.points}</td>
              </tr>
            ))}
          </tbody>
        </table>
        {rows.length > TOP && (
          <button
            type="button"
            className="show-all"
            aria-expanded={showAll}
            onClick={() => onToggle(!showAll)}
          >
            {showAll
              ? `Show the top ${TOP}`
              : `Show all ${rows.length} coaches`}
          </button>
        )}
      </div>
    </>
  );
}
