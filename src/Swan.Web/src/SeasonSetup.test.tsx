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

type Reply = { status: number; body?: unknown };
let replies: Record<
  string,
  Reply | ((body: { step: number; weeks?: number[] }) => Reply)
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

function stubApi(role: "Admin" | "Coach") {
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
          ? { status: 200, body: { id: "u", name: "Swan Admin", role } }
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
    [SCHEDULE]: {
      status: 200,
      body: plan({ imported: false, fingerprint: null, weeks: [] }),
    },
  };
  calls = [];
  window.history.replaceState({}, "", "/");
});

const SETUP = "GET /api/admin/seasons/setup";
const TEAMS = "GET /api/admin/seasons/setup/teams";
const SCHEDULE = "GET /api/admin/seasons/setup/schedule";
const PREVIEW = "POST /api/admin/seasons/setup/schedule/preview";
const IMPORT = "POST /api/admin/seasons/setup/schedule/import";
const DOGS = "GET /api/admin/seasons/setup/double-dog-weeks";
const SAVE_DOGS = "PUT /api/admin/seasons/setup/double-dog-weeks";
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
  expect(screen.getByText("Step 1 of 6")).toBeVisible();
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
  [0, "Basics", "Step 1 of 6"],
  [1, "Money", "Step 2 of 6"],
  [2, "Weekly deadline", "Step 3 of 6"],
  [3, "Teams and point values", "Step 4 of 6"],
  [4, "Schedule import", "Step 5 of 6"],
  [5, "Weeks and dogs", "Step 6 of 6"],
  [6, "Weeks and dogs", "Step 6 of 6"],
])(
  "a fresh load resumes at the saved step %i",
  async (setupStep, form, position) => {
    stubApi("Admin");
    replies[SETUP] = { status: 200, body: season({ setupStep }) };
    render(<App />);

    expect(await screen.findByRole("form", { name: form })).toBeVisible();
    expect(screen.getAllByRole("form")).toHaveLength(1);
    expect(screen.getByText(position)).toBeVisible();
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
  expect(screen.getByText("Step 2 of 6")).toBeVisible();
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

test("later steps are listed as upcoming and cannot be opened", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 6 }) };
  render(<App />);

  await screen.findByRole("form", { name: "Weeks and dogs" });
  const next = screen.getByText(/Coming next/);
  expect(next).not.toHaveTextContent("Weeks and dogs");
  expect(next).toHaveTextContent("Fines and payouts");
  expect(screen.queryByRole("link")).not.toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: /fines|payouts/i }),
  ).not.toBeInTheDocument();
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

  expect(await within(form).findByText("Week 1")).toBeVisible();
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
  expect(screen.getByText(/Coming next/)).toHaveTextContent(
    "Fines and payouts",
  );
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

test("saving sends the chosen weeks, records the next step and keeps the page", async () => {
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

  expect(await within(form).findByText("Double-dog weeks saved")).toBeVisible();
  expect(
    calls.find(
      (call) => call.method === "PUT" && call.url.endsWith("/double-dog-weeks"),
    ),
  ).toMatchObject({ body: { weeks: [2, 5, 12, 17] } });
  expect(calls.find((call) => call.url.endsWith("/step"))).toMatchObject({
    body: { step: 6 },
  });
  expect(screen.getByText(/Coming next/)).toHaveTextContent(
    "Fines and payouts",
  );
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

test("the Teams page lists every team by division with its derived point value", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 3 }) };
  replies[TEAMS] = { status: 200, body: catalog(true) };
  render(<App />);

  const form = await screen.findByRole("form", {
    name: "Teams and point values",
  });
  expect(within(form).getAllByRole("group")).toHaveLength(8);
  const east = within(form).getByRole("group", { name: "AFC East" });
  expect(within(east).getAllByRole("combobox")).toHaveLength(4);
  expect(within(east).getByLabelText("AFC East Team 3")).toHaveValue("3");
  expect(within(east).getByText("Point value 3")).toBeVisible();
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
  expect(screen.getByText(/Coming next/)).toHaveTextContent(
    "Fines and payouts",
  );
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
