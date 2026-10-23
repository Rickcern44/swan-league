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
  latestLine?: {
    spread: number;
    homeMoneyline: number | null;
    awayMoneyline: number | null;
    source: string;
    enteredAt: string;
    enteredBy: string;
  } | null;
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

function week(state: string, games: Game[], dogs: string[] = []) {
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
    dogOptionTeamIds: dogs,
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

const RAVENS_AT_BILLS = "Baltimore Ravens at Buffalo Bills";
const LIONS_AT_BEARS = "Detroit Lions at Chicago Bears";
const csvLine = (spread: number) => ({
  spread,
  homeMoneyline: -160,
  awayMoneyline: 135,
  source: "csv",
  enteredAt: "2026-09-08T13:02:00Z",
  enteredBy: "u",
});
const draftGames = () => [
  game(G1, {
    approvedLine: -3.5,
    lineHistory: [-3.5],
    latestLine: csvLine(-3.5),
  }),
  game(G2, {
    lineHistory: [2],
    homeTeamName: "Chicago Bears",
    awayTeamName: "Detroit Lions",
    latestLine: { ...csvLine(2), source: "manual" },
  }),
  game(G3, {
    homeTeamName: "Green Bay Packers",
    awayTeamName: "New York Jets",
  }),
];
const row = (matchup: string) =>
  screen.getByRole("row", { name: new RegExp(matchup) });
const lineTab = () => screen.getByRole("tab", { name: /^Lines/ });

test("a draft week shows its stage, its tabs and a line table with what each game still needs", async () => {
  open("Draft", draftGames());

  expect(
    await screen.findByRole("heading", { level: 1, name: "Week 4" }),
  ).toBeVisible();
  expect(screen.getByText("Week 4 · Regular season · 3 games")).toBeVisible();
  expect(screen.getByText("Review Week 4 lines")).toBeVisible();
  const stages = within(screen.getByRole("list", { name: "Week stages" }));
  expect(stages.getByRole("listitem", { current: "step" })).toHaveTextContent(
    "Draft",
  );
  expect(lineTab()).toHaveAttribute("aria-selected", "true");
  expect(lineTab()).toHaveTextContent("Lines 1/3");
  expect(
    screen.getAllByRole("tab").map((tab) => tab.textContent?.split(" ")[0]),
  ).toEqual(["Lines", "Dogs", "Picks", "Results", "Activity"]);

  const ravens = row(RAVENS_AT_BILLS);
  expect(ravens).toHaveTextContent("Thu 4:15 PM");
  expect(ravens).toHaveTextContent("Approved");
  expect(ravens).toHaveTextContent("+135 / -160");
  expect(ravens).toHaveTextContent("CSV");
  expect(row(LIONS_AT_BEARS)).toHaveTextContent("Needs review");
  expect(row(LIONS_AT_BEARS)).toHaveTextContent("Manual");
  expect(row("New York Jets at Green Bay Packers")).toHaveTextContent(
    "No line yet",
  );
  expect(
    within(row(LIONS_AT_BEARS)).getByRole("textbox", {
      name: `Spread for Bears (${LIONS_AT_BEARS})`,
    }),
  ).toHaveValue("2");

  const dock = screen.getByRole("region", { name: "Next action" });
  expect(dock).toHaveTextContent(
    "2 of 3 included games still need an approved line.",
  );
  expect(
    within(dock).getByRole("button", { name: "Preview as coach" }),
  ).toBeVisible();
  expect(
    within(dock).queryByRole("button", { name: /Approve|Publish/ }),
  ).toBeNull();
});

test("a typed spread can be saved without approving, as a manual line", async () => {
  open("Draft", [game(G1)]);
  replies[`POST /games/${G1}/lines`] = () => {
    server = week("Draft", [game(G1, { lineHistory: [-3.5] })]);
    return { status: 200, body: server };
  };
  await screen.findByRole("table", { name: "Lines" });

  fireEvent.change(screen.getByRole("textbox", { name: /^Spread for Bills/ }), {
    target: { value: "-3.5" },
  });
  expect(row(RAVENS_AT_BILLS)).toHaveTextContent("Unsaved");
  fireEvent.click(
    screen.getByRole("button", { name: `Save line for ${RAVENS_AT_BILLS}` }),
  );

  expect(await screen.findByText("Line saved.")).toBeVisible();
  expect(calls.find((c) => c.method === "POST")).toMatchObject({
    url: `/api/admin/weeks/${WEEK}/games/${G1}/lines`,
    body: { spread: -3.5, approve: false, source: "manual" },
  });
  expect(row(RAVENS_AT_BILLS)).toHaveTextContent("Needs review");
});

test("approve selected approves a reviewed line as it is and saves and approves a typed one", async () => {
  open("Draft", draftGames());
  replies[`POST /games/${G2}/lines/approve-latest`] = () => {
    server = week("Draft", [
      server.games[0],
      { ...server.games[1], approvedLine: 2 },
      server.games[2],
    ]);
    return { status: 200, body: server };
  };
  replies[`POST /games/${G3}/lines`] = () => {
    server = week("Draft", [
      server.games[0],
      server.games[1],
      { ...server.games[2], approvedLine: 7, lineHistory: [7] },
    ]);
    return { status: 200, body: server };
  };
  await screen.findByRole("table", { name: "Lines" });

  const approve = () =>
    screen.getByRole("button", { name: /^Approve \d+ selected$/ });
  expect(approve()).toBeDisabled();
  fireEvent.change(
    screen.getByRole("textbox", { name: /^Spread for Packers/ }),
    { target: { value: "+7" } },
  );
  fireEvent.click(
    screen.getByRole("checkbox", { name: "Select all lines that need review" }),
  );
  expect(
    screen.getByRole("checkbox", { name: `Select ${LIONS_AT_BEARS}` }),
  ).toBeChecked();
  expect(
    screen.getByRole("checkbox", { name: `Select ${RAVENS_AT_BILLS}` }),
  ).toBeDisabled();
  fireEvent.click(approve());

  expect(await screen.findByText("Approved 2 lines.")).toBeVisible();
  expect(calls.filter((c) => c.method === "POST")).toMatchObject([
    { url: `/api/admin/weeks/${WEEK}/games/${G2}/lines/approve-latest` },
    {
      url: `/api/admin/weeks/${WEEK}/games/${G3}/lines`,
      body: { spread: 7, approve: true, source: "manual" },
    },
  ]);
  expect(lineTab()).toHaveTextContent("Lines 3/3");
  expect(
    screen.getByRole("button", { name: /^Approve \d+ selected$/ }),
  ).toHaveTextContent("Approve 0 selected");
});

test("a game with no line and nothing typed cannot be selected", async () => {
  open("Draft", draftGames());
  await screen.findByRole("table", { name: "Lines" });

  expect(
    screen.getByRole("checkbox", {
      name: "Select New York Jets at Green Bay Packers",
    }),
  ).toBeDisabled();
});

test("the needs-review filter hides approved games", async () => {
  open("Draft", draftGames());
  await screen.findByRole("table", { name: "Lines" });

  const filter = screen.getByRole("button", { name: "Needs review (2)" });
  fireEvent.click(filter);
  expect(filter).toHaveAttribute("aria-pressed", "true");
  expect(
    screen.queryByRole("row", { name: new RegExp(RAVENS_AT_BILLS) }),
  ).toBeNull();
  expect(row(LIONS_AT_BEARS)).toBeVisible();
});

test("a refused approval names the game and keeps going with the rest", async () => {
  open("Draft", draftGames());
  replies[`POST /games/${G2}/lines/approve-latest`] = {
    status: 409,
    body: { code: "game.frozen", message: "Approved lines are frozen." },
  };
  replies[`POST /games/${G3}/lines`] = () => ({ status: 200, body: server });
  await screen.findByRole("table", { name: "Lines" });

  fireEvent.change(
    screen.getByRole("textbox", { name: /^Spread for Packers/ }),
    { target: { value: "3" } },
  );
  fireEvent.click(
    screen.getByRole("checkbox", { name: "Select all lines that need review" }),
  );
  fireEvent.click(screen.getByRole("button", { name: "Approve 2 selected" }));

  expect(await screen.findByRole("alert")).toHaveTextContent(
    `${LIONS_AT_BEARS}: Approved lines are frozen.`,
  );
  expect(calls.filter((c) => c.method === "POST")).toHaveLength(2);
});

test.each(["35", "-30.5", "1e3", "3,5", "--3", "0x10", "Infinity"])(
  "the spread %j is refused before anything is sent",
  async (typed) => {
    open("Draft", [game(G1)]);
    await screen.findByRole("table", { name: "Lines" });

    fireEvent.change(
      screen.getByRole("textbox", { name: /^Spread for Bills/ }),
      { target: { value: typed } },
    );
    fireEvent.click(
      screen.getByRole("button", { name: `Save line for ${RAVENS_AT_BILLS}` }),
    );

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "a number from -30 to 30",
    );
    expect(calls.some((c) => c.method === "POST")).toBe(false);
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
  await screen.findByRole("table", { name: "Lines" });

  fireEvent.change(screen.getByRole("textbox", { name: /^Spread for Bills/ }), {
    target: { value: typed },
  });
  fireEvent.click(
    screen.getByRole("button", { name: `Save line for ${RAVENS_AT_BILLS}` }),
  );

  await waitFor(() =>
    expect(calls.find((c) => c.method === "POST")?.body).toEqual({
      spread: sent,
      approve: false,
      source: "manual",
    }),
  );
});

