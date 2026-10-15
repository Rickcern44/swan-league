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

const SEASON = "22222222-2222-2222-2222-222222222222";
const id = (n: number) =>
  `00000000-0000-0000-0000-0000000000${String(n).padStart(2, "0")}`;
const catalog = Array.from({ length: 12 }, (_, i) => ({
  teamId: id(i + 1),
  name: `Team ${i + 1}`,
  division: "AFC East",
}));

type Status = "missing" | "incomplete" | "complete" | "locked";
const coach = (
  n: number,
  name: string,
  status: Status,
  teamNumbers: number[] = [],
  revision = 0,
) => ({
  membershipId: `m${n}`,
  name,
  email: `${name.toLowerCase().replace(" ", ".")}@example.com`,
  status,
  teamCount: teamNumbers.length,
  teamIds: teamNumbers.map(id),
  teams: teamNumbers.map((t) => `Team ${t}`),
  isLocked: status === "locked",
  lockedAt: null,
  revision,
});

function draft(
  coaches: ReturnType<typeof coach>[],
  over: Record<string, unknown> = {},
) {
  return {
    seasonId: SEASON,
    seasonName: "2026 Swan League",
    deadline: "2026-10-18T12:00:00Z",
    draftOpen: true,
    serverTime: "2026-10-15T12:00:00Z",
    teams: catalog,
    coaches,
    ...over,
  };
}

type Reply = { status: number; body?: unknown } | "network-error";
let current: ReturnType<typeof draft>;
let replies: Record<string, Reply | (() => Reply)>;
let calls: { method: string; url: string; body?: Record<string, unknown> }[];

