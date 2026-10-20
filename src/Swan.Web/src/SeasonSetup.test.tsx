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

const money = (amount: number) => ({ amount, currency: "USD" });
const season = (overrides: Record<string, unknown> = {}) => ({
  id: { value: "s1" },
  year: 2026,
  name: "2026 Season",
  isSimulation: false,
  state: 0,
  settings: {
    buyIn: money(100),
    doubleDogWeeks: 3,
    teamPickDeadlineEastern: "13:00:00",
  },
  setupStep: 0,
  pools: [
    { name: "Main", stake: money(60) },
    { name: "Dog", stake: money(20) },
    { name: "Regular Season", stake: money(10) },
    { name: "Playoff", stake: money(10) },
  ],
  ...overrides,
});

const divisions = [
  "AFC East",
  "AFC North",
  "AFC South",
  "AFC West",
  "NFC East",
  "NFC North",
  "NFC South",
  "NFC West",
];
const catalog = (finishes = false) => ({
  teams: divisions.flatMap((division) =>
    [1, 2, 3, 4].map((slot) => ({
      teamId: `${division}-${slot}`,
      name: `${division} Team ${slot}`,
      division,
      finishPosition: finishes ? slot : null,
      pointValue: finishes ? slot : null,
    })),
  ),
});

const plan = (overrides: Record<string, unknown> = {}) => ({
  imported: false,
  fingerprint: "FP1",
  season: 2026,
  gameCount: 3,
  weeks: [
    {
      week: 1,
      games: [
        {
          providerGameId: "10",
          homeTeam: "Buffalo Bills",
          awayTeam: "New York Jets",
          homeTeamId: "t1",
          awayTeamId: "t2",
          kickoff: "2026-09-13T17:00:00+00:00",
        },
        {
          providerGameId: "11",
          homeTeam: "Dallas Cowboys",
          awayTeam: "New York Giants",
          homeTeamId: "t3",
          awayTeamId: "t4",
          kickoff: "2026-09-14T00:20:00+00:00",
        },
      ],
    },
    {
      week: 2,
      games: [
        {
          providerGameId: "20",
          homeTeam: "Miami Dolphins",
          awayTeam: "Buffalo Bills",
          homeTeamId: "t5",
          awayTeamId: "t1",
          kickoff: "2026-09-20T17:00:00+00:00",
        },
      ],
    },
  ],
  ...overrides,
});

const dogPlan = (selected: number[] = []) => ({
  selected,
  weeks: Array.from({ length: 18 }, (_, index) => {
    const week = index + 1;
    return {
      week,
      selected: selected.includes(week),
      required: week === 2,
      suggested: week === 12 || week === 17,
      reason:
        week === 2
          ? "Required by the rules"
          : week === 12
            ? "Suggested · contains Thanksgiving (Thu Nov 26)"
            : week === 17
              ? "Suggested · contains Christmas (Fri Dec 25)"
              : "",
    };
  }),
});

const poolStakes = {
  Main: 60,
  Dog: 20,
  "Regular Season": 10,
  Playoff: 10,
} as const;
const finesPlan = (
  coaches = 0,
  plans: Record<string, [number, number][]> | null = null,
  fine: number | null = 5,
) => ({
  configured: plans !== null,
  missedPickFine: fine,
  activeCoaches: coaches,
  pools: Object.entries(poolStakes).map(([pool, stake]) => ({
    pool,
    stake,
    pot: stake * coaches,
    places: (plans?.[pool] ?? []).map(([position, percent]) => ({
      position,
      percent,
      amount: 0,
    })),
  })),
});

const rulesPlan = (overrides: Record<string, unknown> = {}) => ({
  teamMayAlsoBeDog: true,
  pointColors: [
    { points: 1, color: "Green" },
    { points: 2, color: "Blue" },
    { points: 3, color: "Yellow" },
    { points: 4, color: "Red" },
  ],
  recordHolder: "FirstOccurrence",
  ...overrides,
});

const coachesPlan = (
  coaches: { name: string; email: string; active?: boolean }[] = [],
  candidates: { name: string; email: string; selected?: boolean }[] = [],
) => ({
  prior: candidates.length
    ? { id: "p1", year: 2025, name: "2025 Season" }
    : null,
  coaches: coaches.map((coach, index) => ({
    membershipId: `m${index}`,
    userId: `u-${coach.email}`,
    name: coach.name,
    email: coach.email,
    active: coach.active ?? true,
  })),
  candidates: candidates.map((candidate) => ({
    userId: `u-${candidate.email}`,
    name: candidate.name,
    email: candidate.email,
    selected: candidate.selected ?? false,
  })),
  activeCoaches: coaches.filter((coach) => coach.active ?? true).length,
});

const reviewData = (overrides: Record<string, unknown> = {}) => ({
  completed: false,
  ready: true,
  blockers: [],
  warnings: [],
  basics: { year: 2027, name: "2027 Swan League", isSimulation: true },
  money: {
    buyIn: 100,
    pools: [
      { name: "Main", stake: 60 },
      { name: "Dog", stake: 20 },
      { name: "Regular Season", stake: 10 },
      { name: "Playoff", stake: 10 },
    ],
  },
  payouts: finesPlan(
    6,
    {
      Main: [
        [1, 70],
        [2, 30],
      ],
      Dog: [[1, 100]],
      "Regular Season": [[1, 100]],
      Playoff: [[1, 100]],
    },
    5,
  ),
  teamPickDeadlineEastern: "11:30:00",
  teams: { entered: 32, total: 32, teams: catalog(true).teams },
  schedule: {
    weeks: 18,
    games: 272,
    complete: true,
    firstKickoff: "2027-09-10T00:20:00+00:00",
    lastKickoff: "2028-01-09T21:25:00+00:00",
  },
  doubleDogWeeks: [2, 12, 17],
  leagueSettings: rulesPlan({
    teamMayAlsoBeDog: false,
    recordHolder: "MostOccurrences",
  }),
  activeCoaches: 6,
  ...overrides,
});

type Reply = { status: number; body?: unknown };
let replies: Record<
  string,
  | Reply
  | ((body: { step: number; weeks?: number[]; coaches?: unknown[] }) => Reply)
>;
let calls: { method: string; url: string; body?: unknown }[];

function resolve(
  reply:
    | Reply
    | ((body: { step: number; weeks?: number[] }) => Reply)
    | undefined,
  body?: unknown,
): Reply {
  if (!reply) return { status: 404 };
  return typeof reply === "function"
    ? reply(body ? JSON.parse(body as string) : undefined)
    : reply;
}

function stubApi(role: "Admin" | "Coach", developmentTools = false) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? "GET";
      calls.push({
        method,
        url,
        body: init?.body ? JSON.parse(init.body as string) : undefined,
      });
      const reply =
        url === "/api/me"
          ? {
              status: 200,
              body: { id: "u", name: "Swan Admin", role, developmentTools },
            }
          : resolve(replies[`${method} ${url}`], init?.body);
      return {
        ok: reply.status < 400,
        status: reply.status,
        json: async () => reply.body,
      };
    }),
  );
}

afterEach(cleanup);

beforeEach(() => {
  replies = {
    [TEAMS]: { status: 200, body: catalog() },
    [DOGS]: { status: 200, body: dogPlan() },
    [FINES]: { status: 200, body: finesPlan() },
    [RULES]: { status: 200, body: rulesPlan() },
    [COACHES]: { status: 200, body: coachesPlan() },
    [REVIEW]: { status: 200, body: reviewData() },
    [SCHEDULE]: {
      status: 200,
      body: plan({ imported: false, fingerprint: null, weeks: [] }),
    },
  };
  calls = [];
  window.history.replaceState({}, "", "/setup");
});

const SETUP = "GET /api/admin/seasons/setup";
const TEAMS = "GET /api/admin/seasons/setup/teams";
const SCHEDULE = "GET /api/admin/seasons/setup/schedule";
const PREVIEW = "POST /api/admin/seasons/setup/schedule/preview";
const IMPORT = "POST /api/admin/seasons/setup/schedule/import";
const DOGS = "GET /api/admin/seasons/setup/double-dog-weeks";
const SAVE_DOGS = "PUT /api/admin/seasons/setup/double-dog-weeks";
const FINES = "GET /api/admin/seasons/setup/fines-and-payouts";
const SAVE_FINES = "PUT /api/admin/seasons/setup/fines-and-payouts";
const RULES = "GET /api/admin/seasons/setup/league-settings";
const SAVE_RULES = "PUT /api/admin/seasons/setup/league-settings";
const COACHES = "GET /api/admin/seasons/setup/coaches";
const SAVE_COACHES = "PUT /api/admin/seasons/setup/coaches";
const REVIEW = "GET /api/admin/seasons/setup/review";
const FINISH = "POST /api/admin/seasons/setup/review/finish";
const SAVE_TEAMS = "PUT /api/admin/seasons/setup/teams";
const STEP = "PUT /api/admin/seasons/setup/step";
const putCalls = () =>
  calls
    .filter((call) => call.method === "PUT")
    .map((call) => call.url.replace("/api/admin/seasons/setup", "") || "/");
const stepReply =
  (current: Record<string, unknown> = {}) =>
  (body: { step: number }) => ({
    status: 200,
    body: season({ ...current, setupStep: body.step }),
  });

test("an admin with no Setup season can start one and lands on Basics", async () => {
  stubApi("Admin");
  replies["POST /api/admin/seasons"] = {
    status: 201,
    body: season({ year: 2027, name: "2027 Swan League", isSimulation: true }),
  };
  render(<App />);

  const card = await screen.findByRole("form", { name: "Start a new season" });
  fireEvent.change(within(card).getByLabelText("Season year"), {
    target: { value: "2027" },
  });
  fireEvent.change(within(card).getByLabelText("Season name"), {
    target: { value: "2027 Swan League" },
  });
  fireEvent.click(within(card).getByLabelText("Simulation season"));
  fireEvent.click(within(card).getByRole("button", { name: "Create season" }));

  expect(await screen.findByRole("form", { name: "Basics" })).toBeVisible();
  expect(screen.getByText("Phase 1 of 5: League details")).toBeVisible();
  expect(screen.getByText("Step 1 of 2: Basics")).toBeVisible();
  expect(screen.getByLabelText("Season name")).toHaveValue("2027 Swan League");
  expect(calls.find((call) => call.method === "POST")).toMatchObject({
    body: { year: 2027, name: "2027 Swan League", isSimulation: true },
  });
  expect(
    screen.queryByRole("form", { name: "Start a new season" }),
  ).not.toBeInTheDocument();
  expect(screen.queryByRole("form", { name: "Money" })).not.toBeInTheDocument();
});

