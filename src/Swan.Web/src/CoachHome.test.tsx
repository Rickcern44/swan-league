import {
  cleanup,
  fireEvent,
  render,
  screen,
  within,
} from "@testing-library/react";
import { afterEach, beforeEach, expect, test, vi } from "vitest";
import App from "./App";

const WEEK = "11111111-1111-1111-1111-111111111111";
const team = (n: number) =>
  `00000000-0000-0000-0000-0000000000${String(n).padStart(2, "0")}`;
const hours = (h: number) =>
  new Date(Date.UTC(2026, 9, 18, 17, 0, 0) + h * 3_600_000).toISOString();

const row = (n: number, over: Record<string, unknown> = {}) => ({
  teamId: team(n),
  team: `Team ${n}`,
  gameId: `g${n}`,
  opponent: `Rival ${n}`,
  isHome: n % 2 === 0,
  kickoff: hours(n),
  lockTime: hours(n),
  locked: false,
  pointValue: (n % 4) + 1,
  selected: false,
  ...over,
});
const dog = (n: number, over: Record<string, unknown> = {}) => ({
  teamId: team(n + 20),
  team: `Dog ${n}`,
  gameId: `d${n}`,
  opponent: `Fav ${n}`,
  isHome: false,
  kickoff: hours(n),
  lockTime: hours(n),
  locked: false,
  spread: n + 0.5,
  selected: false,
  ...over,
});
const desk = (over: Record<string, unknown> = {}) => ({
  state: "open",
  seasonName: "2026 Swan League",
  week: {
    id: WEEK,
    number: 4,
    deadline: hours(30),
    isDoubleDogWeek: true,
    pickLimit: 3,
    dogLimit: 2,
  },
  revision: 1,
  serverTime: hours(0),
  games: [
    row(1, { selected: true, locked: true }),
    row(2, { selected: true }),
    row(3),
    row(4),
  ],
  notPlaying: [],
  dogs: [dog(1, { selected: true }), dog(2), dog(3)],
  ...over,
});

let calls: string[];
let reply: () => { status: number; body?: unknown };

function open(path = "/") {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string) => {
      calls.push(url);
      const answer =
        url === "/api/me"
          ? {
              status: 200,
              body: { id: "u", name: "Coach One", role: "Coach" },
            }
          : url === "/api/coach/picks/current"
            ? reply()
            : { status: 404 };
      return {
        ok: answer.status < 400,
        status: answer.status,
        json: async () => answer.body,
      };
    }),
  );
  window.history.replaceState({}, "", path);
  return render(<App />);
}

beforeEach(() => {
  calls = [];
  reply = () => ({ status: 200, body: desk() });
});
afterEach(cleanup);

const card = () =>
  within(screen.getByRole("region", { name: "Your Week 4 card" }));
const nav = () => within(screen.getByRole("navigation", { name: "Primary" }));

test("home says what is left this week, when it locks, and what locks next", async () => {
  open();

  expect(
    await screen.findByRole("heading", {
      level: 1,
      name: "1 start and 1 dog left",
    }),
  ).toBeVisible();
  expect(
    screen.getByText("Week 4 · 2026 Swan League · Double-dog week"),
  ).toBeVisible();
  const deadline = within(
    screen.getByRole("group", { name: "Weekly deadline" }),
  );
  expect(deadline.getByText("1d 6h")).toBeVisible();
  // Team 1 has already locked, so the next lock is the saved Dog 1, then Team 2.
  const next = within(screen.getByRole("group", { name: "Next lock" }));
  expect(next.getByText(/Dog 1/)).toBeVisible();
  expect(nav().getByRole("link", { name: "Home" })).toHaveAttribute(
    "aria-current",
    "page",
  );
});

