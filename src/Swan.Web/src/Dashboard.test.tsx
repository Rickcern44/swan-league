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
  window.history.replaceState({}, "", "/commish");
});
afterEach(cleanup);

test("today states the week, marks its stage and puts the top decision in the next-action card", async () => {
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
  expect(
    screen.getByText("2026 Swan League · Regular season · Double dogs"),
  ).toBeVisible();
  expect(screen.getByText("Lines are being prepared")).toBeVisible();
  const stages = within(screen.getByRole("list", { name: "Week 4 stages" }));
  expect(stages.getAllByRole("listitem")).toHaveLength(5);
  expect(stages.getByRole("listitem", { current: "step" })).toHaveTextContent(
    "Draft",
  );
  expect(screen.getByText("13 of 16 lines approved")).toBeVisible();

  const next = within(screen.getByRole("region", { name: "Next action" }));
  expect(next.getByRole("heading", { level: 2 })).toHaveTextContent(
    "Results to confirm",
  );
  expect(
    next.getByText("2 of 16 included games are missing results"),
  ).toBeVisible();
  expect(next.getByRole("link", { name: "Open Week 3" })).toHaveAttribute(
    "href",
    "/commish/weeks/w3",
  );

  const queue = screen.getByRole("region", { name: "Needs your decision" });
  const items = within(queue).getAllByRole("listitem");
  expect(items).toHaveLength(1);
  expect(items[0]).toHaveTextContent("Lines need approval");
  expect(items[0]).toHaveTextContent("Week 4 · Draft");
});

test("an open week with coaches still owing picks makes that the next action and lists who owes what", async () => {
  dashboard = {
    status: 200,
    body: {
      season,
      currentWeek: week({
        state: "Open",
        linesApproved: 16,
        submissions: {
          complete: 24,
          of: 30,
          incomplete: [
            {
              membershipId: "m4",
              name: "Coach 4",
              starts: 0,
              startLimit: 3,
              dogs: 0,
              dogLimit: 2,
            },
            {
              membershipId: "m2",
              name: "Coach 2",
              starts: 2,
              startLimit: 3,
              dogs: 1,
              dogLimit: 2,
            },
            ...[5, 6, 7, 8].map((n) => ({
              membershipId: `m${n}`,
              name: `Coach ${n}`,
              starts: 3,
              startLimit: 3,
              dogs: 1,
              dogLimit: 2,
            })),
          ],
        },
      }),
      attention: [],
      caughtUp: false,
    },
  };
  stubApi("Admin");
  render(<App />);

  const next = within(
    await screen.findByRole("region", { name: "Next action" }),
  );
  expect(next.getByRole("heading", { level: 2 })).toHaveTextContent(
    "6 coaches still owe picks",
  );
  expect(
    screen.getByText("Open for picks", { selector: ".today-state" }),
  ).toBeVisible();

  const subs = within(
    screen.getByRole("region", { name: "Week 4 submissions" }),
  );
  expect(subs.getByText("24 of 30 complete")).toBeVisible();
  expect(subs.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "24");
  const owing = subs.getAllByRole("listitem");
  expect(owing).toHaveLength(5);
  expect(owing[0]).toHaveTextContent("Coach 4");
  expect(owing[0]).toHaveTextContent("0 of 3 starts · 0 of 2 dogs");
  expect(owing[1]).toHaveTextContent("2 of 3 starts · 1 of 2 dogs");
  expect(subs.getByText("+ 1 more")).toBeVisible();
  expect(subs.getByRole("link", { name: "See all picks" })).toHaveAttribute(
    "href",
    "/commish/weeks/w4?tab=picks",
  );
});

test("money and job cards appear only when the server reports them", async () => {
  dashboard = {
    status: 200,
    body: {
      season,
      currentWeek: week(),
      attention: [lines],
      caughtUp: false,
      money: null,
      jobs: null,
    },
  };
  stubApi("Admin");
  const { unmount } = render(<App />);
  await screen.findByRole("region", { name: "Next action" });
  expect(screen.queryByRole("region", { name: "Money" })).toBeNull();
  expect(screen.queryByRole("region", { name: "Jobs" })).toBeNull();
  unmount();

  dashboard = {
    status: 200,
    body: {
      season,
      currentWeek: week(),
      attention: [lines],
      caughtUp: false,
      money: {
        buyInsPaid: 27,
        buyInsDue: 30,
        finePoolCents: 1000,
        pendingFines: 3,
        pendingFinesCents: 1500,
      },
      jobs: {
        healthy: false,
        lastScoreSync: "2026-10-22T13:00:00Z",
        nextReminder: null,
        failures: 2,
      },
    },
  };
  render(<App />);
  const money = within(await screen.findByRole("region", { name: "Money" }));
  expect(money.getByText("27 of 30 buy-ins paid")).toBeVisible();
  expect(
    money.getByText("$10.00 fine pool · $15.00 pending (3)"),
  ).toBeVisible();
  const jobs = within(screen.getByRole("region", { name: "Jobs" }));
  expect(jobs.getByText("2 failures")).toBeVisible();
  expect(jobs.getByText(/Score sync Thu, Oct 22, 9:00 AM ET/)).toBeVisible();
});