test.each([
  [0, "Basics", 1],
  [1, "Money", 2],
  [2, "Weekly deadline", 3],
  [3, "Teams and point values", 4],
  [4, "Schedule import", 5],
  [5, "Weeks and dogs", 6],
  [6, "Fines and payouts", 7],
  [7, "League settings", 8],
  [8, "Coaches", 9],
  [9, "Ready to launch", 10],
  [10, "Ready to launch", 10],
])(
  "a fresh load resumes at the saved step %i",
  async (setupStep, form, stepNumber) => {
    stubApi("Admin");
    replies[SETUP] = { status: 200, body: season({ setupStep }) };
    render(<App />);

    expect(await screen.findByRole("form", { name: form })).toBeVisible();
    expect(screen.getAllByRole("form")).toHaveLength(1);
    const [milestone, name, part] = {
      1: ["1", "League details", "Step 1 of 2: Basics"],
      2: ["1", "League details", "Step 2 of 2: Money"],
      3: ["2", "Competition rules", "Step 1 of 2: Weekly deadline"],
      4: ["2", "Competition rules", "Step 2 of 2: Teams and point values"],
      5: ["3", "Schedule", "Step 1 of 2: Schedule import"],
      6: ["3", "Schedule", "Step 2 of 2: Weeks and dogs"],
      7: ["4", "People & payouts", "Step 1 of 3: Fines and payouts"],
      8: ["4", "People & payouts", "Step 2 of 3: League settings"],
      9: ["4", "People & payouts", "Step 3 of 3: Coaches"],
      10: ["5", "Review & launch", ""],
    }[stepNumber] as [string, string, string];
    expect(screen.getByText(`Phase ${milestone} of 5: ${name}`)).toBeVisible();
    if (part) expect(screen.getByText(part)).toBeVisible();
    expect(putCalls()).toEqual([]);
  },
);

test("the Money page is populated and its pool total follows the stakes", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 1 }) };
  render(<App />);

  expect(await screen.findByLabelText("Buy-in")).toHaveValue(100);
  expect(screen.getByLabelText("Main")).toHaveValue(60);
  expect(screen.getByLabelText("Playoff")).toHaveValue(10);
  expect(screen.getByText("Pool total $100.00")).toBeVisible();
  fireEvent.change(screen.getByLabelText("Playoff"), {
    target: { value: "15.50" },
  });
  expect(screen.getByText("Pool total $105.50")).toBeVisible();
});

test("Save & continue on Basics saves it, records Money as the resume point and shows Money", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season() };
  replies["PUT /api/admin/seasons/setup"] = {
    status: 200,
    body: season({ year: 2027, isSimulation: true }),
  };
  replies[STEP] = stepReply({ year: 2027, isSimulation: true });
  render(<App />);

  fireEvent.change(await screen.findByLabelText("Season year"), {
    target: { value: "2027" },
  });
  fireEvent.click(screen.getByLabelText("Simulation season"));
  fireEvent.click(screen.getByRole("button", { name: "Save & continue" }));

  expect(await screen.findByRole("form", { name: "Money" })).toBeVisible();
  expect(screen.getByText("Phase 1 of 5: League details")).toBeVisible();
  expect(screen.getByText("Step 2 of 2: Money")).toBeVisible();
  expect(putCalls()).toEqual(["/", "/step"]);
  expect(
    calls.find(
      (call) =>
        call.url === "/api/admin/seasons/setup" && call.method === "PUT",
    ),
  ).toMatchObject({
    body: { year: 2027, name: "2026 Season", isSimulation: true },
  });
  expect(calls.find((call) => call.url.endsWith("/step"))).toMatchObject({
    body: { step: 1 },
  });
});

test("Money then deadline walk the wizard and the last save records the next unavailable step", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 1 }) };
  replies["PUT /api/admin/seasons/setup/money"] = {
    status: 200,
    body: season({ setupStep: 1 }),
  };
  replies["PUT /api/admin/seasons/setup/deadline"] = {
    status: 200,
    body: season({
      setupStep: 2,
      settings: {
        buyIn: money(100),
        doubleDogWeeks: 3,
        teamPickDeadlineEastern: "11:30:00",
      },
    }),
  };
  replies[STEP] = stepReply();
  render(<App />);

  fireEvent.change(await screen.findByLabelText("Buy-in"), {
    target: { value: "150" },
  });
  for (const [label, value] of [
    ["Main", "90"],
    ["Dog", "30"],
    ["Regular Season", "15"],
    ["Playoff", "15"],
  ])
    fireEvent.change(screen.getByLabelText(label), { target: { value } });
  fireEvent.click(screen.getByRole("button", { name: "Save & continue" }));
  expect(
    await screen.findByRole("form", { name: "Weekly deadline" }),
  ).toBeVisible();
  expect(calls.find((call) => call.url.endsWith("/money"))).toMatchObject({
    body: { buyIn: 150, main: 90, dog: 30, regularSeason: 15, playoff: 15 },
  });
  expect(calls.filter((call) => call.url.endsWith("/step"))[0]).toMatchObject({
    body: { step: 2 },
  });

  fireEvent.change(screen.getByLabelText("Team picks lock Sundays at"), {
    target: { value: "11:30" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save & continue" }));

  expect(
    await screen.findByRole("form", { name: "Teams and point values" }),
  ).toBeVisible();
  expect(calls.find((call) => call.url.endsWith("/deadline"))).toMatchObject({
    body: { teamPickDeadlineEastern: "11:30:00" },
  });
  expect(calls.filter((call) => call.url.endsWith("/step"))[1]).toMatchObject({
    body: { step: 3 },
  });
});

test("saving an earlier page never moves the saved resume point backwards", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 2 }) };
  replies["PUT /api/admin/seasons/setup"] = {
    status: 200,
    body: season({ setupStep: 2 }),
  };
  render(<App />);

  fireEvent.click(await screen.findByRole("button", { name: "Back" }));
  fireEvent.click(await screen.findByRole("button", { name: "Back" }));
  expect(await screen.findByRole("form", { name: "Basics" })).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "Save & continue" }));

  expect(await screen.findByRole("form", { name: "Money" })).toBeVisible();
  expect(putCalls()).toEqual(["/"]);
});

test("Back only navigates: nothing is saved and unsaved edits survive the round trip", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 1 }) };
  render(<App />);

  fireEvent.change(await screen.findByLabelText("Playoff"), {
    target: { value: "7" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Back" }));
  expect(await screen.findByRole("form", { name: "Basics" })).toBeVisible();
  expect(
    screen.queryByRole("button", { name: "Back" }),
  ).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText("Season name"), {
    target: { value: "Edited" },
  });
  replies["PUT /api/admin/seasons/setup"] = {
    status: 500,
    body: { title: "stop here" },
  };
  fireEvent.click(screen.getByRole("button", { name: "Save & continue" }));
  await screen.findByRole("alert");
  expect(putCalls()).toEqual(["/"]);
  expect(screen.getByLabelText("Season name")).toHaveValue("Edited");
});

test("the browser Back button returns to the previous page without saving", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season() };
  replies["PUT /api/admin/seasons/setup"] = { status: 200, body: season() };
  replies[STEP] = stepReply();
  render(<App />);
  fireEvent.click(
    await screen.findByRole("button", { name: "Save & continue" }),
  );
  expect(await screen.findByRole("form", { name: "Money" })).toBeVisible();
  const before = putCalls().length;

  window.history.back();
  fireEvent.popState(window, { state: { setupView: 0 } });

  expect(await screen.findByRole("form", { name: "Basics" })).toBeVisible();
  expect(putCalls()).toHaveLength(before);
});

test("an API error stays beside the page, keeps what was typed and does not advance", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 1 }) };
  replies["PUT /api/admin/seasons/setup/money"] = {
    status: 400,
    body: {
      code: "season.money",
      message: "Pool stakes must add up to the buy-in.",
    },
  };
  render(<App />);

  fireEvent.change(await screen.findByLabelText("Playoff"), {
    target: { value: "5" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save & continue" }));

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Pool stakes must add up to the buy-in.",
  );
  expect(screen.getByLabelText("Playoff")).toHaveValue(5);
  expect(screen.getByRole("form", { name: "Money" })).toBeVisible();
  expect(putCalls()).toEqual(["/money"]);
});

test("a failed resume-point save is reported and the page does not advance", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season() };
  replies["PUT /api/admin/seasons/setup"] = { status: 200, body: season() };
  replies[STEP] = { status: 500, body: { title: "db down" } };
  render(<App />);

  fireEvent.click(
    await screen.findByRole("button", { name: "Save & continue" }),
  );

  expect(await screen.findByRole("alert")).toHaveTextContent("progress");
  expect(screen.getByRole("form", { name: "Basics" })).toBeVisible();
});

test("every wizard page exists, so nothing is listed as coming next and no placeholder pages are offered", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 10 }) };
  render(<App />);

  await screen.findByRole("form", { name: "Ready to launch" });
  expect(screen.queryByText(/Coming next/)).not.toBeInTheDocument();
  expect(
    within(screen.getByRole("region", { name: "Season setup" })).queryByRole(
      "link",
    ),
  ).not.toBeInTheDocument();
});

const expandDetails = (root: HTMLElement) =>
  root.querySelectorAll("details").forEach((details) => {
    details.open = true;
  });

const openSchedule = async (overrides: Record<string, unknown> = {}) => {
  stubApi("Admin");
  replies[SETUP] = {
    status: 200,
    body: season({ setupStep: 4, ...overrides }),
  };
  render(<App />);
  const form = await screen.findByRole("form", { name: "Schedule import" });
  await within(form).findByText(
    /Nothing is saved until you confirm|Schedule imported/,
  );
  return form;
};

test("opening the Schedule import page reads only the saved schedule, never the provider", async () => {
  const form = await openSchedule();

  expect(within(form).getByText(/BALLDONTLIE/)).toBeVisible();
  expect(
    within(form).getByText(/Nothing is saved until you confirm/),
  ).toBeVisible();
  expect(
    within(form).getByRole("button", { name: "Preview schedule" }),
  ).toBeVisible();
  expect(
    within(form).queryByRole("button", { name: /^Import/ }),
  ).not.toBeInTheDocument();
  expect(
    calls
      .filter((call) => call.url.includes("schedule"))
      .map((call) => `${call.method} ${call.url}`),
  ).toEqual(["GET /api/admin/seasons/setup/schedule"]);
});

