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
const G1 = "aaaaaaaa-0000-0000-0000-000000000001";
const G2 = "aaaaaaaa-0000-0000-0000-000000000002";
const G3 = "aaaaaaaa-0000-0000-0000-000000000003";

type Game = {
  id: string;
  homeTeamId: string;
  awayTeamId: string;
  homeTeamName: string;
  awayTeamName: string;
  kickoff: string;
  excluded: boolean;
  approvedLine: number | null;
  lineHistory: number[];
  hasResult: boolean;
  homeScore: number | null;
  awayScore: number | null;
};

const game = (id: string, over: Partial<Game> = {}): Game => ({
  id,
  homeTeamId: "h" + id,
  awayTeamId: "a" + id,
  homeTeamName: "Buffalo Bills",
  awayTeamName: "Baltimore Ravens",
  kickoff: "2026-09-10T20:15:00Z",
  excluded: false,
  approvedLine: null,
  lineHistory: [],
  hasResult: false,
  homeScore: null,
  awayScore: null,
  ...over,
});

function week(state: string, games: Game[]) {
  const included = games.filter((g) => !g.excluded);
  const lines = included.filter((g) => g.approvedLine !== null).length;
  const results = included.filter((g) => g.hasResult).length;
  const missingLines = included.length - lines;
  const missingResults = included.length - results;
  let next = {
    kind: "recompute",
    label: "Recompute scores",
    detail: "The week is final.",
    canAct: true,
  };
  if (state === "Draft")
    next =
      missingLines > 0
        ? {
            kind: "add-lines",
            label: "Add lines",
            detail: `${missingLines} of ${included.length} included ${included.length === 1 ? "game still needs" : "games still need"} an approved line.`,
            canAct: false,
          }
        : {
            kind: "approve-lines",
            label: `Approve ${included.length} lines`,
            detail: "Freezes the lines.",
            canAct: true,
          };
  if (state === "LinesApproved")
    next = {
      kind: "publish",
      label: "Publish week",
      detail: "Opens the week for coach picks.",
      canAct: true,
    };
  if (state === "Open")
    next = {
      kind: "lock",
      label: "Lock week",
      detail: "Closes picks.",
      canAct: true,
    };
  if (state === "Locked")
    next =
      missingResults > 0
        ? {
            kind: "enter-results",
            label: "Enter results",
            detail: `${missingResults} of ${included.length} included games still need a result.`,
            canAct: false,
          }
        : {
            kind: "finalize",
            label: "Finalize week",
            detail: "Scores the week.",
            canAct: true,
          };
  return {
    id: WEEK,
    seasonId: "s",
    number: 4,
    kind: "Regular",
    state,
    games,
    includedGames: included.length,
    linesApproved: lines,
    resultsConfirmed: results,
    nextAction: next,
    dogOptionTeamIds: [],
  };
}

type Reply = { status: number; body?: unknown } | "network-error";
let server: ReturnType<typeof week>;
let replies: Record<string, Reply | (() => Reply)>;
let calls: { method: string; url: string; body?: unknown }[];

