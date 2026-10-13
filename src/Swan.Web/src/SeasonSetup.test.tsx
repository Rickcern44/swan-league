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

type Reply = { status: number; body?: unknown };
let replies: Record<string, Reply | ((body: { step: number }) => Reply)>;
let calls: { method: string; url: string; body?: unknown }[];

function resolve(
  reply: Reply | ((body: { step: number }) => Reply) | undefined,
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
  replies = { [TEAMS]: { status: 200, body: catalog() } };
  calls = [];
  window.history.replaceState({}, "", "/");
});

const SETUP = "GET /api/admin/seasons/setup";
const TEAMS = "GET /api/admin/seasons/setup/teams";
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
  expect(screen.getByText("Step 1 of 4")).toBeVisible();
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
  [0, "Basics", "Step 1 of 4"],
  [1, "Money", "Step 2 of 4"],
  [2, "Weekly deadline", "Step 3 of 4"],
  [3, "Teams and point values", "Step 4 of 4"],
  [4, "Teams and point values", "Step 4 of 4"],
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
  expect(screen.getByText("Step 2 of 4")).toBeVisible();
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
  replies[SETUP] = { status: 200, body: season({ setupStep: 4 }) };
  render(<App />);

  await screen.findByRole("form", { name: "Teams and point values" });
  const next = screen.getByText(/Coming next/);
  expect(next).not.toHaveTextContent("Teams and point values");
  expect(next).toHaveTextContent("Schedule import");
  expect(next).toHaveTextContent("Weeks and dogs");
  expect(screen.queryByRole("link")).not.toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: /schedule|weeks/i }),
  ).not.toBeInTheDocument();
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

test("saving valid finishes sends the complete set, records Schedule import and keeps Teams on screen", async () => {
  stubApi("Admin");
  replies[SETUP] = { status: 200, body: season({ setupStep: 3 }) };
  replies[TEAMS] = { status: 200, body: catalog(true) };
  replies[SAVE_TEAMS] = { status: 200, body: catalog(true) };
  replies[STEP] = stepReply();
  render(<App />);

  fireEvent.click(
    await screen.findByRole("button", { name: "Save & continue" }),
  );

  expect(await screen.findByText("Teams saved")).toBeVisible();
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
  expect(
    screen.getByRole("form", { name: "Teams and point values" }),
  ).toBeVisible();
  expect(screen.getByText(/Coming next/)).toHaveTextContent("Schedule import");
  expect(
    screen.queryByRole("form", { name: /schedule/i }),
  ).not.toBeInTheDocument();
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