test("previewing lists the games by week in Eastern time and saves nothing until confirmed", async () => {
  replies[PREVIEW] = { status: 200, body: plan() };
  const form = await openSchedule();

  fireEvent.click(
    within(form).getByRole("button", { name: "Preview schedule" }),
  );

  // The preview is a one-line summary first; the games sit behind a deliberate disclosure.
  expect(
    await within(form).findByText(
      "Preview: 3 games in 2 weeks. Nothing is saved until you import.",
    ),
  ).toBeVisible();
  expect(within(form).getByText("Review games by week (3)")).toBeVisible();
  expect(within(form).getByText("Week 1")).not.toBeVisible();
  expandDetails(form);
  expect(within(form).getByText("Week 1")).toBeVisible();
  expect(within(form).getByText("Week 2")).toBeVisible();
  expect(
    within(form).getByText(/New York Jets at Buffalo Bills/),
  ).toHaveTextContent("Sun, Sep 13, 1:00 PM EDT");
  expect(
    within(form).getByText(/New York Giants at Dallas Cowboys/),
  ).toHaveTextContent("Sun, Sep 13, 8:20 PM EDT");
  expect(
    within(form).getByRole("button", { name: "Import 3 games" }),
  ).toBeVisible();
  expect(calls.some((call) => call.url.endsWith("/import"))).toBe(false);
});

test("confirming imports with the previewed fingerprint, records Weeks and dogs and shows the saved schedule", async () => {
  replies[PREVIEW] = { status: 200, body: plan() };
  replies[IMPORT] = {
    status: 200,
    body: plan({ imported: true, fingerprint: null }),
  };
  replies[STEP] = stepReply();
  const form = await openSchedule();
  fireEvent.click(
    within(form).getByRole("button", { name: "Preview schedule" }),
  );

  fireEvent.click(
    await within(form).findByRole("button", { name: "Import 3 games" }),
  );

  expect(
    await within(form).findByText("Schedule imported: 3 games in 2 weeks."),
  ).toBeVisible();
  expect(calls.find((call) => call.url.endsWith("/import"))).toMatchObject({
    method: "POST",
    body: { fingerprint: "FP1" },
  });
  expect(calls.find((call) => call.url.endsWith("/step"))).toMatchObject({
    body: { step: 5 },
  });
  expect(
    within(form).queryByRole("button", { name: /Preview|Import/ }),
  ).not.toBeInTheDocument();
  expect(screen.queryByText(/Coming next/)).not.toBeInTheDocument();
});

test("an empty preview says nothing is published yet and that manual import stays available", async () => {
  replies[PREVIEW] = {
    status: 200,
    body: plan({ fingerprint: null, gameCount: 0, weeks: [] }),
  };
  const form = await openSchedule();

  fireEvent.click(
    within(form).getByRole("button", { name: "Preview schedule" }),
  );

  expect(
    await within(form).findByText(
      /No schedule has been published for 2026 yet/,
    ),
  ).toHaveTextContent("Manual/CSV import will remain available");
  expect(
    within(form).queryByRole("button", { name: /^Import/ }),
  ).not.toBeInTheDocument();
});

test("a provider problem is shown with the manual-import note and no import button", async () => {
  replies[PREVIEW] = {
    status: 502,
    body: {
      code: "schedule.provider",
      message: "The schedule provider answered HTTP 500.",
    },
  };
  const form = await openSchedule();

  fireEvent.click(
    within(form).getByRole("button", { name: "Preview schedule" }),
  );

  const alert = await within(form).findByRole("alert");
  expect(alert).toHaveTextContent("The schedule provider answered HTTP 500.");
  expect(alert).toHaveTextContent("Manual/CSV import will remain available");
  expect(
    within(form).queryByRole("button", { name: /^Import/ }),
  ).not.toBeInTheDocument();
  expect(
    within(form).getByRole("button", { name: "Preview schedule" }),
  ).toBeVisible();
});

test("a refused import shows why, keeps the preview and records no progress", async () => {
  replies[PREVIEW] = { status: 200, body: plan() };
  replies[IMPORT] = {
    status: 409,
    body: {
      code: "schedule.changed",
      message:
        "The provider schedule changed since the preview; preview it again.",
    },
  };
  const form = await openSchedule();
  fireEvent.click(
    within(form).getByRole("button", { name: "Preview schedule" }),
  );

  fireEvent.click(
    await within(form).findByRole("button", { name: "Import 3 games" }),
  );

  expect(await within(form).findByRole("alert")).toHaveTextContent(
    "preview it again",
  );
  expandDetails(form);
  expect(within(form).getByText("Week 1")).toBeVisible();
  expect(calls.some((call) => call.url.endsWith("/step"))).toBe(false);
});

test("an already imported schedule is shown on load with no way to import again", async () => {
  replies[SCHEDULE] = {
    status: 200,
    body: plan({ imported: true, fingerprint: null }),
  };
  const form = await openSchedule();

  expect(
    await within(form).findByText("Schedule imported: 3 games in 2 weeks."),
  ).toBeVisible();
  expandDetails(form);
  expect(within(form).getByText("Week 2")).toBeVisible();
  expect(
    within(form).queryByRole("button", { name: /Preview|Import/ }),
  ).not.toBeInTheDocument();
});

test("discarding a preview returns to the start of the page and Back returns to Teams", async () => {
  replies[PREVIEW] = { status: 200, body: plan() };
  const form = await openSchedule();
  fireEvent.click(
    within(form).getByRole("button", { name: "Preview schedule" }),
  );
  await within(form).findByText("Week 1");

  fireEvent.click(
    within(form).getByRole("button", { name: "Discard preview" }),
  );
  expect(within(form).queryByText("Week 1")).not.toBeInTheDocument();
  expect(
    within(form).getByRole("button", { name: "Preview schedule" }),
  ).toBeVisible();

  fireEvent.click(within(form).getByRole("button", { name: "Back" }));
  expect(
    await screen.findByRole("form", { name: "Teams and point values" }),
  ).toBeVisible();
});

const openDogs = async (
  selected: number[] = [],
  overrides: Record<string, unknown> = {},
) => {
  stubApi("Admin");
  replies[SETUP] = {
    status: 200,
    body: season({ setupStep: 5, ...overrides }),
  };
  replies[DOGS] = { status: 200, body: dogPlan(selected) };
  render(<App />);
  const form = await screen.findByRole("form", { name: "Weeks and dogs" });
  await within(form).findByRole("checkbox", { name: "Week 1" });
  return form;
};

test("the Weeks and dogs page lists all 18 weeks with Week 2 required and the holiday weeks suggested", async () => {
  const form = await openDogs();

  expect(within(form).getAllByRole("checkbox")).toHaveLength(18);
  const two = within(form).getByRole("checkbox", { name: "Week 2" });
  expect(two).toBeChecked();
  expect(two).toBeDisabled();
  expect(within(form).getByText("Required by the rules")).toBeVisible();
  expect(within(form).getByRole("checkbox", { name: "Week 12" })).toBeChecked();
  expect(within(form).getByRole("checkbox", { name: "Week 17" })).toBeChecked();
  expect(
    within(form).getByText(/Suggested · contains Thanksgiving \(Thu Nov 26\)/),
  ).toBeVisible();
  expect(
    within(form).getByText(/Suggested · contains Christmas \(Fri Dec 25\)/),
  ).toBeVisible();
  expect(
    within(form).getByRole("checkbox", { name: "Week 5" }),
  ).not.toBeChecked();
  expect(within(form).getByText("3 selected · choose 3–4")).toBeVisible();
  expect(putCalls()).toEqual([]);
});

test("a saved selection is shown instead of the suggestions", async () => {
  const form = await openDogs([2, 5, 9, 14]);

  expect(
    within(form).getByRole("checkbox", { name: "Week 12" }),
  ).not.toBeChecked();
  expect(within(form).getByRole("checkbox", { name: "Week 14" })).toBeChecked();
  expect(within(form).getByText("4 selected · choose 3–4")).toBeVisible();
});

test("a fifth week cannot be picked and unchecking frees the others again", async () => {
  const form = await openDogs([2, 5, 9, 14]);

  expect(within(form).getByRole("checkbox", { name: "Week 1" })).toBeDisabled();
  fireEvent.click(within(form).getByRole("checkbox", { name: "Week 14" }));
  expect(within(form).getByText("3 selected · choose 3–4")).toBeVisible();
  expect(within(form).getByRole("checkbox", { name: "Week 1" })).toBeEnabled();
  fireEvent.click(within(form).getByRole("checkbox", { name: "Week 1" }));
  expect(within(form).getByText("4 selected · choose 3–4")).toBeVisible();
});

test("choosing fewer than three weeks is explained without a request", async () => {
  const form = await openDogs();
  fireEvent.click(within(form).getByRole("checkbox", { name: "Week 12" }));

  fireEvent.click(
    within(form).getByRole("button", { name: "Save & continue" }),
  );

  expect(await within(form).findByRole("alert")).toHaveTextContent(
    "Choose at least 3",
  );
  expect(putCalls()).toEqual([]);
});

test("saving sends the chosen weeks, records the next step and moves on to Fines and payouts", async () => {
  replies[SAVE_DOGS] = (body: { weeks?: number[] }) => ({
    status: 200,
    body: dogPlan(body.weeks),
  });
  replies[STEP] = stepReply();
  const form = await openDogs();
  fireEvent.click(within(form).getByRole("checkbox", { name: "Week 5" }));

  fireEvent.click(
    within(form).getByRole("button", { name: "Save & continue" }),
  );

  expect(
    await screen.findByRole("form", { name: "Fines and payouts" }),
  ).toBeVisible();
  expect(
    calls.find(
      (call) => call.method === "PUT" && call.url.endsWith("/double-dog-weeks"),
    ),
  ).toMatchObject({ body: { weeks: [2, 5, 12, 17] } });
  expect(calls.find((call) => call.url.endsWith("/step"))).toMatchObject({
    body: { step: 6 },
  });
  expect(screen.queryByText(/Coming next/)).not.toBeInTheDocument();
});

test("a server rejection is shown beside the page, keeps the choice and records no progress", async () => {
  replies[SAVE_DOGS] = {
    status: 400,
    body: {
      code: "season.double-dogs",
      message: "Week 2 is always a double-dog week; include it.",
    },
  };
  const form = await openDogs();
  fireEvent.click(within(form).getByRole("checkbox", { name: "Week 5" }));

  fireEvent.click(
    within(form).getByRole("button", { name: "Save & continue" }),
  );

  expect(await within(form).findByRole("alert")).toHaveTextContent(
    "Week 2 is always a double-dog week",
  );
  expect(within(form).getByRole("checkbox", { name: "Week 5" })).toBeChecked();
  expect(calls.some((call) => call.url.endsWith("/step"))).toBe(false);
});