function stubApi(role: "Admin" | "Coach" = "Admin") {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? "GET";
      calls.push({
        method,
        url,
        body: init?.body ? JSON.parse(init.body as string) : undefined,
      });
      let reply: Reply;
      if (url === "/api/me")
        reply = { status: 200, body: { id: "u", name: "Swan Admin", role } };
      else {
        const key = `${method} ${url.replace(`/api/admin/weeks/${WEEK}`, "")}`;
        const configured = replies[key];
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

function open(state: string, games: Game[]) {
  server = week(state, games);
  replies["GET "] = () => ({ status: 200, body: server });
  stubApi();
  window.history.replaceState({}, "", `/weeks/${WEEK}`);
  return render(<App />);
}

beforeEach(() => {
  replies = {};
  calls = [];
});
afterEach(cleanup);

test("a draft week shows its games in order with kickoff, team names and what is missing", async () => {
  open("Draft", [
    game(G1, { approvedLine: -3.5, lineHistory: [-3.5] }),
    game(G2, {
      lineHistory: [2],
      homeTeamName: "Chicago Bears",
      awayTeamName: "Detroit Lions",
    }),
    game(G3),
  ]);

  expect(
    await screen.findByRole("heading", { level: 1, name: "Week 4" }),
  ).toBeVisible();
  expect(screen.getByText("Regular season")).toBeVisible();
  const rows = within(
    screen.getByRole("region", { name: "Games" }),
  ).getAllByRole("listitem");
  expect(rows).toHaveLength(3);
  expect(rows[0]).toHaveTextContent("Bills -3.5 · approved");
  expect(rows[1]).toHaveTextContent("Bears +2 · awaiting approval");
  expect(rows[2]).toHaveTextContent("No line yet");
  expect(rows[0]).toHaveTextContent("Thu 4:15 PM");
  const dock = screen.getByRole("region", { name: "Next action" });
  expect(dock).toHaveTextContent(
    "2 of 3 included games still need an approved line.",
  );
  expect(within(dock).queryByRole("button")).not.toBeInTheDocument();
  const pulse = screen.getByRole("complementary", { name: "Week pulse" });
  expect(pulse).toHaveTextContent("1 / 3");
  expect(
    within(pulse).getByRole("listitem", { current: "step" }),
  ).toHaveTextContent("Draft · current");
});

test("saving a line and approving it are two explicit choices sent to the line endpoint", async () => {
  open("Draft", [game(G1)]);
  replies[`POST /games/${G1}/lines`] = () => {
    server = week("Draft", [
      game(G1, { approvedLine: -3.5, lineHistory: [-3.5] }),
    ]);
    return { status: 200, body: server };
  };
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  fireEvent.click(screen.getByRole("button", { name: /Add line/ }));
  const form = screen.getByRole("form", {
    name: "Line for Baltimore Ravens at Buffalo Bills",
  });
  fireEvent.change(within(form).getByLabelText(/Spread for Bills/), {
    target: { value: "-3.5" },
  });
  fireEvent.click(
    within(form).getByRole("button", { name: "Save and approve" }),
  );

  expect(await screen.findByText("Bills -3.5 · approved")).toBeVisible();
  expect(calls.find((c) => c.method === "POST")).toMatchObject({
    url: `/api/admin/weeks/${WEEK}/games/${G1}/lines`,
    body: { spread: -3.5, approve: true },
  });
  expect(screen.getByRole("status")).toHaveTextContent(
    "Line saved and approved.",
  );
  expect(screen.queryByRole("form")).not.toBeInTheDocument();
  await waitFor(() =>
    expect(screen.getByRole("button", { name: /Edit line/ })).toHaveFocus(),
  );
});

test("Save line without approving leaves it awaiting approval and approve-latest finishes it", async () => {
  open("Draft", [game(G1)]);
  replies[`POST /games/${G1}/lines`] = () => {
    server = week("Draft", [game(G1, { lineHistory: [1.5] })]);
    return { status: 200, body: server };
  };
  replies[`POST /games/${G1}/lines/approve-latest`] = () => {
    server = week("Draft", [
      game(G1, { lineHistory: [1.5], approvedLine: 1.5 }),
    ]);
    return { status: 200, body: server };
  };
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  fireEvent.click(screen.getByRole("button", { name: /Add line/ }));
  fireEvent.change(screen.getByLabelText(/Spread for Bills/), {
    target: { value: "1.5" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save line" }));
  expect(
    await screen.findByText("Bills +1.5 · awaiting approval"),
  ).toBeVisible();
  expect(calls.find((c) => c.method === "POST")?.body).toEqual({
    spread: 1.5,
    approve: false,
  });
  fireEvent.click(screen.getByRole("button", { name: /Approve line/ }));

  expect(await screen.findByText("Bills +1.5 · approved")).toBeVisible();
  expect(screen.getByRole("status")).toHaveTextContent("Line approved.");
});

test("a rejected line stays beside its editor with what was typed and nothing is claimed saved", async () => {
  open("Draft", [game(G1)]);
  replies[`POST /games/${G1}/lines`] = {
    status: 409,
    body: { code: "week.state", message: "Approved lines are frozen." },
  };
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  fireEvent.click(screen.getByRole("button", { name: /Add line/ }));
  fireEvent.change(screen.getByLabelText(/Spread for Bills/), {
    target: { value: "-7" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save line" }));

  const alert = await screen.findByRole("alert");
  expect(alert).toHaveTextContent("Approved lines are frozen.");
  expect(screen.getByLabelText(/Spread for Bills/)).toHaveValue("-7");
  expect(screen.getByRole("status")).toBeEmptyDOMElement();
});

test("a spread that is not a number is refused before anything is sent", async () => {
  open("Draft", [game(G1)]);
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  fireEvent.click(screen.getByRole("button", { name: /Add line/ }));
  fireEvent.click(screen.getByRole("button", { name: "Save line" }));

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Enter the spread as a number",
  );
  expect(calls.some((c) => c.method === "POST")).toBe(false);
});

test("with every line approved the dock offers the bulk approval and the server answer replaces the page", async () => {
  open("Draft", [
    game(G1, { approvedLine: -3, lineHistory: [-3] }),
    game(G2, { approvedLine: 1, lineHistory: [1] }),
  ]);
  replies["POST /lines/approve"] = () => {
    server = week("LinesApproved", server.games);
    return { status: 200, body: server };
  };
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  fireEvent.click(
    within(screen.getByRole("region", { name: "Next action" })).getByRole(
      "button",
      { name: "Approve 2 lines" },
    ),
  );

  expect(
    await within(
      screen.getByRole("region", { name: "Next action" }),
    ).findByRole("button", { name: "Publish week" }),
  ).toBeVisible();
  expect(screen.getAllByText("Lines approved").length).toBeGreaterThan(0);
  expect(
    screen.queryByRole("button", { name: /Edit line|Add line/ }),
  ).not.toBeInTheDocument();
});

test.each([
  ["LinesApproved", "Publish week", "publish", "Open"],
  ["Open", "Lock week", "lock", "Locked"],
])(
  "%s: the dock calls %s and shows the new state from the server",
  async (state, label, path, nextState) => {
    open(state, [game(G1, { approvedLine: -3, lineHistory: [-3] })]);
    replies[`POST /${path}`] = () => {
      server = week(nextState, server.games);
      return { status: 200, body: server };
    };
    await screen.findByRole("heading", { level: 1, name: "Week 4" });

    fireEvent.click(screen.getByRole("button", { name: label }));

    await waitFor(() =>
      expect(
        calls.some((c) => c.method === "POST" && c.url.endsWith(`/${path}`)),
      ).toBe(true),
    );
    await waitFor(() =>
      expect(
        screen.getByRole("listitem", { current: "step" }),
      ).toHaveTextContent(
        new RegExp(nextState === "Open" ? "Open for picks" : "Locked"),
      ),
    );
  },
);

test("a lifecycle rejection shows its reason in the dock and keeps the week as it was", async () => {
  open("Open", [game(G1, { approvedLine: -3, lineHistory: [-3] })]);
  replies["POST /lock"] = {
    status: 409,
    body: { code: "week.state", message: "Week must be open." },
  };
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  fireEvent.click(screen.getByRole("button", { name: "Lock week" }));

  const dock = screen.getByRole("region", { name: "Next action" });
  expect(await within(dock).findByRole("alert")).toHaveTextContent(
    "Week must be open.",
  );
  expect(within(dock).getByRole("button", { name: "Lock week" })).toBeEnabled();
  expect(screen.getByRole("listitem", { current: "step" })).toHaveTextContent(
    "Open for picks",
  );
});

test("a locked week takes a result per game, then finalizing is offered only when none is missing", async () => {
  open("Locked", [
    game(G1, {
      approvedLine: -3,
      lineHistory: [-3],
      hasResult: true,
      homeScore: 21,
      awayScore: 14,
    }),
    game(G2, { approvedLine: 1, lineHistory: [1] }),
  ]);
  replies[`POST /games/${G2}/result`] = () => {
    server = week("Locked", [
      server.games[0],
      game(G2, {
        approvedLine: 1,
        lineHistory: [1],
        hasResult: true,
        homeScore: 10,
        awayScore: 17,
      }),
    ]);
    return { status: 200, body: server };
  };
  replies["POST /finalize"] = () => {
    server = week("Final", server.games);
    return { status: 200, body: server };
  };
  await screen.findByRole("heading", { level: 1, name: "Week 4" });
  const dock = screen.getByRole("region", { name: "Next action" });
  expect(dock).toHaveTextContent("1 of 2 included games still need a result.");
  expect(within(dock).queryByRole("button")).not.toBeInTheDocument();

  fireEvent.click(screen.getByRole("button", { name: /Enter result/ }));
  const form = screen.getByRole("form", { name: /Result for/ });
  fireEvent.change(within(form).getByLabelText("Ravens score"), {
    target: { value: "17" },
  });
  fireEvent.change(within(form).getByLabelText("Bills score"), {
    target: { value: "10" },
  });
  fireEvent.click(within(form).getByRole("button", { name: "Save result" }));

  expect(
    await within(dock).findByRole("button", { name: "Finalize week" }),
  ).toBeVisible();
  expect(calls.find((c) => c.url.endsWith("/result"))?.body).toEqual({
    homeScore: 10,
    awayScore: 17,
  });
  fireEvent.click(within(dock).getByRole("button", { name: "Finalize week" }));
  expect(
    await within(dock).findByRole("button", { name: "Recompute scores" }),
  ).toBeVisible();
  expect(screen.getByRole("listitem", { current: "step" })).toHaveTextContent(
    "Final",
  );
  expect(
    screen.queryByRole("button", { name: /Enter result|Edit result/ }),
  ).not.toBeInTheDocument();
});

test("result scores must be whole numbers before anything is sent", async () => {
  open("Locked", [game(G1, { approvedLine: -3, lineHistory: [-3] })]);
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  fireEvent.click(screen.getByRole("button", { name: /Enter result/ }));
  fireEvent.change(screen.getByLabelText("Ravens score"), {
    target: { value: "x" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save result" }));

  expect(await screen.findByRole("alert")).toHaveTextContent("whole numbers");
  expect(calls.some((c) => c.method === "POST")).toBe(false);
});

test("an excluded game is labelled and has no actions", async () => {
  open("Draft", [game(G1, { excluded: true }), game(G2)]);
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  const rows = screen
    .getAllByRole("listitem")
    .filter((li) => li.classList.contains("game"));
  expect(rows[0]).toHaveTextContent("Excluded from this week");
  expect(within(rows[0]).queryByRole("button")).not.toBeInTheDocument();
  expect(screen.getByRole("region", { name: "Next action" })).toHaveTextContent(
    "1 of 1 included game still needs",
  );
});

test("a final week shows scores and no editing", async () => {
  open("Final", [
    game(G1, {
      approvedLine: -3,
      lineHistory: [-3],
      hasResult: true,
      homeScore: 21,
      awayScore: 14,
    }),
  ]);
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  expect(screen.getByText("Ravens 14 · Bills 21")).toBeVisible();
  expect(
    screen.queryByRole("button", { name: /line|result/i }),
  ).not.toBeInTheDocument();
});

test("a missing week is said plainly and leads back to the league desk", async () => {
  replies["GET "] = {
    status: 404,
    body: { code: "week.missing", message: "Week does not exist." },
  };
  stubApi();
  window.history.replaceState({}, "", `/weeks/${WEEK}`);
  replies["GET /api/admin/dashboard"] = {
    status: 200,
    body: { season: null, currentWeek: null, attention: [], caughtUp: false },
  };
  render(<App />);

  expect(
    await screen.findByRole("heading", { name: "Week not found" }),
  ).toBeVisible();
  fireEvent.click(
    screen.getByRole("button", { name: "Back to the league desk" }),
  );
  expect(window.location.pathname).toBe("/");
});

test("a failed load or an unreachable server can be retried", async () => {
  let attempts = 0;
  replies["GET "] = () =>
    ++attempts === 1
      ? { status: 500 }
      : attempts === 2
        ? "network-error"
        : { status: 200, body: week("Open", [game(G1)]) };
  stubApi();
  window.history.replaceState({}, "", `/weeks/${WEEK}`);
  render(<App />);

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "The server answered 500.",
  );
  fireEvent.click(screen.getByRole("button", { name: "Try again" }));
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Could not reach the server.",
  );
  fireEvent.click(screen.getByRole("button", { name: "Try again" }));
  expect(
    await screen.findByRole("heading", { level: 1, name: "Week 4" }),
  ).toBeVisible();
});

test("a coach on a week address gets their own desk and no admin call is made", async () => {
  stubApi("Coach");
  window.history.replaceState({}, "", `/weeks/${WEEK}`);
  render(<App />);

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Could not load your picks.",
  );
  expect(calls.some((c) => c.url.includes("/api/admin"))).toBe(false);
});

test("Escape closes an open editor without sending anything and returns focus to its button", async () => {
  open("Draft", [game(G1)]);
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  fireEvent.click(screen.getByRole("button", { name: /Add line/ }));
  fireEvent.keyDown(screen.getByLabelText(/Spread for Bills/), {
    key: "Escape",
  });

  expect(screen.queryByRole("form")).not.toBeInTheDocument();
  await waitFor(() =>
    expect(screen.getByRole("button", { name: /Add line/ })).toHaveFocus(),
  );
  expect(calls.some((c) => c.method === "POST")).toBe(false);
});

test.each(["35", "-30.5", "1e3", "3,5", "--3", "0x10", "Infinity", ""])(
  "the spread %j is refused beside the editor before anything is sent",
  async (typed) => {
    open("Draft", [game(G1)]);
    await screen.findByRole("heading", { level: 1, name: "Week 4" });

    fireEvent.click(screen.getByRole("button", { name: /Add line/ }));
    fireEvent.change(screen.getByLabelText(/Spread for Bills/), {
      target: { value: typed },
    });
    fireEvent.click(screen.getByRole("button", { name: "Save and approve" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "a number from -30 to 30",
    );
    expect(calls.some((c) => c.method === "POST")).toBe(false);
    expect(screen.getByRole("status")).toBeEmptyDOMElement();
  },
);

test.each([
  ["30", 30],
  ["-30", -30],
  ["0", 0],
  ["-3.5", -3.5],
  ["+7", 7],
])("the spread %s is sent as the number %d", async (typed, sent) => {
  open("Draft", [game(G1)]);
  replies[`POST /games/${G1}/lines`] = () => ({
    status: 200,
    body: week("Draft", [game(G1, { lineHistory: [sent] })]),
  });
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  fireEvent.click(screen.getByRole("button", { name: /Add line/ }));
  fireEvent.change(screen.getByLabelText(/Spread for Bills/), {
    target: { value: typed },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save line" }));

  await waitFor(() =>
    expect(calls.find((c) => c.method === "POST")?.body).toEqual({
      spread: sent,
      approve: false,
    }),
  );
});

test.each([
  ["101", "0"],
  ["0", "101"],
  ["1e2", "3"],
  ["3.5", "3"],
  ["-1", "3"],
  ["", "3"],
  ["3", " "],
])(
  "the scores %j and %j are refused before anything is sent",
  async (away, home) => {
    open("Locked", [game(G1, { approvedLine: -3, lineHistory: [-3] })]);
    await screen.findByRole("heading", { level: 1, name: "Week 4" });

    fireEvent.click(screen.getByRole("button", { name: /Enter result/ }));
    fireEvent.change(screen.getByLabelText("Ravens score"), {
      target: { value: away },
    });
    fireEvent.change(screen.getByLabelText("Bills score"), {
      target: { value: home },
    });
    fireEvent.click(screen.getByRole("button", { name: "Save result" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "whole numbers from 0 to 100",
    );
    expect(calls.some((c) => c.method === "POST")).toBe(false);
  },
);

test("scores of 0 and 100 are accepted", async () => {
  open("Locked", [game(G1, { approvedLine: -3, lineHistory: [-3] })]);
  replies[`POST /games/${G1}/result`] = () => ({
    status: 200,
    body: week("Locked", [
      game(G1, {
        approvedLine: -3,
        lineHistory: [-3],
        hasResult: true,
        homeScore: 100,
        awayScore: 0,
      }),
    ]),
  });
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  fireEvent.click(screen.getByRole("button", { name: /Enter result/ }));
  fireEvent.change(screen.getByLabelText("Ravens score"), {
    target: { value: "0" },
  });
  fireEvent.change(screen.getByLabelText("Bills score"), {
    target: { value: "100" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save result" }));

  await waitFor(() =>
    expect(calls.find((c) => c.method === "POST")?.body).toEqual({
      homeScore: 100,
      awayScore: 0,
    }),
  );
});

test("the server's refusal of an out-of-range spread is shown beside the editor", async () => {
  open("Draft", [game(G1)]);
  replies[`POST /games/${G1}/lines`] = {
    status: 400,
    body: {
      code: "spread.invalid",
      message: "A spread must be between -30 and 30.",
    },
  };
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  fireEvent.click(screen.getByRole("button", { name: /Add line/ }));
  fireEvent.change(screen.getByLabelText(/Spread for Bills/), {
    target: { value: "-3.5" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save line" }));

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "A spread must be between -30 and 30.",
  );
  expect(screen.getByLabelText(/Spread for Bills/)).toHaveValue("-3.5");
});

test("recompute asks first, can be cancelled, and calls the endpoint only after confirming", async () => {
  open("Final", [
    game(G1, {
      approvedLine: -3,
      lineHistory: [-3],
      hasResult: true,
      homeScore: 21,
      awayScore: 14,
    }),
  ]);
  replies["POST /recompute"] = () => ({ status: 200, body: server });
  await screen.findByRole("heading", { level: 1, name: "Week 4" });
  const dock = screen.getByRole("region", { name: "Next action" });

  fireEvent.click(
    within(dock).getByRole("button", { name: "Recompute scores" }),
  );
  const confirm = within(dock).getByRole("group", {
    name: "Confirm recompute",
  });
  expect(confirm).toHaveTextContent("Recompute Week 4's scores?");
  expect(confirm).toHaveTextContent(
    "replaces them using the current ballots and results",
  );
  expect(
    within(confirm).getByRole("button", { name: "Confirm recompute" }),
  ).toHaveFocus();
  expect(calls.some((c) => c.method === "POST")).toBe(false);

  fireEvent.click(within(confirm).getByRole("button", { name: "Cancel" }));
  expect(within(dock).queryByRole("group")).not.toBeInTheDocument();
  expect(calls.some((c) => c.method === "POST")).toBe(false);
  await waitFor(() =>
    expect(
      within(dock).getByRole("button", { name: "Recompute scores" }),
    ).toHaveFocus(),
  );

  fireEvent.click(
    within(dock).getByRole("button", { name: "Recompute scores" }),
  );
  fireEvent.keyDown(within(dock).getByRole("group"), { key: "Escape" });
  expect(within(dock).queryByRole("group")).not.toBeInTheDocument();
  expect(calls.some((c) => c.method === "POST")).toBe(false);

  fireEvent.click(
    within(dock).getByRole("button", { name: "Recompute scores" }),
  );
  fireEvent.click(
    within(dock).getByRole("button", { name: "Confirm recompute" }),
  );

  await waitFor(() =>
    expect(calls.filter((c) => c.method === "POST").map((c) => c.url)).toEqual([
      `/api/admin/weeks/${WEEK}/recompute`,
    ]),
  );
  expect(await screen.findByText("Recompute scores: done.")).toBeVisible();
  expect(within(dock).queryByRole("group")).not.toBeInTheDocument();
});

test("a refused recompute shows the reason and leaves the confirmation closed", async () => {
  open("Final", [
    game(G1, {
      approvedLine: -3,
      lineHistory: [-3],
      hasResult: true,
      homeScore: 21,
      awayScore: 14,
    }),
  ]);
  replies["POST /recompute"] = {
    status: 409,
    body: {
      code: "week.ballots",
      message: "A member has more than one ballot for this week.",
    },
  };
  await screen.findByRole("heading", { level: 1, name: "Week 4" });
  const dock = screen.getByRole("region", { name: "Next action" });

  fireEvent.click(
    within(dock).getByRole("button", { name: "Recompute scores" }),
  );
  fireEvent.click(
    within(dock).getByRole("button", { name: "Confirm recompute" }),
  );

  expect(await within(dock).findByRole("alert")).toHaveTextContent(
    "more than one ballot",
  );
  expect(within(dock).queryByRole("group")).not.toBeInTheDocument();
});

test("the other dock actions need no confirmation", async () => {
  open("Open", [game(G1, { approvedLine: -3, lineHistory: [-3] })]);
  replies["POST /lock"] = () => ({
    status: 200,
    body: week("Locked", server.games),
  });
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  fireEvent.click(screen.getByRole("button", { name: "Lock week" }));

  await waitFor(() =>
    expect(calls.some((c) => c.url.endsWith("/lock"))).toBe(true),
  );
  expect(
    screen.queryByRole("group", { name: /Confirm/ }),
  ).not.toBeInTheDocument();
});
