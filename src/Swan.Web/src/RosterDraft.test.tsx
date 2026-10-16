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

type Team = {
  teamId: string;
  name: string;
  division: string;
  pointValue: number;
  selected: boolean;
};
const team = (n: number, selected = false): Team => ({
  teamId: id(n),
  name: `Team ${n}`,
  division: n <= 4 ? "AFC East" : n <= 8 ? "AFC North" : "NFC East",
  pointValue: (n % 4) + 1,
  selected,
});

function desk(over: Record<string, unknown> = {}, selected: number[] = []) {
  return {
    state: "open",
    seasonId: SEASON,
    seasonName: "2026 Swan League",
    deadline: "2026-10-18T12:00:00Z",
    serverTime: "2026-10-15T12:00:00Z",
    isLocked: false,
    lockedAt: null,
    complete: selected.length === 9,
    revision: 0,
    teams: Array.from({ length: 12 }, (_, i) =>
      team(i + 1, selected.includes(i + 1)),
    ),
    ...over,
  };
}

type Reply = { status: number; body?: unknown } | "network-error";
let current: ReturnType<typeof desk>;
let replies: Record<string, Reply | ((body?: { teamIds?: string[] }) => Reply)>;
let calls: {
  method: string;
  url: string;
  body?: { teamIds?: string[]; seasonId?: string; expectedRevision?: number };
}[];