test("a missing imported schedule is reported instead of an empty list", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 5 }) };
  replies[DOGS] = {
    status: 409,
    body: {
      code: "schedule.missing",
      message:
        "Import the full regular-season schedule before choosing double-dog weeks.",
    },
  };
  render(<App />);

  const form = await screen.findByRole("form", { name: "Weeks and dogs" });

  expect(await within(form).findByRole("alert")).toHaveTextContent(
    "Import the full regular-season schedule",
  );
  expect(within(form).queryByRole("checkbox")).not.toBeInTheDocument();
  expect(
    within(form).queryByRole("button", { name: "Save & continue" }),
  ).not.toBeInTheDocument();
  expect(within(form).getByRole("button", { name: "Back" })).toBeVisible();
});

test("after importing, Continue opens Weeks and dogs without saving anything", async () => {
  replies[SCHEDULE] = {
    status: 200,
    body: plan({ imported: true, fingerprint: null }),
  };
  const form = await openSchedule();

  fireEvent.click(
    await within(form).findByRole("button", { name: "Continue" }),
  );

  expect(
    await screen.findByRole("form", { name: "Weeks and dogs" }),
  ).toBeVisible();
  expect(putCalls()).toEqual([]);
});

const openFines = async (
  plan = finesPlan(),
  overrides: Record<string, unknown> = {},
) => {
  stubApi("Admin");
  replies[SETUP] = {
    status: 200,
    body: season({ setupStep: 6, ...overrides }),
  };
  replies[FINES] = { status: 200, body: plan };
  render(<App />);
  const form = await screen.findByRole("form", { name: "Fines and payouts" });
  await within(form).findByLabelText("Missed team-pick fine");
  return form;
};

test("a fresh Fines and payouts page defaults to a $5 fine and 100% to first place, explaining the zero-coach state", async () => {
  const form = await openFines();

  expect(within(form).getByText("0 active coaches")).toBeVisible();
  expect(within(form).getByLabelText("Missed team-pick fine")).toHaveValue(5);
  for (const pool of ["Main", "Dog", "Regular Season", "Playoff"])
    expect(
      within(form).getByLabelText(`${pool} 1st place percent`),
    ).toHaveValue(100);
  expect(
    within(form).getByText("Main pool · $60.00 per coach · current pot $0.00"),
  ).toBeVisible();
  expect(
    within(form).getByText(
      "No active coaches yet. Payouts update as coaches are added.",
    ),
  ).toBeVisible();
  expect(within(form).getByText("Main: 1st $0.00")).toBeVisible();
  expect(putCalls()).toEqual([]);
});

test("a saved configuration is shown and the calculator follows the edits for the current coaches", async () => {
  const form = await openFines(
    finesPlan(
      7,
      {
        Main: [
          [1, 70],
          [2, 30],
        ],
        Dog: [[1, 100]],
        "Regular Season": [[1, 100]],
        Playoff: [[1, 100]],
      },
      7.5,
    ),
  );

  expect(within(form).getByText("7 active coaches")).toBeVisible();
  expect(within(form).getByLabelText("Missed team-pick fine")).toHaveValue(7.5);
  expect(within(form).getByLabelText("Main 2nd place percent")).toHaveValue(30);
  expect(
    within(form).getByText(
      "Main pool · $60.00 per coach · current pot $420.00",
    ),
  ).toBeVisible();
  expect(
    within(form).getByText("Main: 1st $294.00 · 2nd $126.00"),
  ).toBeVisible();
  fireEvent.change(within(form).getByLabelText("Main 1st place percent"), {
    target: { value: "60" },
  });
  fireEvent.change(within(form).getByLabelText("Main 2nd place percent"), {
    target: { value: "40" },
  });
  expect(
    within(form).getByText("Main: 1st $252.00 · 2nd $168.00"),
  ).toBeVisible();
});

test("the calculator rounds down and gives the remainder to first place so amounts add up to the pot", async () => {
  const form = await openFines(
    finesPlan(7, {
      Main: [
        [1, 33.33],
        [2, 33.33],
        [3, 33.34],
      ],
      Dog: [[1, 100]],
      "Regular Season": [[1, 100]],
      Playoff: [[1, 100]],
    }),
  );

  expect(
    within(form).getByText("Main: 1st $140.00 · 2nd $139.98 · 3rd $140.02"),
  ).toBeVisible();
});

test("one coach is singular", async () => {
  const form = await openFines(finesPlan(1));

  expect(within(form).getByText("1 active coach")).toBeVisible();
});

test("placings can be added and removed and each pool shows whether it totals 100.00%", async () => {
  const form = await openFines();

  fireEvent.click(
    within(form).getByRole("button", { name: "Add Main placing" }),
  );
  expect(within(form).getByLabelText("Main 2nd place percent")).toBeVisible();
  fireEvent.change(within(form).getByLabelText("Main 1st place percent"), {
    target: { value: "70" },
  });
  expect(
    within(form).getByText("Main total 70.00% — must be 100.00%"),
  ).toBeVisible();
  fireEvent.change(within(form).getByLabelText("Main 2nd place percent"), {
    target: { value: "30" },
  });
  expect(within(form).getByText("Main total 100.00%")).toBeVisible();
  fireEvent.click(
    within(form).getByRole("button", { name: "Remove Main 2nd place" }),
  );
  expect(
    within(form).queryByLabelText("Main 2nd place percent"),
  ).not.toBeInTheDocument();
  expect(
    within(form).queryByRole("button", { name: "Remove Main 1st place" }),
  ).not.toBeInTheDocument();
});

test("a pool that does not total 100.00% is explained without a request and keeps the edits", async () => {
  const form = await openFines();
  fireEvent.change(within(form).getByLabelText("Dog 1st place percent"), {
    target: { value: "90" },
  });

  fireEvent.click(
    within(form).getByRole("button", { name: "Save & continue" }),
  );

  expect(await within(form).findByRole("alert")).toHaveTextContent("Dog");
  expect(within(form).getByRole("alert")).toHaveTextContent("100.00");
  expect(within(form).getByLabelText("Dog 1st place percent")).toHaveValue(90);
  expect(putCalls()).toEqual([]);
});

test.each(["0", "-1", "5.001", ""])(
  "a missed-pick fine of '%s' is explained without a request",
  async (value) => {
    const form = await openFines();
    fireEvent.change(within(form).getByLabelText("Missed team-pick fine"), {
      target: { value },
    });

    fireEvent.click(
      within(form).getByRole("button", { name: "Save & continue" }),
    );

    expect(await within(form).findByRole("alert")).toHaveTextContent("fine");
    expect(putCalls()).toEqual([]);
  },
);

test("saving sends the fine and every plan, records the next step and moves on to League settings", async () => {
  replies[SAVE_FINES] = {
    status: 200,
    body: finesPlan(
      2,
      {
        Main: [
          [1, 70],
          [2, 30],
        ],
        Dog: [[1, 100]],
        "Regular Season": [[1, 100]],
        Playoff: [[1, 100]],
      },
      6,
    ),
  };
  replies[STEP] = stepReply();
  const form = await openFines();
  fireEvent.change(within(form).getByLabelText("Missed team-pick fine"), {
    target: { value: "6" },
  });
  fireEvent.click(
    within(form).getByRole("button", { name: "Add Main placing" }),
  );
  fireEvent.change(within(form).getByLabelText("Main 1st place percent"), {
    target: { value: "70" },
  });
  fireEvent.change(within(form).getByLabelText("Main 2nd place percent"), {
    target: { value: "30" },
  });

  fireEvent.click(
    within(form).getByRole("button", { name: "Save & continue" }),
  );

  expect(
    await screen.findByRole("form", { name: "League settings" }),
  ).toBeVisible();
  expect(
    calls.find(
      (call) =>
        call.method === "PUT" && call.url.endsWith("/fines-and-payouts"),
    ),
  ).toMatchObject({
    body: {
      missedPickFine: 6,
      payouts: [
        {
          pool: "Main",
          places: [
            { position: 1, percent: 70 },
            { position: 2, percent: 30 },
          ],
        },
        { pool: "Dog", places: [{ position: 1, percent: 100 }] },
        { pool: "Regular Season", places: [{ position: 1, percent: 100 }] },
        { pool: "Playoff", places: [{ position: 1, percent: 100 }] },
      ],
    },
  });
  expect(calls.find((call) => call.url.endsWith("/step"))).toMatchObject({
    body: { step: 7 },
  });
  expect(screen.queryByText(/Coming next/)).not.toBeInTheDocument();
});

test("a server rejection stays beside the page, keeps the edits and records no progress", async () => {
  replies[SAVE_FINES] = {
    status: 400,
    body: {
      code: "season.payouts",
      message:
        "Main: percentages must total exactly 100.00 (they total 99.99).",
    },
  };
  const form = await openFines();
  fireEvent.change(within(form).getByLabelText("Missed team-pick fine"), {
    target: { value: "8" },
  });

  fireEvent.click(
    within(form).getByRole("button", { name: "Save & continue" }),
  );

  expect(await within(form).findByRole("alert")).toHaveTextContent(
    "they total 99.99",
  );
  expect(within(form).getByLabelText("Missed team-pick fine")).toHaveValue(8);
  expect(calls.some((call) => call.url.endsWith("/step"))).toBe(false);
});

test("a failed load shows an error and leaves Back", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 6 }) };
  replies[FINES] = { status: 500, body: { title: "Boom" } };
  render(<App />);

  const form = await screen.findByRole("form", { name: "Fines and payouts" });

  expect(await within(form).findByRole("alert")).toHaveTextContent("Boom");
  expect(
    within(form).queryByRole("button", { name: "Save & continue" }),
  ).not.toBeInTheDocument();
  expect(within(form).getByRole("button", { name: "Back" })).toBeVisible();
});

const openRules = async (plan = rulesPlan()) => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 7 }) };
  replies[RULES] = { status: 200, body: plan };
  render(<App />);
  const form = await screen.findByRole("form", { name: "League settings" });
  await within(form).findByLabelText(/R17/);
  return form;
};

test("the League settings page shows the three settings with their defaults", async () => {
  const form = await openRules();

  expect(within(form).getByLabelText(/R17/)).toBeChecked();
  expect(within(form).getByLabelText("1 point color")).toHaveValue("Green");
  expect(within(form).getByLabelText("2 point color")).toHaveValue("Blue");
  expect(within(form).getByLabelText("3 point color")).toHaveValue("Yellow");
  expect(within(form).getByLabelText("4 point color")).toHaveValue("Red");
  expect(within(form).getByLabelText("First occurrence")).toBeChecked();
  expect(within(form).getByLabelText("Most occurrences")).not.toBeChecked();
  expect(putCalls()).toEqual([]);
});