test("the server's refusal of a spread is shown and what was typed is kept", async () => {
  open("Draft", [game(G1)]);
  replies[`POST /games/${G1}/lines`] = {
    status: 400,
    body: {
      code: "spread.invalid",
      message: "A spread must be between -30 and 30.",
    },
  };
  await screen.findByRole("table", { name: "Lines" });

  fireEvent.change(screen.getByRole("textbox", { name: /^Spread for Bills/ }), {
    target: { value: "-3.5" },
  });
  fireEvent.click(
    screen.getByRole("button", { name: `Save line for ${RAVENS_AT_BILLS}` }),
  );

  expect(await screen.findByRole("alert")).toHaveTextContent(
    `${RAVENS_AT_BILLS}: A spread must be between -30 and 30.`,
  );
  expect(
    screen.getByRole("textbox", { name: /^Spread for Bills/ }),
  ).toHaveValue("-3.5");
});

test("pasted CSV is previewed first, then saved as CSV lines for the games it matches", async () => {
  open("Draft", draftGames());
  replies[`POST /games/${G2}/lines`] = () => ({ status: 200, body: server });
  replies[`POST /games/${G3}/lines`] = () => ({ status: 200, body: server });
  await screen.findByRole("table", { name: "Lines" });

  fireEvent.click(screen.getByRole("button", { name: "Paste CSV" }));
  const panel = within(screen.getByRole("region", { name: "Paste CSV" }));
  fireEvent.change(panel.getByLabelText(/^CSV lines/), {
    target: {
      value:
        "away,home,home spread,away moneyline,home moneyline\nLions,Bears,+2.5,-140,120\nnew york jets,GREEN BAY PACKERS,-6,240,-290\nRaiders,Chargers,-4,,\nJets,Packers,abc",
    },
  });
  fireEvent.click(panel.getByRole("button", { name: "Preview CSV" }));

  const preview = within(panel.getByRole("table", { name: "CSV preview" }));
  const rows = preview.getAllByRole("row").slice(1);
  expect(rows).toHaveLength(4);
  expect(rows[0]).toHaveTextContent(LIONS_AT_BEARS);
  expect(rows[0]).toHaveTextContent("Bears +2.5");
  expect(rows[0]).toHaveTextContent("-140 / +120");
  expect(rows[1]).toHaveTextContent("Packers -6");
  expect(rows[2]).toHaveTextContent(
    "No game this week matches Raiders at Chargers",
  );
  expect(rows[3]).toHaveTextContent("The spread must be a number");
  expect(calls.some((c) => c.method === "POST")).toBe(false);

  fireEvent.click(panel.getByRole("button", { name: "Save 2 lines" }));

  expect(await screen.findByText("Saved 2 lines from CSV.")).toBeVisible();
  expect(calls.filter((c) => c.method === "POST")).toMatchObject([
    {
      url: `/api/admin/weeks/${WEEK}/games/${G2}/lines`,
      body: {
        spread: 2.5,
        approve: false,
        source: "csv",
        moneyline: { home: 120, away: -140 },
      },
    },
    {
      url: `/api/admin/weeks/${WEEK}/games/${G3}/lines`,
      body: {
        spread: -6,
        approve: false,
        source: "csv",
        moneyline: { home: -290, away: 240 },
      },
    },
  ]);
});