function stubApi(role: "Admin" | "Coach" = "Admin") {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? "GET";
      const body = init?.body ? JSON.parse(init.body as string) : undefined;
      calls.push({ method, url, body });
      let reply: Reply;
      if (url === "/api/me")
        reply = { status: 200, body: { id: "u", name: "Swan Admin", role } };
      else {
        const configured = replies[`${method} ${url}`];
        reply =
          typeof configured === "function"
            ? configured()
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

function open(
  d = draft([
    coach(1, "Coach A", "missing"),
    coach(2, "Coach B", "incomplete", [1, 2, 3], 2),
    coach(3, "Coach C", "locked", [1, 2, 3, 4, 5, 6, 7, 8, 9], 3),
  ]),
) {
  current = d;
  replies["GET /api/commissioner/roster-draft"] = () => ({
    status: 200,
    body: current,
  });
  stubApi();
  window.history.replaceState({}, "", "/draft");
  return render(<App />);
}

beforeEach(() => {
  replies = {};
  calls = [];
});
afterEach(cleanup);

const ready = () =>
  screen.findByRole("heading", { level: 1, name: "Roster draft" });
const rows = () =>
  within(screen.getByRole("region", { name: "Coaches" })).getAllByRole(
    "listitem",
  );

test("the commissioner sees the deadline, a count by state and every coach with missing and incomplete rosters first", async () => {
  open();
  await ready();

  await screen.findByText(
    /Coaches can choose and lock nine teams until Sun, Oct 18, 8:00 AM ET/,
  );
  expect(screen.getByText(/3d 0h left/)).toBeVisible();
  expect(
    screen.getByText("1 missing · 1 incomplete · 0 complete · 1 locked"),
  ).toBeVisible();
  expect(rows().map((row) => row.querySelector(".teams")?.textContent)).toEqual(
    ["Coach A", "Coach B", "Coach C"],
  );
  expect(rows()[0]).toHaveTextContent("No roster yet");
  expect(rows()[0]).toHaveTextContent("still drafting");
  expect(rows()[1]).toHaveTextContent("Incomplete · 3 of 9 teams");
  expect(rows()[1]).toHaveTextContent("Team 1, Team 2, Team 3");
  expect(rows()[2]).toHaveTextContent("Locked · 9 of 9 teams");
});

test("after the deadline the same view says the draft closed and what needs correcting", async () => {
  open(draft([coach(1, "Coach A", "missing")], { draftOpen: false }));
  await ready();

  await screen.findByText(
    /The draft closed Sun, Oct 18, 8:00 AM ET\. Only you can change a roster now\./,
  );
  expect(rows()[0]).toHaveTextContent("needs your correction");
});

test("a correction replaces one team for a reason and shows the coach's updated roster", async () => {
  open();
  replies["POST /api/commissioner/roster-draft/corrections"] = () => {
    current = draft([
      coach(1, "Coach A", "missing"),
      coach(2, "Coach B", "incomplete", [1, 2, 3], 2),
      coach(3, "Coach C", "locked", [10, 2, 3, 4, 5, 6, 7, 8, 9], 4),
    ]);
    return { status: 200, body: current };
  };
  await ready();
  await screen.findByRole("region", { name: "Coaches" });

  fireEvent.click(
    within(rows()[2]).getByRole("button", {
      name: "Correct roster for Coach C",
    }),
  );
  const form = screen.getByRole("form", { name: "Correct Coach C's roster" });
  fireEvent.change(within(form).getByLabelText(/^Replace/), {
    target: { value: id(1) },
  });
  fireEvent.change(within(form).getByLabelText("With"), {
    target: { value: id(10) },
  });
  fireEvent.change(within(form).getByLabelText(/^Reason/), {
    target: { value: "  Wrong team picked  " },
  });
  fireEvent.click(
    within(form).getByRole("button", { name: "Save correction" }),
  );

  expect(await screen.findByText("Corrected Coach C's roster.")).toBeVisible();
  expect(calls.find((c) => c.method === "POST")).toMatchObject({
    url: "/api/commissioner/roster-draft/corrections",
    body: {
      seasonId: SEASON,
      membershipId: "m3",
      expectedRevision: 3,
      removeTeamId: id(1),
      addTeamId: id(10),
      reason: "Wrong team picked",
    },
  });
  expect(rows()[2]).toHaveTextContent("Team 10");
  expect(screen.queryByRole("form")).not.toBeInTheDocument();
});

test("a short roster is completed by adding a team with nothing to replace", async () => {
  open();
  replies["POST /api/commissioner/roster-draft/corrections"] = () => ({
    status: 200,
    body: current,
  });
  await ready();
  await screen.findByRole("region", { name: "Coaches" });

  fireEvent.click(
    within(rows()[1]).getByRole("button", {
      name: "Correct roster for Coach B",
    }),
  );
  const form = screen.getByRole("form", { name: "Correct Coach B's roster" });
  expect(
    within(form).getByText(/Leave blank to add a team to this short roster/),
  ).toBeVisible();
  fireEvent.change(within(form).getByLabelText("With"), {
    target: { value: id(7) },
  });
  fireEvent.change(within(form).getByLabelText(/^Reason/), {
    target: { value: "Never finished" },
  });
  fireEvent.click(
    within(form).getByRole("button", { name: "Save correction" }),
  );

  await waitFor(() =>
    expect(calls.some((c) => c.method === "POST")).toBe(true),
  );
  expect(calls.find((c) => c.method === "POST")?.body).toMatchObject({
    removeTeamId: null,
    addTeamId: id(7),
    expectedRevision: 2,
  });
});

test("a full roster must name the team to replace, a team to add and a reason, before anything is sent", async () => {
  open();
  await ready();
  await screen.findByRole("region", { name: "Coaches" });
  fireEvent.click(
    within(rows()[2]).getByRole("button", {
      name: "Correct roster for Coach C",
    }),
  );
  const form = screen.getByRole("form", { name: "Correct Coach C's roster" });

  fireEvent.click(
    within(form).getByRole("button", { name: "Save correction" }),
  );
  expect(await within(form).findByRole("alert")).toHaveTextContent(
    "Choose which team to replace",
  );
  fireEvent.change(within(form).getByLabelText(/^Replace/), {
    target: { value: id(1) },
  });
  fireEvent.click(
    within(form).getByRole("button", { name: "Save correction" }),
  );
  expect(within(form).getByRole("alert")).toHaveTextContent(
    "Choose the team to add",
  );
  fireEvent.change(within(form).getByLabelText("With"), {
    target: { value: id(10) },
  });
  fireEvent.click(
    within(form).getByRole("button", { name: "Save correction" }),
  );
  expect(within(form).getByRole("alert")).toHaveTextContent("Give a reason");
  expect(calls.some((c) => c.method === "POST")).toBe(false);
});

test("a server refusal is shown beside the form and a stale one offers a reload", async () => {
  open();
  replies["POST /api/commissioner/roster-draft/corrections"] = {
    status: 409,
    body: {
      code: "roster.stale",
      message:
        "That roster changed since this page loaded. Reload to see the saved one.",
    },
  };
  await ready();
  await screen.findByRole("region", { name: "Coaches" });
  fireEvent.click(
    within(rows()[1]).getByRole("button", {
      name: "Correct roster for Coach B",
    }),
  );
  const form = screen.getByRole("form", { name: "Correct Coach B's roster" });
  fireEvent.change(within(form).getByLabelText("With"), {
    target: { value: id(7) },
  });
  fireEvent.change(within(form).getByLabelText(/^Reason/), {
    target: { value: "Because" },
  });

  fireEvent.click(
    within(form).getByRole("button", { name: "Save correction" }),
  );

  expect(await within(form).findByRole("alert")).toHaveTextContent(
    "changed since this page loaded",
  );
  expect(within(form).getByLabelText(/^Reason/)).toHaveValue("Because");
  fireEvent.click(within(form).getByRole("button", { name: "Reload rosters" }));
  await waitFor(() =>
    expect(screen.queryByRole("form")).not.toBeInTheDocument(),
  );
});

test("Escape closes the correction form without sending anything", async () => {
  open();
  await ready();
  await screen.findByRole("region", { name: "Coaches" });
  fireEvent.click(
    within(rows()[0]).getByRole("button", {
      name: "Correct roster for Coach A",
    }),
  );

  fireEvent.keyDown(
    screen.getByRole("form", { name: "Correct Coach A's roster" }),
    { key: "Escape" },
  );

  expect(screen.queryByRole("form")).not.toBeInTheDocument();
  expect(calls.some((c) => c.method === "POST")).toBe(false);
});

test("an administrator who is not this season's commissioner is told so and shown no controls", async () => {
  replies["GET /api/commissioner/roster-draft"] = {
    status: 403,
    body: {
      code: "commissioner.required",
      message: "Only the season's commissioner can manage its rosters.",
    },
  };
  stubApi();
  window.history.replaceState({}, "", "/draft");
  render(<App />);

  expect(
    await screen.findByRole("heading", {
      name: "Only the commissioner can manage rosters",
    }),
  ).toBeVisible();
  expect(
    screen.getByText(/Being an administrator is not enough/),
  ).toBeVisible();
  expect(
    screen.queryByRole("button", { name: /Correct roster/ }),
  ).not.toBeInTheDocument();
});

test("before any season is live the page points at Season setup", async () => {
  replies["GET /api/commissioner/roster-draft"] = {
    status: 404,
    body: { code: "season.missing", message: "No season is live." },
  };
  stubApi();
  window.history.replaceState({}, "", "/draft");
  render(<App />);

  expect(
    await screen.findByRole("heading", { name: "No season is live yet" }),
  ).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "Go to Season setup" }));
  expect(window.location.pathname).toBe("/setup");
});