test("every other rule is listed as fixed and R17 to R19 are not among them", async () => {
  const form = await openRules();

  const fixed = within(form).getByRole("list", { name: "Fixed rules" });
  const ids = within(fixed)
    .getAllByRole("listitem")
    .map((item) => item.textContent!.split(" ")[0]);
  expect(ids).toEqual([
    ..."R1 R2 R3 R4 R5 R6 R7 R8 R9 R10 R11 R12 R13 R14 R15 R16 R20 R21".split(
      " ",
    ),
  ]);
  expect(
    within(fixed).getByText(/Each coach has exactly 9 NFL teams/),
  ).toBeVisible();
});

test("the page says plainly what each setting does today", async () => {
  const form = await openRules();

  expect(
    within(form).getByText(
      /R17 takes effect the next time a week is scored or recomputed/,
    ),
  ).toBeVisible();
  expect(within(form).getByText(/R18 and R19 are saved now/)).toBeVisible();
  expect(
    within(form).queryByText(/tie-?break(ers)? (are|is) (applied|enforced)/i),
  ).not.toBeInTheDocument();
});

test("changed settings are sent in the API shape, the next step is recorded and the wizard moves on to Coaches", async () => {
  replies[SAVE_RULES] = { status: 200, body: rulesPlan() };
  replies[STEP] = stepReply();
  const form = await openRules();
  fireEvent.click(within(form).getByLabelText(/R17/));
  fireEvent.change(within(form).getByLabelText("1 point color"), {
    target: { value: "Red" },
  });
  fireEvent.change(within(form).getByLabelText("4 point color"), {
    target: { value: "Green" },
  });
  fireEvent.click(within(form).getByLabelText("Most occurrences"));

  fireEvent.click(
    within(form).getByRole("button", { name: "Save & continue" }),
  );

  expect(await screen.findByRole("form", { name: "Coaches" })).toBeVisible();
  expect(
    calls.find(
      (call) => call.method === "PUT" && call.url.endsWith("/league-settings"),
    ),
  ).toMatchObject({
    body: {
      teamMayAlsoBeDog: false,
      pointColors: [
        { points: 1, color: "Red" },
        { points: 2, color: "Blue" },
        { points: 3, color: "Yellow" },
        { points: 4, color: "Green" },
      ],
      recordHolder: "MostOccurrences",
    },
  });
  expect(calls.find((call) => call.url.endsWith("/step"))).toMatchObject({
    body: { step: 8 },
  });
  expect(screen.queryByText(/Coming next/)).not.toBeInTheDocument();
});

test("a color used twice is explained without a request and keeps the choices", async () => {
  const form = await openRules();
  fireEvent.change(within(form).getByLabelText("2 point color"), {
    target: { value: "Green" },
  });

  expect(
    within(form).getByText("Each color can be used for only one point value."),
  ).toBeVisible();
  fireEvent.click(
    within(form).getByRole("button", { name: "Save & continue" }),
  );

  expect(await within(form).findByRole("alert")).toHaveTextContent(
    "only one point value",
  );
  expect(within(form).getByLabelText("2 point color")).toHaveValue("Green");
  expect(putCalls()).toEqual([]);
});

test("a server rejection stays beside the page, keeps the edits and records no progress", async () => {
  replies[SAVE_RULES] = {
    status: 400,
    body: {
      code: "season.league-settings",
      message: "Give each point value from 1 to 4 exactly one color.",
    },
  };
  const form = await openRules();
  fireEvent.click(within(form).getByLabelText(/R17/));

  fireEvent.click(
    within(form).getByRole("button", { name: "Save & continue" }),
  );

  expect(await within(form).findByRole("alert")).toHaveTextContent(
    "exactly one color",
  );
  expect(within(form).getByLabelText(/R17/)).not.toBeChecked();
  expect(calls.some((call) => call.url.endsWith("/step"))).toBe(false);
});

test("a failed load shows an error and leaves Back", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 7 }) };
  replies[RULES] = { status: 500, body: { title: "Boom" } };
  render(<App />);

  const form = await screen.findByRole("form", { name: "League settings" });

  expect(await within(form).findByRole("alert")).toHaveTextContent("Boom");
  expect(
    within(form).queryByRole("button", { name: "Save & continue" }),
  ).not.toBeInTheDocument();
  expect(within(form).getByRole("button", { name: "Back" })).toBeVisible();
});

const openCoaches = async (plan = coachesPlan()) => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 8 }) };
  replies[COACHES] = { status: 200, body: plan };
  render(<App />);
  const form = await screen.findByRole("form", { name: "Coaches" });
  await within(form).findByText(/coach(es)? selected/);
  return form;
};

test("the Coaches page starts empty, warns about zero coaches and says nothing is sent", async () => {
  const form = await openCoaches();

  expect(within(form).getByText("0 coaches selected")).toBeVisible();
  expect(within(form).getByText(/No coaches yet/)).toBeVisible();
  expect(
    within(form).getByText(/only prepares the coach list/i),
  ).toHaveTextContent(/no invitations or emails are sent/i);
  expect(putCalls()).toEqual([]);
});

test("carry-forward candidates from the previous season are unchecked until chosen and already-selected ones are checked", async () => {
  const form = await openCoaches(
    coachesPlan(
      [{ name: "Lee", email: "lee@example.com" }],
      [
        { name: "Lee", email: "lee@example.com", selected: true },
        { name: "Pat Smith", email: "pat@example.com" },
      ],
    ),
  );

  expect(within(form).getByText(/2025 Season/)).toBeVisible();
  expect(within(form).getByLabelText("Lee (lee@example.com)")).toBeChecked();
  const pat = within(form).getByLabelText("Pat Smith (pat@example.com)");
  expect(pat).not.toBeChecked();
  expect(within(form).getByText("1 coach selected")).toBeVisible();
  fireEvent.click(pat);
  expect(within(form).getByText("2 coaches selected")).toBeVisible();
  fireEvent.click(pat);
  expect(within(form).getByText("1 coach selected")).toBeVisible();
});

test("an earlier season without a name is described by its year, or generically", async () => {
  const named = coachesPlan([], [{ name: "Pat", email: "pat@example.com" }]);
  const form = await openCoaches({
    ...named,
    prior: { id: "p1", year: 2024, name: "" },
  });

  expect(
    within(form).getByText("Carry forward from the 2024 season"),
  ).toBeVisible();
  cleanup();
  const yearless = await openCoaches({
    ...named,
    prior: { id: "p1", year: 0, name: "" },
  });
  expect(
    within(yearless).getByText("Carry forward from the previous season"),
  ).toBeVisible();
});

test("coaches already saved who are not carry-forward people are listed and can be removed, inactive ones are not", async () => {
  const form = await openCoaches(
    coachesPlan([
      { name: "New One", email: "new@example.com" },
      { name: "Dropped", email: "dropped@example.com", active: false },
    ]),
  );

  expect(within(form).getByText("New One (new@example.com)")).toBeVisible();
  expect(within(form).queryByText(/Dropped/)).not.toBeInTheDocument();
  fireEvent.click(within(form).getByRole("button", { name: "Remove New One" }));
  expect(within(form).getByText("0 coaches selected")).toBeVisible();
});

test("saving sends the complete list of carried-forward and new coaches, records the next step and keeps the page", async () => {
  replies[SAVE_COACHES] = {
    status: 200,
    body: coachesPlan([
      { name: "Pat Smith", email: "pat@example.com" },
      { name: "Sam", email: "sam@example.com" },
    ]),
  };
  replies[STEP] = stepReply();
  const form = await openCoaches(
    coachesPlan([], [{ name: "Pat Smith", email: "pat@example.com" }]),
  );
  fireEvent.click(within(form).getByLabelText("Pat Smith (pat@example.com)"));
  fireEvent.click(within(form).getByRole("button", { name: "Add coach" }));
  fireEvent.change(within(form).getByLabelText("New coach 1 email"), {
    target: { value: "  Sam@Example.com " },
  });
  fireEvent.change(within(form).getByLabelText("New coach 1 name"), {
    target: { value: "Sam" },
  });

  fireEvent.click(
    within(form).getByRole("button", { name: "Save & continue" }),
  );

  expect(
    await screen.findByRole("form", { name: "Ready to launch" }),
  ).toBeVisible();
  expect(
    calls.find(
      (call) => call.method === "PUT" && call.url.endsWith("/coaches"),
    ),
  ).toMatchObject({
    body: {
      coaches: [
        { email: "pat@example.com", name: "Pat Smith" },
        { email: "sam@example.com", name: "Sam" },
      ],
    },
  });
  expect(calls.find((call) => call.url.endsWith("/step"))).toMatchObject({
    body: { step: 9 },
  });
  expect(screen.queryByText(/Coming next/)).not.toBeInTheDocument();
});

test("zero coaches can be saved and the empty list is sent", async () => {
  replies[SAVE_COACHES] = { status: 200, body: coachesPlan() };
  replies[STEP] = stepReply();
  const form = await openCoaches();

  fireEvent.click(
    within(form).getByRole("button", { name: "Save & continue" }),
  );

  expect(
    await screen.findByRole("form", { name: "Ready to launch" }),
  ).toBeVisible();
  expect(
    calls.find(
      (call) => call.method === "PUT" && call.url.endsWith("/coaches"),
    ),
  ).toMatchObject({ body: { coaches: [] } });
});

test.each([
  ["a malformed email", "not-an-email", "Sam", /valid email/i],
  ["a blank name", "sam@example.com", "   ", /name/i],
  [
    "a name over 200 characters",
    "sam@example.com",
    "n".repeat(201),
    /200 characters/,
  ],
  [
    "an email over 320 characters",
    `${"e".repeat(320)}@example.com`,
    "Sam",
    /320 characters/,
  ],
])(
  "%s is explained without a request and keeps the row",
  async (_label, email, name, message) => {
    const form = await openCoaches();
    fireEvent.click(within(form).getByRole("button", { name: "Add coach" }));
    fireEvent.change(within(form).getByLabelText("New coach 1 email"), {
      target: { value: email },
    });
    fireEvent.change(within(form).getByLabelText("New coach 1 name"), {
      target: { value: name },
    });

    fireEvent.click(
      within(form).getByRole("button", { name: "Save & continue" }),
    );

    expect(await within(form).findByRole("alert")).toHaveTextContent(message);
    expect(within(form).getByLabelText("New coach 1 email")).toHaveValue(email);
    expect(putCalls()).toEqual([]);
  },
);