test("the Today link carries a count of decisions waiting", async () => {
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
  await screen.findByRole("region", { name: "Next action" });

  const nav = screen.getByRole("navigation", { name: "Primary" });
  expect(
    await within(nav).findByRole("link", { name: "Today, 2 need a decision" }),
  ).toHaveAttribute("aria-current", "page");
});

test("quick actions lead to the week, the draft and season setup", async () => {
  dashboard = {
    status: 200,
    body: { season, currentWeek: week(), attention: [lines], caughtUp: false },
  };
  stubApi("Admin");
  render(<App />);

  const quick = within(
    await screen.findByRole("region", { name: "Quick actions" }),
  );
  expect(
    quick
      .getAllByRole("link")
      .map((link) => [link.textContent, link.getAttribute("href")]),
  ).toEqual([
    ["Open Week 4", "/commish/weeks/w4"],
    ["Draft status", "/commish/draft"],
    ["Season setup", "/commish/setup"],
  ]);
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
  await screen.findByRole("link", { name: "Today, 1 need a decision" });

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
    .map(
      (element) =>
        element.getAttribute("aria-label") ??
        element.textContent?.replace(/\s+/g, " ").trim(),
    );
  expect(names.slice(0, 7)).toEqual([
    "Skip to main content",
    "Coach",
    "Commish",
    "Sign out",
    "Today, 1 need a decision",
    "Draft",
    "Season setup",
  ]);
  // The next action, then the quick actions, then refresh.
  expect(names.slice(7)).toEqual([
    "Open Week 4",
    "Open Week 4",
    "Draft status",
    "Season setup",
    "Refresh",
  ]);
});

test("only destinations that exist are listed, with Today marked current", async () => {
  stubApi("Admin");
  render(<App />);
  await screen.findByRole("heading", { level: 1 });

  const nav = screen.getByRole("navigation", { name: "Primary" });
  expect(
    within(nav)
      .getAllByRole("link")
      .map((link) => link.textContent),
  ).toEqual(["Today", "Draft", "Season setup"]);
  expect(nav).not.toHaveTextContent(/coming soon/i);
  expect(within(nav).getByRole("link", { name: "Today" })).toHaveAttribute(
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
  expect(window.location.pathname).toBe("/commish/setup");
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
  expect(screen.getByText(/^2026 season · Regular season/)).toBeVisible();
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

test("a coach gets their own pick desk and the app never calls an admin endpoint", async () => {
  stubApi("Coach");
  render(<App />);

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Could not load your week.",
  );
  expect(
    screen.getByRole("heading", { level: 1, name: "Your week" }),
  ).toBeVisible();
  expect(
    screen.queryByRole("link", { name: "Season setup" }),
  ).not.toBeInTheDocument();
  expect(calls).toContain("/api/coach/picks/current");
  expect(calls.some((url) => url.includes("/api/admin"))).toBe(false);
});

test("Season setup is reached from the navigation and Today returns to the dashboard", async () => {
  stubApi("Admin");
  render(<App />);
  await screen.findByRole("heading", { level: 1 });

  fireEvent.click(screen.getByRole("link", { name: "Season setup" }));
  expect(window.location.pathname).toBe("/commish/setup");
  expect(
    await screen.findByRole("heading", { name: "Season setup" }),
  ).toBeVisible();
  fireEvent.click(screen.getByRole("link", { name: "Today" }));
  expect(window.location.pathname).toBe("/commish");
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

test("a season with neither a name nor a year is called this season", async () => {
  dashboard = {
    status: 200,
    body: {
      season: { ...season, name: " ", year: 0 },
      currentWeek: week({ number: 1, state: "Final" }),
      attention: [],
      caughtUp: true,
    },
  };
  stubApi("Admin");
  render(<App />);

  expect(await screen.findByText(/^This season · /)).toBeVisible();
  expect(
    screen.getByRole("region", { name: "Needs your decision" }),
  ).toHaveTextContent(/Every week of this season is final/);
  expect(screen.queryByText(/^0 season/)).not.toBeInTheDocument();
});

test("a decision row and the next action both open that exact week's workspace", async () => {
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

  const rows = await within(
    await screen.findByRole("region", { name: "Needs your decision" }),
  ).findAllByRole("link");
  expect(rows.map((row) => row.getAttribute("href"))).toEqual([
    "/commish/weeks/w4",
  ]);
  expect(
    within(screen.getByRole("region", { name: "Next action" })).getByRole(
      "link",
      { name: "Open Week 3" },
    ),
  ).toHaveAttribute("href", "/commish/weeks/w3");
});
