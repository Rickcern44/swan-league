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

type Me = {
  role: "Admin" | "Coach";
  setupAccess?: "manage" | "create" | "none";
  commissionsLiveSeason?: boolean;
};
type Reply = { status: number; body?: unknown };
let replies: Record<string, Reply>;
let calls: { method: string; url: string; body?: unknown }[];

function open(path: string, me: Me) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? "GET";
      calls.push({
        method,
        url,
        body: init?.body ? JSON.parse(init.body as string) : undefined,
      });
      const reply: Reply =
        url === "/api/me"
          ? { status: 200, body: { id: "u", name: "Pat", ...me } }
          : (replies[`${method} ${url}`] ?? {
              status: 404,
              body: {
                code: "season.missing",
                message: "No season is in Setup.",
              },
            });
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
  replies = {};
  calls = [];
});
afterEach(cleanup);

const nav = () => screen.getByRole("navigation", { name: "Primary" });

test("a coach who commissions the Setup season gets Season setup in the navigation and the setup page itself", async () => {
  open("/setup", { role: "Coach", setupAccess: "manage" });

  expect(
    await screen.findByRole("heading", { level: 1, name: "Season setup" }),
  ).toBeVisible();
  expect(
    within(nav()).getByRole("link", { name: "Season setup" }),
  ).toHaveAttribute("aria-current", "page");
  expect(
    within(nav()).queryByRole("link", { name: "Draft" }),
  ).not.toBeInTheDocument();
  await waitFor(() =>
    expect(calls.some((c) => c.url === "/api/admin/seasons/setup")).toBe(true),
  );
  expect(calls.some((c) => c.url.includes("/api/coach/picks"))).toBe(false);
});

test("a commissioner with no season in Setup is offered the start-a-season form without asking for a Setup season they cannot read", async () => {
  replies["POST /api/admin/seasons"] = {
    status: 201,
    body: {
      id: "s",
      year: 2030,
      name: "2030 Swan League",
      isSimulation: false,
      state: 0,
      setupStep: 0,
      settings: {
        buyIn: { amount: 100, currency: "USD" },
        deadlineEastern: "13:00:00",
        rules: {},
      },
      pools: [],
    },
  };
  open("/setup", { role: "Coach", setupAccess: "create" });

  const form = await screen.findByRole("form", { name: "Start a new season" });
  expect(
    calls.some(
      (c) => c.method === "GET" && c.url === "/api/admin/seasons/setup",
    ),
  ).toBe(false);
  fireEvent.change(within(form).getByLabelText("Season name"), {
    target: { value: "2030 Swan League" },
  });
  fireEvent.click(within(form).getByRole("button", { name: "Create season" }));

  await vi.waitFor(() =>
    expect(
      calls.some((c) => c.method === "POST" && c.url === "/api/admin/seasons"),
    ).toBe(true),
  );
});

test("a coach with no commissioner role sees no Season setup, and /setup is just their pick desk", async () => {
  open("/setup", { role: "Coach", setupAccess: "none" });

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Could not load your picks.",
  );
  expect(
    within(nav()).queryByRole("link", { name: "Season setup" }),
  ).not.toBeInTheDocument();
  expect(
    within(nav()).queryByRole("link", { name: "Draft" }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole("heading", { name: "Season setup" }),
  ).not.toBeInTheDocument();
  expect(
    calls.some(
      (c) =>
        c.url.includes("/api/admin") || c.url.includes("/api/commissioner"),
    ),
  ).toBe(false);
});

test("a coach who commissions the live season reaches the commissioner roster view and nothing else of the admin area", async () => {
  replies["GET /api/commissioner/roster-draft"] = {
    status: 200,
    body: {
      seasonId: "s",
      seasonName: "2026 Swan League",
      deadline: null,
      draftOpen: false,
      serverTime: "2026-10-15T12:00:00Z",
      teams: [],
      coaches: [],
    },
  };
  open("/draft", {
    role: "Coach",
    setupAccess: "none",
    commissionsLiveSeason: true,
  });

  // Wait for the loaded page (the loading state has the same heading and is replaced).
  await screen.findByText(/No draft deadline was set for this season/);
  expect(
    screen.getByRole("heading", { level: 1, name: "Draft" }),
  ).toBeVisible();
  expect(within(nav()).getByRole("link", { name: "Draft" })).toHaveAttribute(
    "aria-current",
    "page",
  );
  expect(within(nav()).getByRole("link", { name: "Roster" })).toBeVisible();
  expect(
    within(nav()).queryByRole("link", { name: "Season setup" }),
  ).not.toBeInTheDocument();
  expect(calls.some((c) => c.url.includes("/api/admin"))).toBe(false);
});

test("a coach who is not a commissioner cannot reach the commissioner roster view by address", async () => {
  open("/draft", {
    role: "Coach",
    setupAccess: "none",
    commissionsLiveSeason: false,
  });

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Could not load your picks.",
  );
  expect(calls.some((c) => c.url.includes("/api/commissioner"))).toBe(false);
});

test("an administrator keeps both links and both pages whatever the access flags say", async () => {
  open("/setup", { role: "Admin" });

  expect(
    await screen.findByRole("heading", { level: 1, name: "Season setup" }),
  ).toBeVisible();
  expect(within(nav()).getByRole("link", { name: "Draft" })).toBeVisible();
  expect(
    within(nav()).getByRole("link", { name: "Season setup" }),
  ).toBeVisible();
});
