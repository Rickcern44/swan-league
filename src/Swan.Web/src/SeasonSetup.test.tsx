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
  pools: [
    { name: "Main", stake: money(60) },
    { name: "Dog", stake: money(20) },
    { name: "Regular Season", stake: money(10) },
    { name: "Playoff", stake: money(10) },
  ],
  ...overrides,
});

type Reply = { status: number; body?: unknown };
let replies: Record<string, Reply>;
let calls: { method: string; url: string; body?: unknown }[];

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
          : (replies[`${method} ${url}`] ?? { status: 404 });
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

test("an admin with no Setup season can start one", async () => {
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
  expect(screen.getByLabelText("Season name")).toHaveValue("2027 Swan League");
  expect(
    calls.find(
      (call) => call.method === "POST" && call.url.endsWith("seasons"),
    ),
  ).toMatchObject({
    body: { year: 2027, name: "2027 Swan League", isSimulation: true },
  });
  expect(
    screen.queryByRole("form", { name: "Start a new season" }),
  ).not.toBeInTheDocument();
});

test("an admin sees the Setup season populated in three cards", async () => {
  stubApi("Admin");
  replies["GET /api/admin/seasons/setup"] = { status: 200, body: season() };
  render(<App />);

  expect(await screen.findByRole("form", { name: "Basics" })).toBeVisible();
  expect(screen.getByLabelText("Season year")).toHaveValue(2026);
  expect(screen.getByLabelText("Season name")).toHaveValue("2026 Season");
  expect(screen.getByLabelText("Buy-in")).toHaveValue(100);
  expect(screen.getByLabelText("Main")).toHaveValue(60);
  expect(screen.getByLabelText("Dog")).toHaveValue(20);
  expect(screen.getByLabelText("Regular Season")).toHaveValue(10);
  expect(screen.getByLabelText("Playoff")).toHaveValue(10);
  expect(screen.getByText("Pool total $100.00")).toBeVisible();
  expect(screen.getByLabelText("Team picks lock Sundays at")).toHaveValue(
    "13:00",
  );
  expect(screen.queryByText("Admin", { exact: true })).not.toBeInTheDocument();
});

test("the pool total follows the stakes as they are typed", async () => {
  stubApi("Admin");
  replies["GET /api/admin/seasons/setup"] = { status: 200, body: season() };
  render(<App />);

  fireEvent.change(await screen.findByLabelText("Playoff"), {
    target: { value: "15.50" },
  });

  expect(screen.getByText("Pool total $105.50")).toBeVisible();
});

test("saving a card sends its API payload, shows the reply, and keeps unsaved edits in other cards", async () => {
  stubApi("Admin");
  replies["GET /api/admin/seasons/setup"] = { status: 200, body: season() };
  replies["PUT /api/admin/seasons/setup/money"] = {
    status: 200,
    body: season({
      settings: {
        buyIn: money(150),
        doubleDogWeeks: 3,
        teamPickDeadlineEastern: "13:00:00",
      },
      pools: [
        { name: "Main", stake: money(90) },
        { name: "Dog", stake: money(30) },
        { name: "Regular Season", stake: money(15) },
        { name: "Playoff", stake: money(15) },
      ],
    }),
  };
  render(<App />);
  fireEvent.change(await screen.findByLabelText("Season name"), {
    target: { value: "Unsaved name" },
  });
  for (const [label, value] of [
    ["Buy-in", "150"],
    ["Main", "90"],
    ["Dog", "30"],
    ["Regular Season", "15"],
    ["Playoff", "15"],
  ])
    fireEvent.change(await screen.findByLabelText(label), {
      target: { value },
    });
  fireEvent.click(screen.getByRole("button", { name: "Save money" }));

  expect(await screen.findByText("Money saved")).toBeVisible();
  expect(
    calls.find((call) => call.url === "/api/admin/seasons/setup/money"),
  ).toMatchObject({
    method: "PUT",
    body: { buyIn: 150, main: 90, dog: 30, regularSeason: 15, playoff: 15 },
  });
  expect(screen.getByLabelText("Season name")).toHaveValue("Unsaved name");
});

test("saving basics and the deadline use their own endpoints", async () => {
  stubApi("Admin");
  replies["GET /api/admin/seasons/setup"] = { status: 200, body: season() };
  replies["PUT /api/admin/seasons/setup"] = {
    status: 200,
    body: season({ year: 2027, isSimulation: true }),
  };
  replies["PUT /api/admin/seasons/setup/deadline"] = {
    status: 200,
    body: season({
      settings: {
        buyIn: money(100),
        doubleDogWeeks: 3,
        teamPickDeadlineEastern: "11:30:00",
      },
    }),
  };
  render(<App />);

  fireEvent.change(await screen.findByLabelText("Season year"), {
    target: { value: "2027" },
  });
  fireEvent.click(screen.getByLabelText("Simulation season"));
  fireEvent.click(screen.getByRole("button", { name: "Save basics" }));
  expect(await screen.findByText("Basics saved")).toBeVisible();
  fireEvent.change(screen.getByLabelText("Team picks lock Sundays at"), {
    target: { value: "11:30" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save deadline" }));
  expect(await screen.findByText("Deadline saved")).toBeVisible();

  expect(
    calls.find(
      (call) =>
        call.url === "/api/admin/seasons/setup" && call.method === "PUT",
    ),
  ).toMatchObject({
    body: { year: 2027, name: "2026 Season", isSimulation: true },
  });
  expect(calls.find((call) => call.url.endsWith("/deadline"))).toMatchObject({
    method: "PUT",
    body: { teamPickDeadlineEastern: "11:30:00" },
  });
  expect(screen.getByLabelText("Team picks lock Sundays at")).toHaveValue(
    "11:30",
  );
});

test("an API error shows beside its card and keeps what was typed", async () => {
  stubApi("Admin");
  replies["GET /api/admin/seasons/setup"] = { status: 200, body: season() };
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
  fireEvent.change(screen.getByLabelText("Season name"), {
    target: { value: "Unsaved name" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save money" }));

  const money_ = screen.getByRole("form", { name: "Money" });
  expect(await within(money_).findByRole("alert")).toHaveTextContent(
    "Pool stakes must add up to the buy-in.",
  );
  expect(screen.getByLabelText("Playoff")).toHaveValue(5);
  expect(screen.getByLabelText("Season name")).toHaveValue("Unsaved name");
  expect(
    within(screen.getByRole("form", { name: "Basics" })).queryByRole("alert"),
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
