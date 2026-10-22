import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import { afterEach, beforeEach, expect, test, vi } from "vitest";
import App from "./App";

type Row = {
  membershipId: string;
  rank: number;
  name: string;
  points: number;
  wins: number;
  isYou: boolean;
  movement: number | null;
};

const row = (n: number, over: Partial<Row> = {}): Row => ({
  membershipId: `m${n}`,
  rank: n,
  name: `Coach ${n}`,
  points: 40 - n,
  wins: 15 - Math.floor(n / 2),
  isYou: false,
  movement: null,
  ...over,
});

function standings(
  pool = "Main",
  rows: Row[] = Array.from({ length: 12 }, (_, i) =>
    row(i + 1, i + 1 === 7 ? { isYou: true, name: "Coach Seven" } : {}),
  ),
  over: Record<string, unknown> = {},
) {
  const you = rows.find((r) => r.isYou);
  return {
    state: "ready",
    seasonName: "2026 Swan League",
    pool,
    throughWeek: 6,
    coachCount: rows.length,
    rows,
    you: you ? { ...you, behindLeader: rows[0].points - you.points } : null,
    ...over,
  };
}

type Reply = { status: number; body?: unknown } | "network-error";
let replies: Record<string, Reply | (() => Reply)>;
let calls: string[];

function open(path = "/standings") {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string) => {
      calls.push(url);
      let reply: Reply;
      if (url === "/api/me")
        reply = {
          status: 200,
          body: { id: "u", name: "Coach Seven", role: "Coach" },
        };
      else {
        const configured = replies[url];
        reply =
          typeof configured === "function"
            ? configured()
            : (configured ?? { status: 404 });
      }
      if (reply === "network-error") throw new Error("offline");
      return {
        ok: reply.status < 400,
        status: reply.status,
        json: async () => reply.body,
      };
    }),
  );
  window.history.replaceState({}, "", path);
  return render(<App />);
}

beforeEach(() => {
  calls = [];
  replies = {
    "/api/coach/standings?pool=Main": { status: 200, body: standings() },
    "/api/coach/standings?pool=Dog": {
      status: 200,
      body: standings("Dog", [
        row(1, { points: 9 }),
        row(2, { rank: 2, points: 6, isYou: true, name: "Coach Seven" }),
        row(3, { rank: 2, points: 6 }),
      ]),
    },
  };
});
afterEach(cleanup);

const nav = () => within(screen.getByRole("navigation", { name: "Primary" }));
const table = () => screen.getByRole("table", { name: /pool standings/ });
const bodyRows = () => within(table()).getAllByRole("row").slice(1);

test("Standings is in the coach navigation between Picks and Draft", async () => {
  open("/");
  await screen.findByRole("heading", { level: 1 });

  expect(
    nav()
      .getAllByRole("link")
      .map((link) => [link.textContent, link.getAttribute("href")]),
  ).toEqual([
    ["Home", "/"],
    ["Picks", "/picks"],
    ["Standings", "/standings"],
    ["Draft", "/roster"],
  ]);
});

test("the main pool shows where you stand and the top ten with your row marked", async () => {
  open();

  expect(
    await screen.findByRole("heading", { level: 1, name: "Standings" }),
  ).toBeVisible();
  expect(nav().getByRole("link", { name: "Standings" })).toHaveAttribute(
    "aria-current",
    "page",
  );
  await screen.findByRole("table", { name: "Main pool standings" });
  expect(screen.getByText("2026 Swan League · Through Week 6")).toBeVisible();
  expect(screen.getByRole("button", { name: "Main" })).toHaveAttribute(
    "aria-pressed",
    "true",
  );

  const you = within(screen.getByRole("region", { name: "Your standing" }));
  expect(you.getByText("7th")).toBeVisible();
  expect(you.getByText("Coach Seven")).toBeVisible();
  expect(you.getByText("33 pts · 6 behind 1st")).toBeVisible();

  expect(table()).toHaveAccessibleName("Main pool standings");
  expect(bodyRows()).toHaveLength(10);
  expect(bodyRows()[0]).toHaveTextContent("1Coach 11539");
  const mine = bodyRows()[6];
  expect(mine).toHaveClass("you");
  expect(mine).toHaveTextContent("Coach Seven (you)");
  expect(calls).toContain("/api/coach/standings?pool=Main");
});