test("excluding a game asks for a reason and sends it", async () => {
  open("Draft", draftGames());
  replies[`POST /games/${G3}/exclusion`] = () => {
    server = week("Draft", [
      server.games[0],
      server.games[1],
      { ...server.games[2], excluded: true },
    ]);
    return { status: 200, body: server };
  };
  await screen.findByRole("table", { name: "Lines" });

  fireEvent.click(
    screen.getByRole("button", {
      name: "Exclude New York Jets at Green Bay Packers",
    }),
  );
  const form = screen.getByRole("form", {
    name: "Exclude New York Jets at Green Bay Packers",
  });
  fireEvent.click(within(form).getByRole("button", { name: "Exclude game" }));
  expect(within(form).getByRole("alert")).toHaveTextContent("Give a reason");
  expect(within(form).getByLabelText(/^Reason/)).toHaveAccessibleDescription(
    "Give a reason for the change.",
  );
  expect(calls.some((c) => c.method === "POST")).toBe(false);

  fireEvent.change(within(form).getByLabelText(/^Reason/), {
    target: { value: "London game, early kickoff" },
  });
  fireEvent.click(within(form).getByRole("button", { name: "Exclude game" }));

  expect(await screen.findByText("Game excluded.")).toBeVisible();
  expect(calls.find((c) => c.method === "POST")).toMatchObject({
    url: `/api/admin/weeks/${WEEK}/games/${G3}/exclusion`,
    body: { excluded: true, reason: "London game, early kickoff" },
  });
  expect(row("New York Jets at Green Bay Packers")).toHaveTextContent(
    "Excluded",
  );
  expect(
    screen.getByRole("button", {
      name: "Include New York Jets at Green Bay Packers",
    }),
  ).toBeVisible();
});

