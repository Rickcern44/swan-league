import {
  cleanup,
  fireEvent,
  render,
  screen,
  within,
} from "@testing-library/react";
import { afterEach, beforeEach, expect, test, vi } from "vitest";
import App from "./App";

type Reply = { status: number; body?: unknown } | "network-error";
let dashboard: Reply;
let calls: string[];

function stubApi(role: "Admin" | "Coach") {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string) => {
      calls.push(url);
      if (url === "/api/me")
        return {
          ok: true,
          status: 200,
          json: async () => ({ id: "u", name: "Swan Admin", role }),
        };
      if (url === "/api/admin/dashboard") {
        const reply = dashboard;
        if (reply === "network-error") throw new Error("offline");
        return {
          ok: reply.status < 400,
          status: reply.status,
          json: async () => reply.body,
        };
      }
      return { ok: false, status: 404, json: async () => ({}) };
    }),
  );
}

const week = (over: object = {}) => ({
  id: "w4",
  number: 4,
  kind: "Regular",
  state: "Draft",
  games: 16,
  includedGames: 16,
  linesApproved: 13,
  resultsConfirmed: 0,
  isDoubleDogWeek: true,
  ...over,
});
const season = {
  id: "s",
  name: "2026 Swan League",
  year: 2026,
  isSimulation: false,
};
const lines = {
  kind: "lines-missing",
  title: "Lines need approval",
  reason: "3 of 16 included games need a line",
  weekId: "w4",
  weekNumber: 4,
  weekState: "Draft",
  count: 3,
  of: 16,
  needsDecision: true,
};
const results = {
  kind: "results-missing",
  title: "Results to confirm",
  reason: "2 of 16 included games are missing results",
  weekId: "w3",
  weekNumber: 3,
  weekState: "Locked",
  count: 2,
  of: 16,
  needsDecision: true,
};

beforeEach(() => {
  calls = [];
  dashboard = {
    status: 200,
    body: { season: null, currentWeek: null, attention: [], caughtUp: false },
  };
  window.history.replaceState({}, "", "/");
});
afterEach(cleanup);

test("the admin lands on the decision queue in ranked order with the current week beside it", async () => {
  dashboard = {
    status: 200,
    body: {
      season,
      currentWeek: week(),
      attention: [results, lines],
      caughtUp: false,
    },
  };
  stubApi("Admin");
  render(<App />);

  expect(
    await screen.findByRole("heading", { level: 1, name: "Week 4" }),
  ).toBeVisible();
  const queue = screen.getByRole("region", { name: "Needs your decision" });
  const items = within(queue).getAllByRole("listitem");
  expect(items).toHaveLength(2);
  expect(items[0]).toHaveTextContent("Results to confirm");
  expect(items[0]).toHaveTextContent(
    "2 of 16 included games are missing results",
  );
  expect(items[0]).toHaveTextContent("Week 3 · Locked");
  expect(items[1]).toHaveTextContent("Lines need approval");
  expect(items[1]).toHaveTextContent("Week 4 · Draft");
  const current = screen.getByRole("region", { name: "This week" });
  expect(current).toHaveTextContent("Week 4 · Regular season · Draft");
  expect(current).toHaveTextContent("13 / 16");
  expect(current).toHaveTextContent("Week 4 is a double-dog week");
});

test("the main landmarks, a skip link and a labelled navigation are present with sign-out in the header", async () => {
  stubApi("Admin");
  render(<App />);

  await screen.findByRole("heading", { level: 1 });
  expect(screen.getByRole("main")).toBeVisible();
  expect(screen.getByRole("navigation", { name: "Primary" })).toBeVisible();
  expect(screen.getByRole("banner")).toHaveTextContent(
    "Signed in as Swan Admin · Admin",
  );
  expect(
    screen.getByRole("link", { name: "Skip to main content" }),
  ).toHaveAttribute("href", "#content");
});

test("keyboard order follows the page: skip link, header, navigation, then the content", async () => {
  dashboard = {
    status: 200,
    body: { season, currentWeek: week(), attention: [lines], caughtUp: false },
  };
  stubApi("Admin");
  const { container } = render(<App />);
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  // Tab order is DOM order when nothing sets a positive tabindex, so assert both.
  const focusable = Array.from(
    container.querySelectorAll<HTMLElement>(
      "a[href], button, input, select, textarea, [tabindex]",
    ),
  );
  expect(
    focusable.every(
      (element) => Number(element.getAttribute("tabindex") ?? 0) <= 0,
    ),
  ).toBe(true);
  const names = focusable
    .filter((element) => element.tabIndex >= 0)
    .map((element) => element.textContent?.replace(/\s+/g, " ").trim());
  expect(names).toEqual([
    "Skip to main content",
    "Sign out",
    "Home",
    "Season setup",
    "Refresh",
  ]);
});