test("show all lists every coach, and your row stays visible when you are outside the top ten", async () => {
  replies["/api/coach/standings?pool=Main"] = {
    status: 200,
    body: standings(
      "Main",
      Array.from({ length: 14 }, (_, i) =>
        row(i + 1, i + 1 === 13 ? { isYou: true, name: "Coach Seven" } : {}),
      ),
    ),
  };
  open();
  await screen.findByRole("table", { name: /pool standings/ });

  expect(bodyRows()).toHaveLength(11);
  expect(bodyRows()[10]).toHaveTextContent("13Coach Seven (you)");

  const all = screen.getByRole("button", { name: "Show all 14 coaches" });
  fireEvent.click(all);
  expect(bodyRows()).toHaveLength(14);
  expect(
    screen.getByRole("button", { name: "Show the top 10" }),
  ).toHaveAttribute("aria-expanded", "true");
});

test("switching pools loads that pool, keeps it in the address and shows shared ranks", async () => {
  open();
  await screen.findByRole("table", { name: "Main pool standings" });

  fireEvent.click(screen.getByRole("button", { name: "Dog" }));

  await screen.findByRole("table", { name: "Dog pool standings" });
  expect(window.location.search).toBe("?pool=Dog");
  expect(calls).toContain("/api/coach/standings?pool=Dog");
  expect(bodyRows().map((r) => r.firstChild?.textContent)).toEqual([
    "1",
    "2",
    "2",
  ]);
  expect(screen.getByText("6 pts · 3 behind 1st")).toBeVisible();
  expect(screen.getByRole("button", { name: "Dog" })).toHaveAttribute(
    "aria-pressed",
    "true",
  );
});

test("the pool in the address is opened directly", async () => {
  open("/standings?pool=Dog");

  expect(
    await screen.findByRole("table", { name: "Dog pool standings" }),
  ).toBeVisible();
  expect(calls).not.toContain("/api/coach/standings?pool=Main");
});

test("the leader is told they are leading", async () => {
  replies["/api/coach/standings?pool=Main"] = {
    status: 200,
    body: standings("Main", [
      row(1, { isYou: true, name: "Coach Seven", points: 12 }),
      row(2, { points: 10 }),
    ]),
  };
  open();

  expect(await screen.findByText("12 pts · Leading")).toBeVisible();
});

test("before any week is scored the page says when standings start", async () => {
  replies["/api/coach/standings?pool=Main"] = {
    status: 200,
    body: standings("Main", [], {
      state: "no-scores",
      throughWeek: null,
      coachCount: 30,
      you: null,
    }),
  };
  open();

  expect(
    await screen.findByText(
      "Standings start once Week 1 is final. 30 coaches are in the league.",
    ),
  ).toBeVisible();
  expect(screen.queryByRole("table")).toBeNull();
});

test("someone who is not a coach in a live season is told so", async () => {
  replies["/api/coach/standings?pool=Main"] = {
    status: 200,
    body: {
      state: "no-membership",
      seasonName: null,
      pool: "Main",
      throughWeek: null,
      coachCount: 0,
      rows: [],
      you: null,
    },
  };
  open();

  expect(
    await screen.findByText("You're not a coach in a live season."),
  ).toBeVisible();
});

test("a failed load can be retried", async () => {
  let attempts = 0;
  replies["/api/coach/standings?pool=Main"] = () =>
    ++attempts === 1 ? { status: 500 } : { status: 200, body: standings() };
  open();

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Could not load the standings.",
  );
  fireEvent.click(screen.getByRole("button", { name: "Try again" }));
  await waitFor(() => expect(bodyRows()).toHaveLength(10));
});