test("an excluded game has no spread input and does not count toward the lines", async () => {
  open("Draft", [
    game(G1, { excluded: true }),
    game(G2, { homeTeamName: "Chicago Bears", awayTeamName: "Detroit Lions" }),
  ]);
  await screen.findByRole("table", { name: "Lines" });

  expect(row(RAVENS_AT_BILLS)).toHaveTextContent("Excluded");
  expect(
    within(row(RAVENS_AT_BILLS)).queryByRole("textbox"),
  ).not.toBeInTheDocument();
  expect(screen.getByRole("region", { name: "Next action" })).toHaveTextContent(
    "1 of 1 included game still needs",
  );
});

test("the dogs tab lists each dog with its spread and can generate, add and remove them", async () => {
  const games = [
    game(G1, { approvedLine: -3.5, lineHistory: [-3.5] }),
    game(G2, {
      approvedLine: 2,
      lineHistory: [2],
      homeTeamName: "Chicago Bears",
      awayTeamName: "Detroit Lions",
    }),
  ];
  open("Draft", games);
  server = week("Draft", games, ["a" + G1]);
  replies["GET "] = () => ({ status: 200, body: server });
  replies["POST /dog-options/generate"] = () => {
    server = week("Draft", games, ["a" + G1, "h" + G2]);
    return { status: 200, body: server };
  };
  replies["PUT /dog-options"] = () => {
    const sent = calls.filter((c) => c.method === "PUT").at(-1)?.body as {
      teamIds: string[];
    };
    server = week("Draft", games, sent.teamIds);
    return { status: 200, body: server };
  };
  await screen.findByRole("table", { name: "Lines" });

  fireEvent.click(screen.getByRole("tab", { name: /^Dogs/ }));
  const dogs = () =>
    within(screen.getByRole("list", { name: "Dog options" })).getAllByRole(
      "listitem",
    );
  expect(dogs()).toHaveLength(1);
  expect(dogs()[0]).toHaveTextContent("Baltimore Ravens +3.5");

  fireEvent.click(
    screen.getByRole("button", { name: "Generate from approved lines" }),
  );
  await waitFor(() => expect(dogs()).toHaveLength(2));
  expect(dogs()[1]).toHaveTextContent("Chicago Bears +2");
  expect(screen.getByRole("tab", { name: /^Dogs/ })).toHaveTextContent(
    "Dogs 2",
  );

  fireEvent.click(
    screen.getByRole("button", { name: "Remove Baltimore Ravens" }),
  );
  await waitFor(() => expect(dogs()).toHaveLength(1));
  expect(calls.find((c) => c.method === "PUT")).toMatchObject({
    url: `/api/admin/weeks/${WEEK}/dog-options`,
    body: { teamIds: ["h" + G2] },
  });

  fireEvent.change(screen.getByLabelText("Add a dog"), {
    target: { value: "a" + G2 },
  });
  fireEvent.click(screen.getByRole("button", { name: "Add dog" }));
  await waitFor(() =>
    expect(calls.filter((c) => c.method === "PUT")).toHaveLength(2),
  );
  expect(calls.filter((c) => c.method === "PUT")[1].body).toEqual({
    teamIds: ["h" + G2, "a" + G2],
  });
});

test("dog options are read-only once the lines are approved", async () => {
  open("LinesApproved", [
    game(G1, { approvedLine: -3.5, lineHistory: [-3.5] }),
  ]);
  server = week(
    "LinesApproved",
    [game(G1, { approvedLine: -3.5, lineHistory: [-3.5] })],
    ["a" + G1],
  );
  replies["GET "] = () => ({ status: 200, body: server });
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  fireEvent.click(screen.getByRole("tab", { name: /^Dogs/ }));
  expect(screen.getByText(/fixed once the lines are approved/)).toBeVisible();
  expect(screen.queryByRole("button", { name: /^Remove/ })).toBeNull();
  expect(screen.queryByRole("button", { name: /Generate/ })).toBeNull();
});