test("destinations that do not exist yet are labelled text, never links", async () => {
  stubApi("Admin");
  render(<App />);
  await screen.findByRole("heading", { level: 1 });

  const nav = screen.getByRole("navigation", { name: "Primary" });
  expect(
    within(nav).queryByRole("link", { name: /Week|Standings/ }),
  ).not.toBeInTheDocument();
  expect(nav).toHaveTextContent("WeekComing soon");
  expect(nav).toHaveTextContent("StandingsComing soon");
  expect(within(nav).getByRole("link", { name: "Home" })).toHaveAttribute(
    "aria-current",
    "page",
  );
});

test("with no live season it says so and points at Season setup", async () => {
  stubApi("Admin");
  render(<App />);

  expect(
    await screen.findByRole("heading", { name: "No season is live" }),
  ).toBeVisible();
  expect(
    screen.queryByRole("region", { name: "Needs your decision" }),
  ).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Go to Season setup" }));
  expect(window.location.pathname).toBe("/setup");
  expect(screen.getByRole("link", { name: "Season setup" })).toHaveAttribute(
    "aria-current",
    "page",
  );
});

test("a season whose weeks are all final says the league is caught up and invents no task", async () => {
  dashboard = {
    status: 200,
    body: {
      season,
      currentWeek: week({
        number: 18,
        state: "Final",
        linesApproved: 16,
        resultsConfirmed: 16,
        isDoubleDogWeek: false,
      }),
      attention: [],
      caughtUp: true,
    },
  };
  stubApi("Admin");
  render(<App />);

  const queue = await screen.findByRole("region", {
    name: "Needs your decision",
  });
  expect(queue).toHaveTextContent("You are caught up");
  expect(within(queue).queryAllByRole("listitem")).toHaveLength(0);
});

test("a season with no name is identified by its year", async () => {
  dashboard = {
    status: 200,
    body: {
      season: { ...season, name: "" },
      currentWeek: week({ number: 1, state: "Final" }),
      attention: [],
      caughtUp: true,
    },
  };
  stubApi("Admin");
  render(<App />);

  const queue = await screen.findByRole("region", {
    name: "Needs your decision",
  });
  expect(queue).toHaveTextContent("Every week of 2026 season is final");
  expect(screen.getByText("2026 season")).toBeVisible();
});

test("an informational item is kept apart from decisions", async () => {
  dashboard = {
    status: 200,
    body: {
      season,
      currentWeek: week({ state: "Open" }),
      attention: [
        {
          ...lines,
          kind: "picks-live",
          title: "Picks are live",
          reason: "Week 4 is open for picks.",
          weekState: "Open",
          needsDecision: false,
          count: null,
          of: null,
        },
      ],
      caughtUp: false,
    },
  };
  stubApi("Admin");
  render(<App />);

  const queue = await screen.findByRole("region", {
    name: "Needs your decision",
  });
  expect(queue).toHaveTextContent("Nothing needs a decision right now.");
  expect(
    within(queue).getByRole("heading", { name: "For your information" }),
  ).toBeVisible();
  expect(queue).toHaveTextContent("Picks are live");
});

test("a failed load shows an alert and Try again recovers", async () => {
  dashboard = { status: 500 };
  stubApi("Admin");
  render(<App />);

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "The server answered 500.",
  );
  dashboard = {
    status: 200,
    body: { season, currentWeek: week(), attention: [lines], caughtUp: false },
  };
  fireEvent.click(screen.getByRole("button", { name: "Try again" }));

  expect(await screen.findByRole("heading", { name: "Week 4" })).toBeVisible();
  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
});

test("an unreachable server is reported the same way", async () => {
  dashboard = "network-error";
  stubApi("Admin");
  render(<App />);

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Could not reach the server.",
  );
});

test("a coach gets a calm placeholder and the app never calls an admin endpoint", async () => {
  stubApi("Coach");
  render(<App />);

  expect(
    await screen.findByRole("heading", {
      level: 1,
      name: "Welcome, Swan Admin",
    }),
  ).toBeVisible();
  expect(screen.getByText(/nothing for you to do here for now/i)).toBeVisible();
  expect(
    screen.queryByRole("link", { name: "Season setup" }),
  ).not.toBeInTheDocument();
  expect(calls.some((url) => url.includes("/api/admin"))).toBe(false);
});

test("Season setup is reached from the navigation and Home returns to the dashboard", async () => {
  stubApi("Admin");
  render(<App />);
  await screen.findByRole("heading", { level: 1 });

  fireEvent.click(screen.getByRole("link", { name: "Season setup" }));
  expect(window.location.pathname).toBe("/setup");
  expect(
    await screen.findByRole("heading", { name: "Season setup" }),
  ).toBeVisible();
  fireEvent.click(screen.getByRole("link", { name: "Home" }));
  expect(window.location.pathname).toBe("/");
  expect(
    await screen.findByRole("heading", { name: "No season is live" }),
  ).toBeVisible();
});

test("the Season setup page has exactly one top-level heading", async () => {
  window.history.replaceState({}, "", "/setup");
  stubApi("Admin");
  render(<App />);

  expect(
    await screen.findByRole("heading", { level: 1, name: "Season setup" }),
  ).toBeVisible();
  expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
});