test("the week card shows saved starts and dogs and links each empty slot to Picks", async () => {
  open();
  await screen.findByRole("region", { name: "Your Week 4 card" });

  const starts = within(card().getByRole("list", { name: "Starts" }));
  const startRows = starts.getAllByRole("listitem");
  expect(startRows).toHaveLength(3);
  expect(startRows[0]).toHaveTextContent("Team 1");
  expect(startRows[0]).toHaveTextContent("2 points");
  expect(startRows[0]).toHaveTextContent("Locked");
  expect(startRows[1]).toHaveTextContent("Team 2");
  expect(startRows[1]).toHaveTextContent("Open");
  expect(
    within(startRows[2]).getByRole("link", { name: "Pick your third start" }),
  ).toHaveAttribute("href", "/picks");

  const dogs = within(card().getByRole("list", { name: "Dogs" }));
  const dogRows = dogs.getAllByRole("listitem");
  expect(dogRows).toHaveLength(2);
  expect(dogRows[0]).toHaveTextContent("+1.5");
  expect(dogRows[0]).toHaveTextContent("Dog 1");
  expect(
    within(dogRows[1]).getByRole("link", { name: "Pick your second dog" }),
  ).toHaveAttribute("href", "/picks");
  expect(card().getByText("Starts · 2 of 3")).toBeVisible();
  expect(card().getByText("Dogs · 1 of 2")).toBeVisible();
});

test("the card's main action opens Picks without reloading the page", async () => {
  open();
  await screen.findByRole("region", { name: "Your Week 4 card" });

  fireEvent.click(card().getByRole("link", { name: "Finish Week 4 picks" }));

  expect(window.location.pathname).toBe("/picks");
  expect(
    await screen.findByRole("heading", { level: 1, name: "Week 4 picks" }),
  ).toBeVisible();
  expect(nav().getByRole("link", { name: "Picks" })).toHaveAttribute(
    "aria-current",
    "page",
  );
});

test("a full card says so and offers a review instead", async () => {
  reply = () => ({
    status: 200,
    body: desk({
      games: [
        row(1, { selected: true }),
        row(2, { selected: true }),
        row(3, { selected: true }),
      ],
      dogs: [dog(1, { selected: true }), dog(2, { selected: true })],
    }),
  });
  open();

  expect(
    await screen.findByRole("heading", {
      level: 1,
      name: "Your Week 4 card is set",
    }),
  ).toBeVisible();
  expect(
    card().getByRole("link", { name: "Review Week 4 picks" }),
  ).toHaveAttribute("href", "/picks");
  expect(card().queryByRole("link", { name: /^Pick your/ })).toBeNull();
});

test("an ordinary week has one dog slot and no double-dog note", async () => {
  reply = () => ({
    status: 200,
    body: desk({
      week: {
        id: WEEK,
        number: 4,
        deadline: hours(30),
        isDoubleDogWeek: false,
        pickLimit: 3,
        dogLimit: 1,
      },
      dogs: [dog(1)],
    }),
  });
  open();

  expect(
    await screen.findByRole("heading", {
      level: 1,
      name: "1 start and 1 dog left",
    }),
  ).toBeVisible();
  expect(screen.getByText("Week 4 · 2026 Swan League")).toBeVisible();
  const dogs = within(card().getByRole("list", { name: "Dogs" }));
  expect(dogs.getAllByRole("listitem")).toHaveLength(1);
  expect(dogs.getByRole("link", { name: "Pick your dog" })).toBeVisible();
});

test("with no open week home says why and points at the draft when the roster is the problem", async () => {
  reply = () => ({
    status: 200,
    body: desk({
      state: "roster-incomplete",
      week: null,
      games: [],
      dogs: [],
    }),
  });
  open();

  // Loading shows the same heading, so wait for the loaded message first.
  expect(await screen.findByText(/needs exactly nine teams/)).toBeVisible();
  expect(
    screen.getByRole("heading", { level: 1, name: "Your week" }),
  ).toBeVisible();
  expect(screen.getByRole("link", { name: "Open your draft" })).toHaveAttribute(
    "href",
    "/roster",
  );
});

test("a failed load says so and can be retried", async () => {
  let attempts = 0;
  reply = () =>
    ++attempts === 1 ? { status: 500 } : { status: 200, body: desk() };
  open();

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Could not load your week.",
  );
  fireEvent.click(screen.getByRole("button", { name: "Try again" }));
  expect(
    await screen.findByRole("heading", {
      level: 1,
      name: "1 start and 1 dog left",
    }),
  ).toBeVisible();
  expect(calls.some((url) => url.includes("/api/admin"))).toBe(false);
});