test("the dock warns when the lines would be approved with no dog options", async () => {
  open("Draft", [game(G1, { approvedLine: -3, lineHistory: [-3] })]);
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  expect(screen.getByRole("region", { name: "Next action" })).toHaveTextContent(
    "No dog options yet. Generate them on the Dogs tab before approving the lines; they can't change afterwards.",
  );
});

test("an open week starts on the picks tab and lists every coach's card", async () => {
  open("Open", [game(G1, { approvedLine: -3, lineHistory: [-3] })]);
  replies["GET /picks"] = {
    status: 200,
    body: {
      coaches: [
        {
          membershipId: "m1",
          name: "Coach One",
          complete: true,
          starts: [
            {
              teamId: "t1",
              team: "Buffalo Bills",
              gameId: G1,
              pointValue: 3,
              spread: null,
              locked: true,
              result: null,
            },
          ],
          dogs: [
            {
              teamId: "t2",
              team: "Baltimore Ravens",
              gameId: G1,
              pointValue: null,
              spread: 3,
              locked: false,
              result: null,
            },
          ],
        },
        {
          membershipId: "m2",
          name: "Coach Two",
          complete: false,
          starts: [],
          dogs: [],
        },
      ],
    },
  };
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  expect(screen.getByRole("tab", { name: /^Picks/ })).toHaveAttribute(
    "aria-selected",
    "true",
  );
  const coaches = within(
    await screen.findByRole("list", { name: "Coach cards" }),
  ).getAllByRole("listitem");
  expect(screen.getByText("1 of 2 cards complete")).toBeVisible();
  expect(coaches[0]).toHaveTextContent("Coach One");
  expect(coaches[0]).toHaveTextContent("Complete");
  expect(coaches[0]).toHaveTextContent("Buffalo Bills · 3 pts · locked");
  expect(coaches[0]).toHaveTextContent("Baltimore Ravens +3");
  expect(coaches[1]).toHaveTextContent("Coach Two");
  expect(coaches[1]).toHaveTextContent("No picks yet");
});

test("a commissioner can remove a submitted pick only with a recorded reason", async () => {
  open("Open", [game(G1, { approvedLine: -3, lineHistory: [-3] })]);
  replies["GET /picks"] = {
    status: 200,
    body: {
      coaches: [
        {
          membershipId: "m1",
          name: "Coach One",
          complete: true,
          starts: [
            {
              teamId: "t1",
              team: "Buffalo Bills",
              gameId: G1,
              pointValue: 3,
              spread: null,
              locked: true,
              result: null,
            },
          ],
          dogs: [],
        },
      ],
    },
  };
  replies["PUT /coaches/m1/picks"] = () => ({ status: 200, body: server });
  await screen.findByRole("list", { name: "Coach cards" });

  fireEvent.click(screen.getByRole("button", { name: "Override picks" }));
  const form = screen.getByRole("form", { name: "Override Coach One's picks" });
  fireEvent.click(
    within(form).getByRole("button", { name: "Save coach-pick override" }),
  );
  expect(await within(form).findByRole("alert")).toHaveTextContent(
    "A commissioner override requires a reason.",
  );
  fireEvent.click(
    within(form).getByRole("checkbox", { name: "Start Buffalo Bills" }),
  );
  fireEvent.change(within(form).getByRole("textbox", { name: "Reason" }), {
    target: { value: "The coach withdrew the pick." },
  });
  fireEvent.click(
    within(form).getByRole("button", { name: "Save coach-pick override" }),
  );

  await waitFor(() =>
    expect(
      calls.find((call) => call.url.endsWith("/coaches/m1/picks"))?.body,
    ).toEqual({
      teamIds: [],
      dogTeamIds: [],
      reason: "The coach withdrew the pick.",
    }),
  );
});

