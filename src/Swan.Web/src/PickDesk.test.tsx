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

const WEEK = "11111111-1111-1111-1111-111111111111";
const team = (n: number) =>
  `00000000-0000-0000-0000-0000000000${String(n).padStart(2, "0")}`;
const hours = (h: number) =>
  new Date(Date.UTC(2026, 9, 18, 17, 0, 0) + h * 3_600_000).toISOString();

type Row = {
  teamId: string;
  team: string;
  gameId: string;
  opponent: string;
  isHome: boolean;
  kickoff: string;
  lockTime: string;
  locked: boolean;
  pointValue: number;
  selected: boolean;
};
type Dog = {
  teamId: string;
  team: string;
  gameId: string;
  opponent: string;
  isHome: boolean;
  kickoff: string;
  lockTime: string;
  locked: boolean;
  spread: number;
  selected: boolean;
};

const row = (n: number, over: Partial<Row> = {}): Row => ({
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
const dog = (n: number, over: Partial<Dog> = {}): Dog => ({
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

function desk(over: Record<string, unknown> = {}) {
  return {
    state: "open",
    seasonName: "2026 Swan League",
    week: {
      id: WEEK,
      number: 4,
      deadline: hours(30),
      isDoubleDogWeek: false,
      pickLimit: 3,
      dogLimit: 1,
    },
    revision: 0,
    serverTime: hours(0),
    games: [row(1), row(2), row(3), row(4)],
    notPlaying: ["Bye Team"],
    dogs: [dog(1), dog(2)],
    ...over,
  };
}

type SavedBody = { teamIds: string[]; dogTeamIds: string[] };
type Reply = { status: number; body?: unknown } | "network-error";
let current: ReturnType<typeof desk>;
let replies: Record<string, Reply | ((body?: SavedBody) => Reply)>;
let calls: { method: string; url: string; body?: SavedBody }[];

function stubApi(role: "Admin" | "Coach" = "Coach") {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? "GET";
      const body = init?.body ? JSON.parse(init.body as string) : undefined;
      calls.push({ method, url, body });
      let reply: Reply;
      if (url === "/api/me")
        reply = { status: 200, body: { id: "u", name: "Coach One", role } };
      else {
        const configured = replies[`${method} ${url}`];
        reply =
          typeof configured === "function"
            ? configured(body)
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
}

function open(d = desk()) {
  current = d;
  replies["GET /api/coach/picks/current"] = () => ({
    status: 200,
    body: current,
  });
  stubApi();
  window.history.replaceState({}, "", "/");
  return render(<App />);
}

// The fake server saves what it is sent and answers like the real one.
function acceptSaves() {
  replies["PUT /api/coach/picks/current"] = (body) => {
    current = {
      ...current,
      revision: current.revision + 1,
      games: current.games.map((g: Row) => ({
        ...g,
        selected: body!.teamIds.includes(g.teamId),
      })),
      dogs: current.dogs.map((d: Dog) => ({
        ...d,
        selected: body!.dogTeamIds.includes(d.teamId),
      })),
    };
    return { status: 200, body: current };
  };
}

beforeEach(() => {
  replies = {};
  calls = [];
});
afterEach(cleanup);

const start = (n: number) =>
  screen.getByRole("button", {
    name: new RegExp(`^(Start|Starting) Team ${n},`),
  });
const dock = () => screen.getByRole("region", { name: "Save picks" });
const ready = () =>
  screen.findByRole("heading", { level: 1, name: "Who are you starting?" });

test("an open week asks the question, lists the coach's playing teams in kickoff order with points and lock times, and notes who is not playing", async () => {
  open();

  expect(await ready()).toBeVisible();
  expect(screen.getByText(/Week 4 · 2026 Swan League/)).toBeVisible();
  expect(screen.getByText("0 of 3 chosen · 3 to go")).toBeVisible();
  const rows = within(
    screen.getByRole("region", { name: "Your teams this week" }),
  ).getAllByRole("listitem");
  expect(rows.map((r) => r.querySelector(".teams")?.textContent)).toEqual([
    "Team 1 at Rival 1",
    "Team 2 vs Rival 2",
    "Team 3 at Rival 3",
    "Team 4 vs Rival 4",
  ]);
  expect(rows[0]).toHaveTextContent("2 points if they win");
  expect(rows[0]).toHaveTextContent("Locks at kickoff, Sun");
  expect(screen.getByText("Not playing this week: Bye Team.")).toBeVisible();
  expect(screen.getByText(/The weekly deadline is .* ET/)).toBeVisible();
  expect(within(dock()).getByText("Nothing saved yet")).toBeVisible();
  expect(
    within(dock()).getByRole("button", { name: "Choose your picks" }),
  ).toBeDisabled();
});

test("a team that kicks off after the weekly deadline says it locks at the deadline", async () => {
  open(desk({ games: [row(1, { lockTime: hours(0.5), kickoff: hours(3) })] }));
  await ready();

  expect(screen.getByText(/Locks at the weekly deadline, Sun/)).toBeVisible();
});

test("up to three teams can be started, a fourth is blocked until one is removed", async () => {
  open();
  await ready();

  for (const n of [1, 2, 3]) fireEvent.click(start(n));

  expect(screen.getByText("3 of 3 chosen · all set")).toBeVisible();
  expect(start(1)).toHaveAttribute("aria-pressed", "true");
  expect(start(4)).toBeDisabled();
  fireEvent.click(start(2));
  expect(start(4)).toBeEnabled();
  expect(within(dock()).getByText("Unsaved changes")).toBeVisible();
});

test("saving sends the chosen teams, dog and the revision it read, then shows the saved ballot", async () => {
  open();
  acceptSaves();
  await ready();

  fireEvent.click(start(1));
  fireEvent.click(start(3));
  fireEvent.click(screen.getByRole("button", { name: /^Pick dog Dog 2,/ }));
  fireEvent.click(
    within(dock()).getByRole("button", { name: "Save 2 picks and 1 dog" }),
  );

  expect(await screen.findByText("Picks saved.")).toBeVisible();
  expect(calls.find((c) => c.method === "PUT")).toMatchObject({
    url: "/api/coach/picks/current",
    body: {
      weekId: WEEK,
      expectedRevision: 0,
      teamIds: [team(1), team(3)],
      dogTeamIds: [team(22)],
    },
  });
  expect(within(dock()).getByRole("button", { name: "Saved" })).toBeDisabled();
});

test("saved picks are shown as chosen on a fresh load and can be revised", async () => {
  open(
    desk({
      revision: 2,
      games: [row(1, { selected: true }), row(2), row(3), row(4)],
      dogs: [dog(1, { selected: true }), dog(2)],
    }),
  );
  acceptSaves();
  await ready();

  expect(start(1)).toHaveAttribute("aria-pressed", "true");
  expect(
    screen.getByRole("button", { name: /^Dog picked Dog 1,/ }),
  ).toBeVisible();
  expect(within(dock()).getByRole("button", { name: "Saved" })).toBeDisabled();

  fireEvent.click(start(1));
  fireEvent.click(start(4));
  fireEvent.click(
    within(dock()).getByRole("button", { name: "Save 1 pick and 1 dog" }),
  );

  await screen.findByText("Picks saved.");
  expect(calls.find((c) => c.method === "PUT")?.body).toMatchObject({
    expectedRevision: 2,
    teamIds: [team(4)],
  });
});

test("a locked game is shown as settled: a locked-in pick has no button and an unpicked locked game cannot be started", async () => {
  open(
    desk({
      games: [
        row(1, { locked: true, selected: true }),
        row(2, { locked: true }),
        row(3),
      ],
    }),
  );
  await ready();

  const rows = within(
    screen.getByRole("region", { name: "Your teams this week" }),
  ).getAllByRole("listitem");
  expect(rows[0]).toHaveTextContent("Locked · locked in");
  expect(rows[1]).toHaveTextContent("Locked");
  expect(within(rows[0]).queryByRole("button")).not.toBeInTheDocument();
  expect(within(rows[1]).queryByRole("button")).not.toBeInTheDocument();
  expect(screen.getByText("1 of 3 chosen · 2 to go")).toBeVisible();
});

test("one dog is allowed in an ordinary week and two in a double-dog week", async () => {
  const { unmount } = open();
  await ready();
  expect(screen.getByText("Choose 1 dog · 0 chosen")).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: /^Pick dog Dog 1,/ }));
  expect(
    screen.getByRole("button", { name: /^Pick dog Dog 2,/ }),
  ).toBeDisabled();
  unmount();

  open(
    desk({
      week: {
        id: WEEK,
        number: 2,
        deadline: hours(30),
        isDoubleDogWeek: true,
        pickLimit: 3,
        dogLimit: 2,
      },
    }),
  );
  await ready();
  expect(screen.getByText("Choose 2 dogs this week · 0 chosen")).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: /^Pick dog Dog 1,/ }));
  expect(
    screen.getByRole("button", { name: /^Pick dog Dog 2,/ }),
  ).toBeEnabled();
});