function stubApi() {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? "GET";
      const body = init?.body ? JSON.parse(init.body as string) : undefined;
      calls.push({ method, url, body });
      let reply: Reply;
      if (url === "/api/me")
        reply = {
          status: 200,
          body: { id: "u", name: "Coach One", role: "Coach" },
        };
      else {
        const configured = replies[`${method} ${url}`];
        reply =
          typeof configured === "function"
            ? configured(body)
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

function open(d = desk()) {
  current = d;
  replies["GET /api/coach/roster"] = () => ({ status: 200, body: current });
  stubApi();
  window.history.replaceState({}, "", "/roster");
  return render(<App />);
}

beforeEach(() => {
  replies = {};
  calls = [];
});
afterEach(cleanup);

const ready = () =>
  screen.findByRole("heading", { level: 1, name: "Choose your nine" });
const choose = (n: number) =>
  screen.getByRole("button", {
    name: new RegExp(`^(Choose|Chosen) Team ${n}$`),
  });
const dock = () => screen.getByRole("region", { name: "Save roster" });

test("the open draft shows the deadline, the count and every team grouped by division without a table", async () => {
  open();

  expect(await ready()).toBeVisible();
  expect(screen.getByText(/Choose exactly nine teams/)).toHaveTextContent(
    "Sun, Oct 18, 8:00 AM ET",
  );
  expect(screen.getByText(/3d 0h left/)).toBeVisible();
  expect(screen.getByText("0 of 9 chosen · 9 to go")).toBeVisible();
  expect(
    screen.getAllByRole("heading", { level: 2 }).map((h) => h.textContent),
  ).toEqual(expect.arrayContaining(["AFC East", "AFC North", "NFC East"]));
  expect(screen.queryByRole("table")).not.toBeInTheDocument();
  expect(
    within(dock()).getByRole("button", { name: "Choose 9 more" }),
  ).toBeDisabled();
  expect(
    screen.queryByRole("button", { name: "Lock roster" }),
  ).not.toBeInTheDocument();
});

test("a tenth team cannot be chosen and removing one frees the others", async () => {
  open(desk({ teams: Array.from({ length: 12 }, (_, i) => team(i + 1)) }));
  await ready();

  for (let n = 1; n <= 9; n++) fireEvent.click(choose(n));

  expect(screen.getByText("9 of 9 chosen · ready to save")).toBeVisible();
  expect(choose(10)).toBeDisabled();
  fireEvent.click(choose(3));
  expect(choose(10)).toBeEnabled();
  expect(
    within(dock()).getByRole("button", { name: "Choose 1 more" }),
  ).toBeDisabled();
});

test("saving sends the nine teams with the revision it read and then offers the lock", async () => {
  open();
  replies["PUT /api/coach/roster"] = (body) => {
    current = desk(
      { revision: 1, complete: true },
      [1, 2, 3, 4, 5, 6, 7, 8, 9],
    );
    expect(body?.teamIds).toHaveLength(9);
    return { status: 200, body: current };
  };
  await ready();

  for (let n = 1; n <= 9; n++) fireEvent.click(choose(n));
  fireEvent.click(within(dock()).getByRole("button", { name: "Save roster" }));

  expect(await screen.findByText("Roster saved.")).toBeVisible();
  expect(calls.find((c) => c.method === "PUT")).toMatchObject({
    url: "/api/coach/roster",
    body: { seasonId: SEASON, expectedRevision: 0 },
  });
  expect(within(dock()).getByRole("button", { name: "Saved" })).toBeDisabled();
  expect(
    within(dock()).getByRole("button", { name: "Lock roster" }),
  ).toBeVisible();
});

test("locking asks first, can be cancelled with Escape, and only then calls the lock endpoint", async () => {
  open(desk({ revision: 1, complete: true }, [1, 2, 3, 4, 5, 6, 7, 8, 9]));
  replies["POST /api/coach/roster/lock"] = () => {
    current = desk(
      {
        state: "locked",
        isLocked: true,
        revision: 2,
        complete: true,
        lockedAt: "2026-10-15T12:05:00Z",
      },
      [1, 2, 3, 4, 5, 6, 7, 8, 9],
    );
    return { status: 200, body: current };
  };
  await ready();

  fireEvent.click(within(dock()).getByRole("button", { name: "Lock roster" }));
  const group = within(dock()).getByRole("group", { name: "Confirm lock" });
  expect(group).toHaveTextContent("only the commissioner can");
  expect(
    within(group).getByRole("button", { name: "Confirm lock" }),
  ).toHaveFocus();
  fireEvent.keyDown(group, { key: "Escape" });
  expect(within(dock()).queryByRole("group")).not.toBeInTheDocument();
  expect(calls.some((c) => c.method === "POST")).toBe(false);

  fireEvent.click(within(dock()).getByRole("button", { name: "Lock roster" }));
  fireEvent.click(within(dock()).getByRole("button", { name: "Confirm lock" }));

  expect(
    await screen.findByRole("heading", { name: "Your roster is locked" }),
  ).toBeVisible();
  expect(calls.find((c) => c.method === "POST")).toMatchObject({
    url: "/api/coach/roster/lock",
    body: { seasonId: SEASON, expectedRevision: 1 },
  });
  expect(
    within(screen.getByRole("region", { name: "Your teams" })).getAllByRole(
      "listitem",
    ),
  ).toHaveLength(9);
  expect(
    screen.queryByRole("button", { name: /Choose|Save|Lock/ }),
  ).not.toBeInTheDocument();
});

test("the lock is not offered while there are unsaved changes", async () => {
  open(desk({ revision: 1, complete: true }, [1, 2, 3, 4, 5, 6, 7, 8, 9]));
  await ready();

  fireEvent.click(choose(9));

  expect(
    screen.queryByRole("button", { name: "Lock roster" }),
  ).not.toBeInTheDocument();
  expect(within(dock()).getByText("Unsaved changes")).toBeVisible();
});

test("a refused save shows the server's reason beside the dock and keeps the choices", async () => {
  open();
  replies["PUT /api/coach/roster"] = {
    status: 409,
    body: {
      code: "roster.locked",
      message: "The roster can no longer be changed by the coach.",
    },
  };
  await ready();
  for (let n = 1; n <= 9; n++) fireEvent.click(choose(n));

  fireEvent.click(within(dock()).getByRole("button", { name: "Save roster" }));

  expect(await within(dock()).findByRole("alert")).toHaveTextContent(
    "can no longer be changed",
  );
  expect(choose(1)).toHaveAttribute("aria-pressed", "true");
  expect(screen.getByRole("status")).toBeEmptyDOMElement();
});

test("a stale save offers a reload that replaces the local choices with the saved roster", async () => {
  open();
  replies["PUT /api/coach/roster"] = {
    status: 409,
    body: {
      code: "roster.stale",
      message:
        "Your roster changed since this page loaded. Reload to see the saved one.",
    },
  };
  await ready();
  for (let n = 1; n <= 9; n++) fireEvent.click(choose(n));
  fireEvent.click(within(dock()).getByRole("button", { name: "Save roster" }));
  await within(dock()).findByRole("alert");

  current = desk(
    { revision: 4, complete: true },
    [4, 5, 6, 7, 8, 9, 10, 11, 12],
  );
  fireEvent.click(
    within(dock()).getByRole("button", { name: "Reload saved roster" }),
  );

  await waitFor(() =>
    expect(choose(12)).toHaveAttribute("aria-pressed", "true"),
  );
  expect(choose(1)).toHaveAttribute("aria-pressed", "false");
});

test("after the deadline an incomplete roster says so and nothing can be changed", async () => {
  open(desk({ state: "closed", complete: false }, [1, 2, 3]));

  expect(
    await screen.findByRole("heading", { name: "Your roster is incomplete" }),
  ).toBeVisible();
  expect(screen.getByRole("status")).toHaveTextContent(
    "with 3 of 9 teams chosen",
  );
  expect(screen.getByRole("status")).toHaveTextContent(
    "until the commissioner completes your roster",
  );
  expect(
    screen.queryByRole("button", { name: /Choose|Save|Lock/ }),
  ).not.toBeInTheDocument();
  expect(
    within(screen.getByRole("region", { name: "Your teams" })).getAllByRole(
      "listitem",
    ),
  ).toHaveLength(3);
});

test("after the deadline a complete roster is shown as final for the coach", async () => {
  open(
    desk(
      { state: "closed", complete: true, revision: 1 },
      [1, 2, 3, 4, 5, 6, 7, 8, 9],
    ),
  );

  expect(
    await screen.findByRole("heading", { name: "The draft is closed" }),
  ).toBeVisible();
  expect(
    screen.getByText(/Only the commissioner can change them now/),
  ).toBeVisible();
  expect(
    within(screen.getByRole("region", { name: "Your teams" })).getAllByRole(
      "listitem",
    ),
  ).toHaveLength(9);
});

test("a coach who is in no live season sees a calm explanation", async () => {
  open(
    desk({
      state: "no-membership",
      seasonId: null,
      seasonName: null,
      teams: [],
    }),
  );

  expect(
    await screen.findByRole("heading", { name: "No roster to draft" }),
  ).toBeVisible();
});

test("a failed load can be retried", async () => {
  let attempts = 0;
  replies["GET /api/coach/roster"] = () =>
    ++attempts === 1 ? { status: 500 } : { status: 200, body: desk() };
  stubApi();
  window.history.replaceState({}, "", "/roster");
  render(<App />);

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "The server answered 500.",
  );
  fireEvent.click(screen.getByRole("button", { name: "Try again" }));
  expect(await ready()).toBeVisible();
});

test("a coach reaches the roster from the navigation and never calls an admin or commissioner endpoint", async () => {
  open();
  await ready();

  expect(screen.getByRole("link", { name: "Roster" })).toHaveAttribute(
    "aria-current",
    "page",
  );
  expect(screen.queryByRole("link", { name: "Draft" })).not.toBeInTheDocument();
  expect(
    calls.some(
      (c) =>
        c.url.includes("/api/admin") || c.url.includes("/api/commissioner"),
    ),
  ).toBe(false);
});

test("every control names its team and the page has one h1", async () => {
  open();
  await ready();

  expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
  expect(screen.getByRole("button", { name: "Choose Team 1" })).toBeVisible();
});

test("cancelling the lock confirmation, with the button or Escape, returns focus to the Lock roster button", async () => {
  open(desk({ revision: 1, complete: true }, [1, 2, 3, 4, 5, 6, 7, 8, 9]));
  await ready();

  for (const cancel of ["button", "escape"]) {
    fireEvent.click(
      within(dock()).getByRole("button", { name: "Lock roster" }),
    );
    const group = within(dock()).getByRole("group", { name: "Confirm lock" });
    if (cancel === "button")
      fireEvent.click(within(group).getByRole("button", { name: "Cancel" }));
    else fireEvent.keyDown(group, { key: "Escape" });

    expect(within(dock()).queryByRole("group")).not.toBeInTheDocument();
    await waitFor(() =>
      expect(
        within(dock()).getByRole("button", { name: "Lock roster" }),
      ).toHaveFocus(),
    );
  }
});