test("the tab in the address is opened, and activity lists what happened to the week", async () => {
  replies[`GET /api/admin/audit?weekId=${WEEK}`] = {
    status: 200,
    body: [
      {
        id: { value: "e2" },
        userId: { value: "u" },
        action: "week.game.exclusion.changed",
        at: "2026-09-08T14:00:00Z",
        resourceId: WEEK,
        details: '{"before":null,"after":null,"reason":"London game"}',
      },
      {
        id: { value: "e1" },
        userId: { value: "u" },
        action: "week.line.added",
        at: "2026-09-08T13:02:00Z",
        resourceId: WEEK,
        details: '{"before":null,"after":null,"reason":null}',
      },
    ],
  };
  server = week("Draft", [game(G1)]);
  replies["GET "] = () => ({ status: 200, body: server });
  stubApi();
  window.history.replaceState({}, "", `/commish/weeks/${WEEK}?tab=activity`);
  render(<App />);

  const entries = within(
    await screen.findByRole("list", { name: "Week activity" }),
  ).getAllByRole("listitem");
  expect(entries).toHaveLength(2);
  expect(entries[0]).toHaveTextContent("Game included or excluded");
  expect(entries[0]).toHaveTextContent("London game");
  expect(entries[0]).toHaveTextContent("Tue, Sep 8, 10:00 AM ET");
  expect(entries[1]).toHaveTextContent("Line entered");
});

test("preview as coach shows the games with their lines and the dog options", async () => {
  const games = [
    game(G1, { approvedLine: -3.5, lineHistory: [-3.5] }),
    game(G2, {
      approvedLine: 2,
      lineHistory: [2],
      homeTeamName: "Chicago Bears",
      awayTeamName: "Detroit Lions",
    }),
  ];
  open("LinesApproved", games);
  server = week("LinesApproved", games, ["h" + G2]);
  replies["GET "] = () => ({ status: 200, body: server });
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  fireEvent.click(screen.getByRole("button", { name: "Preview as coach" }));
  const preview = within(screen.getByRole("region", { name: "Coach preview" }));
  expect(preview.getAllByRole("listitem").map((li) => li.textContent)).toEqual([
    "Ravens at Bills · Bills -3.5",
    "Lions at Bears · Bears +2",
    "Chicago Bears +2",
  ]);
  fireEvent.click(preview.getByRole("button", { name: "Close preview" }));
  expect(screen.queryByRole("region", { name: "Coach preview" })).toBeNull();
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
    ).findByRole("button", { name: "Publish Week 4" }),
  ).toBeVisible();
  expect(screen.getAllByText("Lines approved").length).toBeGreaterThan(0);
  expect(screen.queryByRole("textbox", { name: /^Spread for/ })).toBeNull();
});