test("the same email twice, even in different case or against a carried-forward person, is explained without a request", async () => {
  const form = await openCoaches(
    coachesPlan(
      [],
      [{ name: "Pat", email: "pat@example.com", selected: true }],
    ),
  );
  fireEvent.click(within(form).getByRole("button", { name: "Add coach" }));
  fireEvent.change(within(form).getByLabelText("New coach 1 email"), {
    target: { value: "PAT@example.com" },
  });
  fireEvent.change(within(form).getByLabelText("New coach 1 name"), {
    target: { value: "Pat again" },
  });

  fireEvent.click(
    within(form).getByRole("button", { name: "Save & continue" }),
  );

  expect(await within(form).findByRole("alert")).toHaveTextContent(
    /more than once/i,
  );
  expect(putCalls()).toEqual([]);
});

test("a server rejection stays beside the page, keeps the rows and records no progress", async () => {
  replies[SAVE_COACHES] = {
    status: 400,
    body: {
      code: "season.coaches",
      message:
        "gone@example.com belongs to a deactivated person and cannot be added as a coach.",
    },
  };
  const form = await openCoaches();
  fireEvent.click(within(form).getByRole("button", { name: "Add coach" }));
  fireEvent.change(within(form).getByLabelText("New coach 1 email"), {
    target: { value: "gone@example.com" },
  });
  fireEvent.change(within(form).getByLabelText("New coach 1 name"), {
    target: { value: "Gone" },
  });

  fireEvent.click(
    within(form).getByRole("button", { name: "Save & continue" }),
  );

  expect(await within(form).findByRole("alert")).toHaveTextContent(
    "deactivated",
  );
  expect(within(form).getByLabelText("New coach 1 email")).toHaveValue(
    "gone@example.com",
  );
  expect(calls.some((call) => call.url.endsWith("/step"))).toBe(false);
});

test("a failed load shows an error and leaves Back", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 8 }) };
  replies[COACHES] = { status: 500, body: { title: "Boom" } };
  render(<App />);

  const form = await screen.findByRole("form", { name: "Coaches" });

  expect(await within(form).findByRole("alert")).toHaveTextContent("Boom");
  expect(
    within(form).queryByRole("button", { name: "Save & continue" }),
  ).not.toBeInTheDocument();
  expect(within(form).getByRole("button", { name: "Back" })).toBeVisible();
});

const openReview = async (
  review = reviewData(),
  overrides: Record<string, unknown> = {},
) => {
  stubApi("Admin");
  replies[SETUP] = {
    status: 200,
    body: season({ setupStep: 9, ...overrides }),
  };
  replies[REVIEW] = { status: 200, body: review };
  render(<App />);
  const form = await screen.findByRole("form", { name: "Ready to launch" });
  await within(form).findAllByText(/2027 Swan League/);
  return form;
};

const blocker = (code: string, message: string, step: string) => ({
  code,
  message,
  step,
});

test("the review opens with a compact summary of the saved choices and the launch decision", async () => {
  const form = await openReview();

  const summary = within(
    within(form).getByRole("list", { name: "Setup summary" }),
  );
  expect(summary.getAllByRole("listitem")).toHaveLength(4);
  expect(summary.getByText("League details")).toBeVisible();
  expect(summary.getByText("Competition rules")).toBeVisible();
  expect(summary.getByText("Schedule")).toBeVisible();
  expect(summary.getByText("People & payouts")).toBeVisible();
  expect(summary.getByText(/2027 Swan League \(2027\)/)).toBeVisible();
  expect(summary.getByText(/simulation/)).toBeVisible();
  expect(summary.getByText(/Buy-in \$100\.00/)).toBeVisible();
  expect(
    summary.getByText(/picks lock Sundays at 11:30 AM Eastern/),
  ).toBeVisible();
  expect(
    summary.getByText(/32 of 32 teams have a division finish/),
  ).toBeVisible();
  expect(summary.getByText(/18 weeks, 272 games/)).toBeVisible();
  expect(summary.getByText(/Double-dog weeks: 2, 12, 17/)).toBeVisible();
  expect(summary.getByText(/6 active coaches/)).toBeVisible();
  expect(summary.getByText(/Missed team-pick fine \$5\.00/)).toBeVisible();
  expect(
    within(form).getByRole("button", { name: "Start season and open draft" }),
  ).toBeEnabled();
  expect(putCalls()).toEqual([]);
});

test("View all details holds every saved choice and every division's finishes and point values", async () => {
  const form = await openReview();
  expect(within(form).getByText("View all details")).toBeVisible();
  expandDetails(form);

  expect(within(form).getByText(/Main \$60\.00/)).toBeVisible();
  expect(
    within(form).getByText(/Main: 1st 70\.00% · 2nd 30\.00%/),
  ).toBeVisible();
  expect(within(form).getByText(/R17 off/)).toBeVisible();
  const teams = within(form).getByRole("list", { name: "Teams by division" });
  expect(within(teams).getAllByRole("listitem")).toHaveLength(8);
  expect(
    within(teams).getByText(
      "AFC East: 1st AFC East Team 1 (1 pt) · 2nd AFC East Team 2 (2 pts) · 3rd AFC East Team 3 (3 pts) · 4th AFC East Team 4 (4 pts)",
    ),
  ).toBeVisible();
  expect(
    within(teams).getByText(/^NFC West: 1st NFC West Team 1/),
  ).toBeVisible();
});

test("an Edit button on each summary card goes back to that milestone", async () => {
  const form = await openReview();

  fireEvent.click(
    within(form).getByRole("button", {
      name: "Edit Competition rules",
    }),
  );

  expect(
    await screen.findByRole("form", { name: "Weekly deadline" }),
  ).toBeVisible();
  expect(screen.getByText("Phase 2 of 5: Competition rules")).toBeVisible();
});

test("a team without a finish is shown as not set in its division line", async () => {
  const partial = catalog(true).teams.map((team) =>
    team.teamId === "AFC East-3"
      ? { ...team, finishPosition: null, pointValue: null }
      : team,
  );
  const form = await openReview(
    reviewData({
      ready: false,
      blockers: [
        blocker(
          "teams",
          "Teams and point values are incomplete: 31 of 32 teams have a division finish.",
          "TeamsAndPoints",
        ),
      ],
      teams: { entered: 31, total: 32, teams: partial },
    }),
  );

  expandDetails(form);
  expect(
    within(form).getByText(/AFC East: .*AFC East Team 3 not set/),
  ).toBeVisible();
});

test("the launch decision says what it does before anything is sent", async () => {
  const form = await openReview();
  const launch = within(form).getByRole("group", {
    name: "Start the season",
  });

  expect(launch).toHaveTextContent("Starting is permanent");
  expect(launch).toHaveTextContent("Eastern Time");
  expect(launch).toHaveTextContent("Start season and open draft");
  expect(
    within(form).queryByRole("button", { name: /finish setup/i }),
  ).not.toBeInTheDocument();
  expect(calls.some((call) => call.method === "POST")).toBe(false);
});

test("blockers are listed first with a Fix link to their step and opening the draft is disabled", async () => {
  const form = await openReview(
    reviewData({
      ready: false,
      blockers: [
        blocker(
          "teams",
          "Teams and point values are incomplete: 30 of 32 teams have a division finish.",
          "TeamsAndPoints",
        ),
        blocker(
          "payouts",
          "Save the missed-pick fine and each pool's payout plan.",
          "FinesAndPayouts",
        ),
      ],
    }),
  );

  expect(
    within(form).getByText(/30 of 32 teams have a division finish\./),
  ).toBeVisible();
  expect(
    within(form).getByRole("button", { name: "Start season and open draft" }),
  ).toBeDisabled();
  expect(
    within(form).getByText(/Fix these before starting the season/),
  ).toBeVisible();
  fireEvent.click(
    within(form).getByRole("button", { name: "Fix: Teams and point values" }),
  );
  expect(
    await screen.findByRole("form", { name: "Teams and point values" }),
  ).toBeVisible();
});

test("a zero-coach warning is shown with a Fix link but does not stop opening the draft", async () => {
  const form = await openReview(
    reviewData({
      activeCoaches: 0,
      warnings: [
        blocker(
          "no-coaches",
          "No coaches have been added yet. You can finish setup now and add them later.",
          "Coaches",
        ),
      ],
    }),
  );

  expect(
    within(form).getByText(/No coaches have been added yet/),
  ).toBeVisible();
  expect(
    within(form).getByRole("button", { name: "Start season and open draft" }),
  ).toBeEnabled();
  fireEvent.click(within(form).getByRole("button", { name: "Fix: Coaches" }));
  expect(await screen.findByRole("form", { name: "Coaches" })).toBeVisible();
});

test("confirming finishes setup once, then opens the roster draft with the Eastern deadline", async () => {
  replies[FINISH] = { status: 200, body: reviewData({ completed: true }) };
  replies[ACTIVATE] = {
    status: 200,
    body: {
      id: "s",
      state: "Live",
      rosterDraftDeadline: "2026-10-20T22:00:00Z",
      rosterDraftDeadlineEastern: "2026-10-20T18:00",
    },
  };
  const form = await openReview();
  fireEvent.change(within(form).getByLabelText(/Draft deadline/), {
    target: { value: "2026-10-20T18:00" },
  });

  fireEvent.click(
    within(form).getByRole("button", { name: "Start season and open draft" }),
  );

  const dialog = await screen.findByRole("alertdialog", {
    name: "Start the season and open the draft?",
  });
  expect(dialog).toHaveTextContent("nine teams");
  expect(dialog).toHaveTextContent("Tue, Oct 20, 6:00 PM Eastern Time");
  expect(dialog).toHaveTextContent("only you, the commissioner");
  // Asking sends nothing: the confirmation is the decision.
  expect(calls.some((call) => call.method === "POST")).toBe(false);

  fireEvent.click(
    within(dialog).getByRole("button", { name: "Confirm and open draft" }),
  );

  expect(await screen.findByText(/The season is live/)).toBeVisible();
  expect(
    calls
      .filter((call) => call.method === "POST")
      .map((call) => call.url.replace("/api/admin/seasons/setup", "")),
  ).toEqual(["/review/finish", "/activate"]);
  expect(calls.find((call) => call.url.endsWith("/activate"))).toMatchObject({
    body: { rosterDraftDeadlineEastern: "2026-10-20T18:00" },
  });
});

test("Cancel in the confirmation sends nothing, keeps the deadline and returns focus to the button", async () => {
  const form = await openReview();
  fireEvent.change(within(form).getByLabelText(/Draft deadline/), {
    target: { value: "2026-10-20T18:00" },
  });
  fireEvent.click(
    within(form).getByRole("button", { name: "Start season and open draft" }),
  );
  const dialog = await screen.findByRole("alertdialog");
  expect(within(dialog).getByRole("heading", { level: 3 })).toHaveFocus();

  fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));

  expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
  expect(within(form).getByLabelText(/Draft deadline/)).toHaveValue(
    "2026-10-20T18:00",
  );
  expect(
    within(form).getByRole("button", { name: "Start season and open draft" }),
  ).toHaveFocus();
  expect(calls.some((call) => call.method === "POST")).toBe(false);
});