test("a dog row shows the spread and what the dog scores", async () => {
  open(desk({ dogs: [dog(6, { spread: 6.5 })] }));
  await ready();

  const row6 = within(
    screen.getByRole("region", { name: "Dog pick" }),
  ).getByRole("listitem");
  expect(row6).toHaveTextContent("+6.5 Dog 6 at Fav 6");
  expect(row6).toHaveTextContent("Scores 7 if they win outright");
});

test("the server's refusal is shown beside the dock and the coach's choices stay", async () => {
  open();
  replies["PUT /api/coach/picks/current"] = {
    status: 409,
    body: {
      code: "pick.locked",
      message:
        "A pick whose game has locked cannot be added, changed or removed.",
    },
  };
  await ready();

  fireEvent.click(start(1));
  fireEvent.click(within(dock()).getByRole("button", { name: "Save 1 pick" }));

  expect(await within(dock()).findByRole("alert")).toHaveTextContent(
    "has locked",
  );
  expect(start(1)).toHaveAttribute("aria-pressed", "true");
  expect(within(dock()).getByText("Unsaved changes")).toBeVisible();
  expect(screen.getByRole("status")).toBeEmptyDOMElement();
});

test("a stale ballot offers a reload that replaces the local edits with what is saved", async () => {
  open();
  replies["PUT /api/coach/picks/current"] = {
    status: 409,
    body: {
      code: "ballot.stale",
      message:
        "Your picks changed since this page loaded. Reload to see the saved ballot.",
    },
  };
  await ready();
  fireEvent.click(start(1));
  fireEvent.click(within(dock()).getByRole("button", { name: "Save 1 pick" }));
  expect(await within(dock()).findByRole("alert")).toHaveTextContent(
    "changed since this page loaded",
  );

  current = desk({
    revision: 5,
    games: [row(1), row(2, { selected: true }), row(3), row(4)],
  });
  fireEvent.click(
    within(dock()).getByRole("button", { name: "Reload saved picks" }),
  );

  await waitFor(() => expect(start(2)).toHaveAttribute("aria-pressed", "true"));
  expect(start(1)).toHaveAttribute("aria-pressed", "false");
  expect(within(dock()).queryByRole("alert")).not.toBeInTheDocument();
});