test("a failed load can be retried and the page has one h1", async () => {
  let attempts = 0;
  replies["GET /api/commissioner/roster-draft"] = () =>
    ++attempts === 1
      ? { status: 500 }
      : { status: 200, body: draft([coach(1, "Coach A", "missing")]) };
  stubApi();
  window.history.replaceState({}, "", "/draft");
  render(<App />);

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "The server answered 500.",
  );
  fireEvent.click(screen.getByRole("button", { name: "Try again" }));
  await screen.findByRole("region", { name: "Coaches" });
  expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
});

test("the admin navigation links to the roster draft and marks it current", async () => {
  open();
  await ready();

  expect(screen.getByRole("link", { name: "Roster draft" })).toHaveAttribute(
    "aria-current",
    "page",
  );
  expect(screen.getByRole("link", { name: "Home" })).not.toHaveAttribute(
    "aria-current",
  );
});

test("a team cannot be offered as its own replacement", async () => {
  open();
  await ready();
  await screen.findByRole("region", { name: "Coaches" });
  fireEvent.click(
    within(rows()[2]).getByRole("button", {
      name: "Correct roster for Coach C",
    }),
  );
  const form = screen.getByRole("form", { name: "Correct Coach C's roster" });

  fireEvent.change(within(form).getByLabelText(/^Replace/), {
    target: { value: id(1) },
  });

  const offered = within(within(form).getByLabelText("With"))
    .getAllByRole("option")
    .map((option) => option.textContent);
  expect(offered).toEqual([
    "Choose a team to add",
    "Team 10",
    "Team 11",
    "Team 12",
  ]);
});

test("when a correction also drops a ballot pick of the removed team the commissioner is told", async () => {
  open();
  replies["POST /api/commissioner/roster-draft/corrections"] = () => ({
    status: 200,
    body: { ...current, picksRemoved: 1 },
  });
  await ready();
  await screen.findByRole("region", { name: "Coaches" });
  fireEvent.click(
    within(rows()[2]).getByRole("button", {
      name: "Correct roster for Coach C",
    }),
  );
  const form = screen.getByRole("form", { name: "Correct Coach C's roster" });
  fireEvent.change(within(form).getByLabelText(/^Replace/), {
    target: { value: id(1) },
  });
  fireEvent.change(within(form).getByLabelText("With"), {
    target: { value: id(10) },
  });
  fireEvent.change(within(form).getByLabelText(/^Reason/), {
    target: { value: "Wrong team" },
  });
  fireEvent.click(
    within(form).getByRole("button", { name: "Save correction" }),
  );

  expect(
    await screen.findByText(
      "Corrected Coach C's roster. Their pick of the removed team was also taken off their ballot.",
    ),
  ).toBeVisible();
});