test.each([
  ["LinesApproved", "Publish Week 4", "publish", "Open"],
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
  expect(window.location.pathname).toBe("/commish");
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
    "Could not load your week.",
  );
  expect(calls.some((c) => c.url.includes("/api/admin"))).toBe(false);
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

const W3 = "33333333-3333-3333-3333-333333333333";
const W5 = "55555555-5555-5555-5555-555555555555";
const seasonWeeks = [
  { id: W3, number: 3, kind: "Regular", state: "Final" },
  { id: WEEK, number: 4, kind: "Regular", state: "Open" },
  { id: W5, number: 5, kind: "Regular", state: "Draft" },
];

test("the week picker lists the season's weeks with their state and moves between them", async () => {
  replies["GET /api/admin/seasons/s/weeks"] = {
    status: 200,
    body: seasonWeeks,
  };
  open("Draft", [game(G1)]);
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  const picker = within(
    await screen.findByRole("navigation", { name: "Weeks" }),
  );
  const select = picker.getByLabelText("Week");
  expect(select).toHaveValue(WEEK);
  expect(
    within(select)
      .getAllByRole("option")
      .map((option) => option.textContent),
  ).toEqual(["Week 3 · Final", "Week 4 · Open for picks", "Week 5 · Draft"]);

  fireEvent.click(picker.getByRole("button", { name: "Next week, Week 5" }));
  expect(window.location.pathname).toBe(`/commish/weeks/${W5}`);
  expect(window.location.search).toBe("?tab=lines");
});

test("choosing a week in the picker opens it, and the first week has no previous", async () => {
  replies["GET /api/admin/seasons/s/weeks"] = {
    status: 200,
    body: [seasonWeeks[1], seasonWeeks[2]],
  };
  open("Draft", [game(G1)]);
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  const picker = within(
    await screen.findByRole("navigation", { name: "Weeks" }),
  );
  expect(picker.getByRole("button", { name: "Previous week" })).toBeDisabled();
  fireEvent.change(picker.getByLabelText("Week"), { target: { value: W5 } });
  expect(window.location.pathname).toBe(`/commish/weeks/${W5}`);
});

test("without the season's weeks the workspace still works and offers no picker", async () => {
  open("Draft", [game(G1)]);
  await screen.findByRole("table", { name: "Lines" });

  expect(screen.queryByRole("navigation", { name: "Weeks" })).toBeNull();
  expect(screen.getByRole("button", { name: "‹ Today" })).toBeVisible();
});

const resultsTable = () => screen.getByRole("table", { name: "Results" });
const resultRow = (matchup: string) =>
  within(resultsTable()).getByRole("row", { name: new RegExp(matchup) });
const score = (team: string) =>
  screen.getByRole("textbox", { name: new RegExp(`^${team} score`) });

test("a locked week enters both scores in the table, then finalizing is offered once none is missing", async () => {
  open("Locked", [
    game(G1, {
      approvedLine: -3,
      lineHistory: [-3],
      hasResult: true,
      homeScore: 21,
      awayScore: 14,
    }),
    game(G2, {
      approvedLine: 1,
      lineHistory: [1],
      homeTeamName: "Chicago Bears",
      awayTeamName: "Detroit Lions",
    }),
  ]);
  replies[`POST /games/${G2}/result`] = () => {
    server = week("Locked", [
      server.games[0],
      {
        ...server.games[1],
        hasResult: true,
        homeScore: 10,
        awayScore: 17,
      },
    ]);
    return { status: 200, body: server };
  };
  replies["POST /finalize"] = () => {
    server = week("Final", server.games);
    return { status: 200, body: server };
  };
  await screen.findByRole("table", { name: "Results" });
  expect(screen.getByRole("tab", { name: "Results" })).toHaveAttribute(
    "aria-selected",
    "true",
  );
  const dock = screen.getByRole("region", { name: "Next action" });
  expect(dock).toHaveTextContent("1 of 2 included games still need a result.");
  expect(within(dock).queryByRole("button")).not.toBeInTheDocument();
  expect(resultRow("Baltimore Ravens at Buffalo Bills")).toHaveTextContent(
    "Saved",
  );
  expect(score("Ravens")).toHaveValue("14");
  expect(resultRow("Detroit Lions at Chicago Bears")).toHaveTextContent(
    "No result yet",
  );

  fireEvent.change(score("Lions"), { target: { value: "17" } });
  expect(
    screen.queryByRole("button", { name: /^Save result for Detroit/ }),
  ).toBeNull();
  fireEvent.change(score("Bears"), { target: { value: "10" } });
  expect(resultRow("Detroit Lions at Chicago Bears")).toHaveTextContent(
    "Unsaved",
  );
  fireEvent.click(
    screen.getByRole("button", {
      name: "Save result for Detroit Lions at Chicago Bears",
    }),
  );

  expect(
    await within(dock).findByRole("button", { name: "Finalize week" }),
  ).toBeVisible();
  expect(calls.find((c) => c.url.endsWith("/result"))?.body).toEqual({
    homeScore: 10,
    awayScore: 17,
  });
  expect(screen.getByText("Result saved.")).toBeVisible();
  fireEvent.click(within(dock).getByRole("button", { name: "Finalize week" }));
  expect(
    await within(dock).findByRole("button", { name: "Recompute scores" }),
  ).toBeVisible();
  expect(screen.getByRole("listitem", { current: "step" })).toHaveTextContent(
    "Final",
  );
  expect(screen.queryByRole("textbox", { name: /score/ })).toBeNull();
});

test("save results sends every complete row and names any game the server refuses", async () => {
  open("Locked", [
    game(G1, { approvedLine: -3, lineHistory: [-3] }),
    game(G2, {
      approvedLine: 1,
      lineHistory: [1],
      homeTeamName: "Chicago Bears",
      awayTeamName: "Detroit Lions",
    }),
    game(G3, {
      approvedLine: 2,
      lineHistory: [2],
      homeTeamName: "Green Bay Packers",
      awayTeamName: "New York Jets",
    }),
  ]);
  replies[`POST /games/${G1}/result`] = {
    status: 409,
    body: {
      code: "week.state",
      message: "Results can only be confirmed while the week is locked.",
    },
  };
  replies[`POST /games/${G2}/result`] = () => ({ status: 200, body: server });
  await screen.findByRole("table", { name: "Results" });

  const saveAll = () =>
    screen.getByRole("button", { name: /^Save \d+ results?$/ });
  expect(saveAll()).toBeDisabled();
  fireEvent.change(score("Ravens"), { target: { value: "20" } });
  fireEvent.change(score("Bills"), { target: { value: "24" } });
  fireEvent.change(score("Lions"), { target: { value: "3" } });
  fireEvent.change(score("Bears"), { target: { value: "0" } });
  fireEvent.change(score("Jets"), { target: { value: "7" } });
  expect(saveAll()).toHaveTextContent("Save 2 results");
  fireEvent.click(saveAll());

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Baltimore Ravens at Buffalo Bills: Results can only be confirmed while the week is locked.",
  );
  expect(calls.filter((c) => c.method === "POST").map((c) => c.url)).toEqual([
    `/api/admin/weeks/${WEEK}/games/${G1}/result`,
    `/api/admin/weeks/${WEEK}/games/${G2}/result`,
  ]);
  expect(screen.getByText("Saved 1 result.")).toBeVisible();
  expect(score("Ravens")).toHaveValue("20");
});