test("Escape closes the confirmation", async () => {
  const form = await openReview();
  fireEvent.change(within(form).getByLabelText(/Draft deadline/), {
    target: { value: "2026-10-20T18:00" },
  });
  fireEvent.click(
    within(form).getByRole("button", { name: "Start season and open draft" }),
  );
  const dialog = await screen.findByRole("alertdialog");

  fireEvent.keyDown(dialog, { key: "Escape" });

  expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
});

test("a finished setup is shown as finished after a reload and resumes on the review page", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 10 }) };
  replies[REVIEW] = { status: 200, body: reviewData({ completed: true }) };
  render(<App />);

  const form = await screen.findByRole("form", { name: "Ready to launch" });

  expect(await within(form).findByText(/Setup is finished/)).toBeVisible();
  expect(screen.getByText("Phase 5 of 5: Review & launch")).toBeVisible();
  expect(
    within(form).getByRole("button", { name: "Start season and open draft" }),
  ).toBeEnabled();
});

test("a setup that was finished but has since lost something says so and cannot be finished again", async () => {
  const form = await openReview(
    reviewData({
      completed: true,
      ready: false,
      blockers: [
        blocker(
          "schedule",
          "The regular-season schedule is incomplete: 17 of 18 weeks are imported.",
          "ScheduleImport",
        ),
      ],
    }),
  );

  expect(
    within(form).getByText(/finished earlier, but something has changed/i),
  ).toBeVisible();
  expect(
    within(form).getByRole("button", { name: "Start season and open draft" }),
  ).toBeDisabled();
});

test("a refusal from finishing stays in the confirmation and keeps the review", async () => {
  replies[FINISH] = {
    status: 409,
    body: {
      code: "setup.incomplete",
      message:
        "Setup cannot be finished yet: Teams and point values are incomplete.",
    },
  };
  const form = await openReview();
  fireEvent.change(within(form).getByLabelText(/Draft deadline/), {
    target: { value: "2026-10-20T18:00" },
  });
  fireEvent.click(
    within(form).getByRole("button", { name: "Start season and open draft" }),
  );

  fireEvent.click(
    within(await screen.findByRole("alertdialog")).getByRole("button", {
      name: "Confirm and open draft",
    }),
  );

  expect(
    await within(screen.getByRole("alertdialog")).findByRole("alert"),
  ).toHaveTextContent("cannot be finished yet");
  expect(within(form).queryByText(/Setup is finished/)).not.toBeInTheDocument();
  expect(calls.some((call) => call.url.endsWith("/activate"))).toBe(false);
});

test("a failed load shows an error and leaves Back", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 9 }) };
  replies[REVIEW] = { status: 500, body: { title: "Boom" } };
  render(<App />);

  const form = await screen.findByRole("form", { name: "Ready to launch" });

  expect(await within(form).findByRole("alert")).toHaveTextContent("Boom");
  expect(
    within(form).queryByRole("button", { name: "Start season and open draft" }),
  ).not.toBeInTheDocument();
  expect(within(form).getByRole("button", { name: "Back" })).toBeVisible();
});

test("a failed create shows its error in the start card", async () => {
  stubApi("Admin");
  replies["POST /api/admin/seasons"] = {
    status: 400,
    body: { code: "season.name", message: "Season name is required." },
  };
  render(<App />);

  const card = await screen.findByRole("form", { name: "Start a new season" });
  fireEvent.click(within(card).getByRole("button", { name: "Create season" }));

  await waitFor(() =>
    expect(within(card).getByRole("alert")).toHaveTextContent(
      "Season name is required.",
    ),
  );
});

test("the Teams page reviews standings by division and edits only the selected division", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 3 }) };
  replies[TEAMS] = { status: 200, body: catalog(true) };
  render(<App />);

  const form = await screen.findByRole("form", {
    name: "Teams and point values",
  });
  expect(await within(form).findAllByRole("group")).toHaveLength(8);
  const east = within(form).getByRole("group", { name: "AFC East" });
  expect(within(east).queryAllByRole("combobox")).toHaveLength(0);
  expect(
    within(east).getByText("AFC East Team 3 · 3rd · 3 points"),
  ).toBeVisible();
  expect(
    within(east).getByRole("button", { name: "Edit AFC East" }),
  ).toBeVisible();

  fireEvent.click(within(east).getByRole("button", { name: "Edit AFC East" }));

  expect(within(east).getAllByRole("combobox")).toHaveLength(4);
  expect(within(east).getByLabelText("AFC East Team 3")).toHaveValue("3");
  expect(within(east).getByText("Point value 3")).toBeVisible();
  const north = within(form).getByRole("group", { name: "AFC North" });
  expect(within(north).queryAllByRole("combobox")).toHaveLength(0);
  fireEvent.change(within(east).getByLabelText("AFC East Team 3"), {
    target: { value: "1" },
  });
  expect(within(east).getAllByText("Point value 1")).toHaveLength(2);
});

test("an unfinished division is explained without a request and keeps the selections", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 3 }) };
  replies[TEAMS] = { status: 200, body: catalog(true) };
  render(<App />);

  const east = within(await screen.findByRole("group", { name: "AFC East" }));
  fireEvent.click(east.getByRole("button", { name: "Edit AFC East" }));
  fireEvent.change(east.getByLabelText("AFC East Team 2"), {
    target: { value: "1" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save & continue" }));

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "AFC East needs exactly one team at each of 1st, 2nd, 3rd and 4th.",
  );
  expect(east.getByLabelText("AFC East Team 2")).toHaveValue("1");
  expect(east.getByLabelText("AFC East Team 4")).toHaveValue("4");
  expect(putCalls()).toEqual([]);
});

test("a team without a finish is reported before anything is sent", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 3 }) };
  render(<App />);

  fireEvent.click(
    await screen.findByRole("button", { name: "Save & continue" }),
  );

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Choose a finish for every team",
  );
  expect(putCalls()).toEqual([]);
});

test("saving valid finishes sends the complete set, records Schedule import and moves on to it", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 3 }) };
  replies[TEAMS] = { status: 200, body: catalog(true) };
  replies[SAVE_TEAMS] = { status: 200, body: catalog(true) };
  replies[STEP] = stepReply();
  render(<App />);

  fireEvent.click(
    await screen.findByRole("button", { name: "Save & continue" }),
  );

  expect(
    await screen.findByRole("form", { name: "Schedule import" }),
  ).toBeVisible();
  const sent = calls.find(
    (call) => call.method === "PUT" && call.url.endsWith("/teams"),
  )!;
  expect((sent.body as { teams: unknown[] }).teams).toHaveLength(32);
  expect((sent.body as { teams: unknown[] }).teams[0]).toEqual({
    teamId: "AFC East-1",
    finishPosition: 1,
  });
  expect(JSON.stringify(sent.body)).not.toContain("pointValue");
  expect(calls.find((call) => call.url.endsWith("/step"))).toMatchObject({
    body: { step: 4 },
  });
  expect(screen.queryByText(/Coming next/)).not.toBeInTheDocument();
});

test("a server rejection of the finishes stays beside the page and does not record progress", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 3 }) };
  replies[TEAMS] = { status: 200, body: catalog(true) };
  replies[SAVE_TEAMS] = {
    status: 400,
    body: {
      code: "season.teams",
      message: "Submit each of the 32 teams exactly once.",
    },
  };
  render(<App />);

  fireEvent.click(
    await screen.findByRole("button", { name: "Save & continue" }),
  );

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Submit each of the 32 teams exactly once.",
  );
  expect(putCalls()).toEqual(["/teams"]);
});

test("a failed load shows an error instead of offering to create a season", async () => {
  stubApi("Admin");
  replies["GET /api/admin/seasons/setup"] = {
    status: 500,
    body: { title: "Boom" },
  };
  render(<App />);

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Could not load the Setup season: Boom",
  );
  expect(
    screen.queryByRole("form", { name: "Start a new season" }),
  ).not.toBeInTheDocument();
});

test("a network failure on load is shown as an error", async () => {
  stubApi("Admin");
  const api = vi.mocked(fetch).getMockImplementation()!;
  vi.mocked(fetch).mockImplementation(async (url, init) => {
    if (String(url) === "/api/admin/seasons/setup")
      throw new TypeError("offline");
    return api(url, init);
  });
  render(<App />);

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Could not reach the server.",
  );
  expect(
    screen.queryByRole("form", { name: "Start a new season" }),
  ).not.toBeInTheDocument();
});

test("a coach sees no admin controls and the app never asks for the setup season", async () => {
  stubApi("Coach");
  render(<App />);

  expect(
    await screen.findByText(/Signed in as Swan Admin · Coach/),
  ).toBeVisible();
  expect(screen.queryByRole("form")).not.toBeInTheDocument();
  expect(screen.queryByText("Season setup")).not.toBeInTheDocument();
  expect(calls.some((call) => call.url.includes("/api/admin"))).toBe(false);
});

const ACTIVATE = "POST /api/admin/seasons/setup/activate";

test("a deadline is required before the confirmation, and nothing is sent", async () => {
  const form = await openReview();

  fireEvent.click(
    within(form).getByRole("button", { name: "Start season and open draft" }),
  );

  expect(await within(form).findByRole("alert")).toHaveTextContent(
    "Choose the draft deadline",
  );
  expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
  expect(calls.some((call) => call.method === "POST")).toBe(false);
});

test("an already finished setup skips the finish call and goes straight to opening the draft", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 10 }) };
  replies[REVIEW] = { status: 200, body: reviewData({ completed: true }) };
  replies[ACTIVATE] = {
    status: 200,
    body: {
      id: "s",
      state: "Live",
      rosterDraftDeadline: "2026-10-20T22:00:00Z",
      rosterDraftDeadlineEastern: "2026-10-20T18:00",
    },
  };
  render(<App />);
  const form = await screen.findByRole("form", { name: "Ready to launch" });
  fireEvent.change(await within(form).findByLabelText(/Draft deadline/), {
    target: { value: "2026-10-20T18:00" },
  });
  fireEvent.click(
    within(form).getByRole("button", { name: "Start season and open draft" }),
  );
  fireEvent.click(
    within(await screen.findByRole("alertdialog")).getByRole("button", {
      name: "Confirm and open draft",
    }),
  );

  expect(await screen.findByText(/The season is live/)).toBeVisible();
  expect(screen.getByText(/open until Tue, Oct 20, 6:00 PM ET/)).toBeVisible();
  expect(calls.some((call) => call.url.endsWith("/review/finish"))).toBe(false);
  fireEvent.click(screen.getByRole("link", { name: "Open the draft" }));
  await vi.waitFor(() => expect(window.location.pathname).toBe("/draft"));
});