test("a network failure on save keeps the choices and says they are not saved", async () => {
  open();
  replies["PUT /api/coach/picks/current"] = "network-error";
  await ready();

  fireEvent.click(start(1));
  fireEvent.click(within(dock()).getByRole("button", { name: "Save 1 pick" }));

  expect(await within(dock()).findByRole("alert")).toHaveTextContent(
    "not saved yet",
  );
  expect(start(1)).toHaveAttribute("aria-pressed", "true");
});

test.each([
  ["no-week", "No week is open for picks right now"],
  ["no-roster", "Your roster has not been set up yet"],
  ["no-membership", "You are not on a coach roster for a live season"],
])("%s is said plainly, with no controls to save", async (state, text) => {
  open(desk({ state, week: null, games: [], dogs: [], notPlaying: [] }));

  expect(await screen.findByText(new RegExp(text))).toBeVisible();
  expect(
    screen.getByRole("heading", { level: 1, name: "Your picks" }),
  ).toBeVisible();
  expect(
    screen.queryByRole("region", { name: "Save picks" }),
  ).not.toBeInTheDocument();
  expect(calls.some((c) => c.method === "PUT")).toBe(false);
});

test("a failed load can be retried", async () => {
  let attempts = 0;
  replies["GET /api/coach/picks/current"] = () =>
    ++attempts === 1 ? { status: 500 } : { status: 200, body: desk() };
  stubApi();
  window.history.replaceState({}, "", "/");
  render(<App />);

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "The server answered 500.",
  );
  fireEvent.click(screen.getByRole("button", { name: "Try again" }));
  expect(await ready()).toBeVisible();
});

test("a coach never calls an admin endpoint and an admin never calls the coach desk", async () => {
  open();
  await ready();
  expect(calls.some((c) => c.url.includes("/api/admin"))).toBe(false);
  cleanup();

  calls = [];
  replies["GET /api/admin/dashboard"] = {
    status: 200,
    body: { season: null, currentWeek: null, attention: [], caughtUp: false },
  };
  stubApi("Admin");
  window.history.replaceState({}, "", "/");
  render(<App />);
  await screen.findByRole("heading", { name: "No season is live" });
  expect(calls.some((c) => c.url.includes("/api/coach"))).toBe(false);
});

test("every control has a name that includes the team and the game, and the page has one h1", async () => {
  open();
  await ready();

  expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
  expect(
    screen.getByRole("button", { name: "Start Team 1, Team 1 at Rival 1" }),
  ).toBeVisible();
  expect(
    screen.getByRole("button", { name: "Start Team 2, Rival 2 at Team 2" }),
  ).toBeVisible();
  expect(
    screen.getByRole("button", { name: "Pick dog Dog 1, Dog 1 at Fav 1" }),
  ).toBeVisible();
});
