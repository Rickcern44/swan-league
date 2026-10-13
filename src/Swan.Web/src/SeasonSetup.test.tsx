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
  replies = {};
  calls = [];
  window.history.replaceState({}, "", "/");
});

const SETUP = "GET /api/admin/seasons/setup";
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
  expect(screen.getByText("Step 1 of 3")).toBeVisible();
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
  [0, "Basics", "Step 1 of 3"],
  [1, "Money", "Step 2 of 3"],
  [2, "Weekly deadline", "Step 3 of 3"],
  [3, "Weekly deadline", "Step 3 of 3"],
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
  expect(screen.getByText("Step 2 of 3")).toBeVisible();
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

  expect(await screen.findByText("Deadline saved")).toBeVisible();
  expect(calls.find((call) => call.url.endsWith("/deadline"))).toMatchObject({
    body: { teamPickDeadlineEastern: "11:30:00" },
  });
  expect(calls.filter((call) => call.url.endsWith("/step"))[1]).toMatchObject({
    body: { step: 3 },
  });
  expect(screen.getByRole("form", { name: "Weekly deadline" })).toBeVisible();
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
  replies[SETUP] = { status: 200, body: season({ setupStep: 3 }) };
  render(<App />);

  await screen.findByRole("form", { name: "Weekly deadline" });
  const next = screen.getByText(/Coming next/);
  expect(next).toHaveTextContent("Teams and point values");
  expect(next).toHaveTextContent("Schedule import");
  expect(next).toHaveTextContent("Weeks and dogs");
  expect(screen.queryByRole("link")).not.toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: /schedule|teams|weeks/i }),
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