test.each([
  [403, "Only the season's commissioner can start the season."],
  [400, "The roster draft deadline must be in the future."],
  [409, "The season has no active commissioner, so it cannot start."],
])(
  "a %s refusal is shown in the confirmation and the season stays a draft",
  async (status, message) => {
    stubApi("Admin");
    replies[SETUP] = { status: 200, body: season({ setupStep: 10 }) };
    replies[REVIEW] = { status: 200, body: reviewData({ completed: true }) };
    replies[ACTIVATE] = { status, body: { code: "x", message } };
    render(<App />);
    const form = await screen.findByRole("form", { name: "Ready to launch" });
    fireEvent.change(await within(form).findByLabelText(/Draft deadline/), {
      target: { value: "2026-10-20T18:00" },
    });
    fireEvent.click(
      within(form).getByRole("button", { name: "Start season and open draft" }),
    );

    fireEvent.click(
      within(await screen.findByRole("alertdialog")).getByRole("button", {
        name: "Confirm and open draft",
      }),
    );

    expect(
      await within(screen.getByRole("alertdialog")).findByRole("alert"),
    ).toHaveTextContent(message);
    expect(screen.queryByText(/The season is live/)).not.toBeInTheDocument();
    // The commissioner can still cancel and correct the deadline.
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(within(form).getByLabelText(/Draft deadline/)).toHaveValue(
      "2026-10-20T18:00",
    );
  },
);

test("starting a season updates what a commissioner who is only a coach can reach, with no reload", async () => {
  stubApi("Coach");
  const inner = globalThis.fetch;
  let started = false;
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string, init?: RequestInit) => {
      if (url === "/api/me")
        return {
          ok: true,
          status: 200,
          json: async () => ({
            id: "u",
            name: "Pat",
            role: "Coach",
            setupAccess: started ? "none" : "manage",
            commissionsLiveSeason: started,
          }),
        };
      if (url.endsWith("/setup/activate")) started = true;
      if (url === "/api/commissioner/roster-draft")
        return {
          ok: true,
          status: 200,
          json: async () => ({
            seasonId: "s",
            seasonName: "2027 Swan League",
            deadline: null,
            draftOpen: false,
            serverTime: "2026-10-15T12:00:00Z",
            teams: [],
            coaches: [],
          }),
        };
      return inner(url, init);
    }),
  );
  replies[SETUP] = { status: 200, body: season({ setupStep: 10 }) };
  replies[REVIEW] = { status: 200, body: reviewData({ completed: true }) };
  replies[ACTIVATE] = {
    status: 200,
    body: {
      id: "s",
      state: "Live",
      rosterDraftDeadline: "2026-10-20T22:00:00Z",
      rosterDraftDeadlineEastern: "2026-10-20T18:00",
    },
  };
  render(<App />);
  const start = await screen.findByRole("group", {
    name: "Start the season",
  });
  expect(
    within(screen.getByRole("navigation", { name: "Primary" })).queryByRole(
      "link",
      { name: "Draft" },
    ),
  ).not.toBeInTheDocument();

  fireEvent.change(within(start).getByLabelText(/Draft deadline/), {
    target: { value: "2026-10-20T18:00" },
  });
  fireEvent.click(
    within(start).getByRole("button", { name: "Start season and open draft" }),
  );
  fireEvent.click(
    within(await screen.findByRole("alertdialog")).getByRole("button", {
      name: "Confirm and open draft",
    }),
  );
  fireEvent.click(await screen.findByRole("link", { name: "Open the draft" }));

  expect(
    await screen.findByText(/No draft deadline was set for this season/),
  ).toBeVisible();
  expect(
    screen.getByRole("heading", { level: 1, name: "Draft" }),
  ).toBeVisible();
  const nav = within(screen.getByRole("navigation", { name: "Primary" }));
  expect(nav.getByRole("link", { name: "Draft" })).toHaveAttribute(
    "aria-current",
    "page",
  );
  // The Setup season is finished and started, so the Season setup link goes away.
  expect(
    nav.queryByRole("link", { name: "Season setup" }),
  ).not.toBeInTheDocument();
});

test("the page keeps a sound heading order: one h1, then an h2 for the current page", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 4 }) };
  render(<App />);
  await screen.findByRole("form", { name: "Schedule import" });

  expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
  expect(
    screen.getByRole("heading", { level: 2, name: "Schedule import" }),
  ).toBeVisible();
  expect(screen.queryAllByRole("heading", { level: 3 })).toHaveLength(0);
});

test("five setup phases are listed; reached ones are buttons, later ones are plain text", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 4 }) };
  render(<App />);
  await screen.findByRole("form", { name: "Schedule import" });

  const nav = within(screen.getByRole("navigation", { name: "Setup phases" }));
  expect(nav.getAllByRole("listitem")).toHaveLength(5);
  expect(nav.getByRole("button", { name: /League details/ })).toBeVisible();
  expect(nav.getByRole("button", { name: /Competition rules/ })).toBeVisible();
  expect(
    nav.queryByRole("button", { name: /People & payouts/ }),
  ).not.toBeInTheDocument();
  expect(nav.getByText(/Review & launch/)).toBeVisible();
  expect(nav.getByRole("listitem", { current: "step" })).toHaveTextContent(
    "Schedule",
  );
});

test("going back to an earlier milestone offers a clear way to resume, and nothing is saved by moving", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 5 }) };
  render(<App />);
  await screen.findByRole("form", { name: "Weeks and dogs" });
  expect(screen.queryByText(/Next up:/)).not.toBeInTheDocument();

  fireEvent.click(screen.getByRole("button", { name: /League details/ }));

  expect(await screen.findByRole("form", { name: "Basics" })).toBeVisible();
  expect(screen.getByText("Next up: Weeks and dogs")).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "Continue setup" }));
  expect(
    await screen.findByRole("form", { name: "Weeks and dogs" }),
  ).toBeVisible();
  expect(putCalls()).toEqual([]);
});

test("Development tools are not offered unless the server says so", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season() };
  render(<App />);
  await screen.findByRole("form", { name: "Basics" });

  expect(screen.queryByText("Development tools")).not.toBeInTheDocument();
});

const RESET = "POST /api/admin/development/reset-season-data";

test("resetting local season data needs a confirmation, then shows the fresh-season entry", async () => {
  stubApi("Admin", true);
  replies[SETUP] = { status: 200, body: season() };
  replies[RESET] = { status: 200, body: { removed: { seasons: 1 } } };
  render(<App />);
  await screen.findByRole("form", { name: "Basics" });

  const tools = screen.getByText("Development tools").closest("details")!;
  tools.open = true;
  expect(tools).toHaveTextContent("Teams and development accounts remain");
  fireEvent.click(
    within(tools).getByRole("button", { name: "Reset local season data" }),
  );
  const dialog = within(screen.getByRole("alertdialog"));
  // Asking sends nothing.
  expect(calls.some((call) => call.url.includes("development"))).toBe(false);
  fireEvent.click(dialog.getByRole("button", { name: "Reset season data" }));

  expect(
    await screen.findByRole("form", { name: "Start a new season" }),
  ).toBeVisible();
  expect(screen.getByText(/Local season data was reset/)).toBeVisible();
  expect(
    screen.queryByRole("form", { name: "Basics" }),
  ).not.toBeInTheDocument();
  expect(calls.filter((call) => call.url.includes("development"))).toHaveLength(
    1,
  );
  expect(window.location.pathname).toBe("/setup");
});

test("a failed reset says why and leaves the season in place", async () => {
  stubApi("Admin", true);
  replies[SETUP] = { status: 200, body: season() };
  replies[RESET] = { status: 500, body: { title: "Boom" } };
  render(<App />);
  await screen.findByRole("form", { name: "Basics" });
  fireEvent.click(
    screen.getByRole("button", { name: "Reset local season data" }),
  );

  fireEvent.click(
    within(screen.getByRole("alertdialog")).getByRole("button", {
      name: "Reset season data",
    }),
  );

  expect(
    await within(screen.getByRole("alertdialog")).findByRole("alert"),
  ).toHaveTextContent("Boom");
  expect(screen.getByRole("form", { name: "Basics" })).toBeVisible();
});

test("Enter in the deadline field asks for the confirmation without sending anything", async () => {
  const form = await openReview();
  const input = within(form).getByLabelText(/Draft deadline/);
  fireEvent.change(input, { target: { value: "2026-10-20T18:00" } });

  fireEvent.keyDown(input, { key: "Enter" });

  expect(await screen.findByRole("alertdialog")).toBeVisible();
  expect(calls.some((call) => call.method === "POST")).toBe(false);
});

test("after a refused launch focus returns to the confirmation, so Escape still closes it", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 10 }) };
  replies[REVIEW] = { status: 200, body: reviewData({ completed: true }) };
  replies[ACTIVATE] = {
    status: 400,
    body: {
      code: "x",
      message: "The roster draft deadline must be in the future.",
    },
  };
  render(<App />);
  const form = await screen.findByRole("form", { name: "Ready to launch" });
  fireEvent.change(await within(form).findByLabelText(/Draft deadline/), {
    target: { value: "2026-10-20T18:00" },
  });
  fireEvent.click(
    within(form).getByRole("button", { name: "Start season and open draft" }),
  );
  fireEvent.click(
    within(await screen.findByRole("alertdialog")).getByRole("button", {
      name: "Confirm and open draft",
    }),
  );
  const dialog = await screen.findByRole("alertdialog");
  await within(dialog).findByRole("alert");

  await vi.waitFor(() =>
    expect(within(dialog).getByRole("heading", { level: 3 })).toHaveFocus(),
  );
  fireEvent.keyDown(document.activeElement as Element, { key: "Escape" });
  expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
});

test("after a refused reset focus returns to the confirmation", async () => {
  stubApi("Admin", true);
  replies[SETUP] = { status: 200, body: season() };
  replies[RESET] = { status: 500, body: { title: "Boom" } };
  render(<App />);
  await screen.findByRole("form", { name: "Basics" });
  fireEvent.click(
    screen.getByRole("button", { name: "Reset local season data" }),
  );
  const dialog = screen.getByRole("alertdialog");

  fireEvent.click(
    within(dialog).getByRole("button", { name: "Reset season data" }),
  );

  await within(dialog).findByRole("alert");
  await vi.waitFor(() =>
    expect(within(dialog).getByRole("heading", { level: 3 })).toHaveFocus(),
  );
});