test.each([
  ["101", "0"],
  ["0", "101"],
  ["1e2", "3"],
  ["3.5", "3"],
  ["-1", "3"],
  ["x", "3"],
])(
  "the scores %j and %j are refused before anything is sent",
  async (away, home) => {
    open("Locked", [game(G1, { approvedLine: -3, lineHistory: [-3] })]);
    await screen.findByRole("table", { name: "Results" });

    fireEvent.change(score("Ravens"), { target: { value: away } });
    fireEvent.change(score("Bills"), { target: { value: home } });
    fireEvent.click(
      screen.getByRole("button", {
        name: "Save result for Baltimore Ravens at Buffalo Bills",
      }),
    );

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Baltimore Ravens at Buffalo Bills: Enter both scores as whole numbers from 0 to 100.",
    );
    expect(calls.some((c) => c.method === "POST")).toBe(false);
  },
);

test("scores of 0 and 100 are accepted", async () => {
  open("Locked", [game(G1, { approvedLine: -3, lineHistory: [-3] })]);
  replies[`POST /games/${G1}/result`] = () => ({ status: 200, body: server });
  await screen.findByRole("table", { name: "Results" });

  fireEvent.change(score("Ravens"), { target: { value: "0" } });
  fireEvent.change(score("Bills"), { target: { value: "100" } });
  fireEvent.click(
    screen.getByRole("button", {
      name: "Save result for Baltimore Ravens at Buffalo Bills",
    }),
  );

  await waitFor(() =>
    expect(calls.find((c) => c.method === "POST")?.body).toEqual({
      homeScore: 100,
      awayScore: 0,
    }),
  );
});

test("Escape in a score puts back the saved result and sends nothing", async () => {
  open("Locked", [
    game(G1, {
      approvedLine: -3,
      lineHistory: [-3],
      hasResult: true,
      homeScore: 21,
      awayScore: 14,
    }),
  ]);
  await screen.findByRole("table", { name: "Results" });

  fireEvent.change(score("Ravens"), { target: { value: "17" } });
  expect(resultRow("Baltimore Ravens")).toHaveTextContent("Unsaved");
  fireEvent.keyDown(score("Ravens"), { key: "Escape" });

  expect(score("Ravens")).toHaveValue("14");
  expect(resultRow("Baltimore Ravens")).toHaveTextContent("Saved");
  expect(calls.some((c) => c.method === "POST")).toBe(false);
});

test("before the week locks the results tab says when scores can be entered", async () => {
  open("Open", [game(G1, { approvedLine: -3, lineHistory: [-3] })]);
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  fireEvent.click(screen.getByRole("tab", { name: "Results" }));
  expect(
    screen.getByText("Results are entered once the week locks."),
  ).toBeVisible();
  expect(resultRow("Baltimore Ravens")).toHaveTextContent("No result yet");
  expect(screen.queryByRole("textbox", { name: /score/ })).toBeNull();
});

test("an early coach-pick visibility override requires a reason and uses the admin contract", async () => {
  open("Locked", [game(G1, { approvedLine: -3, lineHistory: [-3] })]);
  replies["GET /picks"] = { status: 200, body: { coaches: [] } };
  replies["PUT /coach-pick-visibility"] = () => ({ status: 200, body: server });
  await screen.findByRole("heading", { level: 1, name: "Week 4" });

  fireEvent.click(screen.getByRole("tab", { name: "Picks" }));
  const form = await screen.findByRole("form", {
    name: "Coach pick visibility",
  });
  fireEvent.click(
    within(form).getByRole("checkbox", {
      name: "Show coach picks early for this week",
    }),
  );
  fireEvent.click(
    within(form).getByRole("button", { name: "Save visibility override" }),
  );
  expect(await within(form).findByRole("alert")).toHaveTextContent(
    "A visibility override requires a reason.",
  );

  fireEvent.change(within(form).getByRole("textbox", { name: "Reason" }), {
    target: { value: "A scoring correction needs review." },
  });
  fireEvent.click(
    within(form).getByRole("button", { name: "Save visibility override" }),
  );
  await waitFor(() =>
    expect(calls.find((call) => call.method === "PUT")?.body).toEqual({
      enabled: true,
      reason: "A scoring correction needs review.",
    }),
  );
});
