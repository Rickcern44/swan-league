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
let calls: string[];

function open(path: string, me: Me) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string) => {
      calls.push(url);
      const ok = url === "/api/me";
      return {
        ok,
        status: ok ? 200 : 404,
        json: async () =>
          ok
            ? { id: "u", name: "Pat", ...me }
            : { code: "missing", message: "Not here." },
      };
    }),
  );
  window.history.replaceState({}, "", path);
  return render(<App />);
}

beforeEach(() => {
  calls = [];
  window.localStorage.clear();
});
afterEach(cleanup);

const nav = () => screen.getByRole("navigation", { name: "Primary" });
const modes = () => screen.queryByRole("group", { name: "Mode" });
const weekId = "7a1f3c2e-9b4d-4e8a-a1b2-c3d4e5f60718";

test("a coach with no commissioner access gets the coach destinations and no mode switch", async () => {
  open("/", { role: "Coach", setupAccess: "none" });

  await screen.findByRole("heading", { level: 1, name: "Your week" });
  expect(
    within(nav())
      .getAllByRole("link")
      .map((link) => [link.textContent, link.getAttribute("href")]),
  ).toEqual([
    ["Home", "/"],
    ["Picks", "/picks"],
    ["Draft", "/roster"],
  ]);
  expect(within(nav()).getByRole("link", { name: "Home" })).toHaveAttribute(
    "aria-current",
    "page",
  );
  expect(modes()).not.toBeInTheDocument();
  expect(screen.queryByText(/coming soon/i)).not.toBeInTheDocument();
  expect(
    within(nav()).queryByRole("link", { name: "Season setup" }),
  ).not.toBeInTheDocument();
});

test("an administrator starts in coach mode with the full coach experience and a switch to Commish", async () => {
  open("/", { role: "Admin" });

  await screen.findByRole("heading", { level: 1, name: "Your week" });
  const switcher = modes()!;
  expect(within(switcher).getByRole("link", { name: "Coach" })).toHaveAttribute(
    "aria-current",
    "page",
  );
  expect(
    within(switcher).getByRole("link", { name: "Commish" }),
  ).toHaveAttribute("href", "/commish");
  expect(within(nav()).getByRole("link", { name: "Picks" })).toBeVisible();
  expect(within(nav()).getByRole("link", { name: "Draft" })).toHaveAttribute(
    "href",
    "/roster",
  );
  expect(calls.some((url) => url.includes("/api/admin"))).toBe(false);
});

test("switching an administrator to Commish shows the commish destinations and remembers the mode", async () => {
  open("/", { role: "Admin" });
  await screen.findByRole("heading", { level: 1, name: "Your week" });

  fireEvent.click(within(modes()!).getByRole("link", { name: "Commish" }));

  expect(window.location.pathname).toBe("/commish");
  await waitFor(() => expect(calls).toContain("/api/admin/dashboard"));
  expect(within(nav()).getByRole("link", { name: "Today" })).toHaveAttribute(
    "aria-current",
    "page",
  );
  expect(within(nav()).getByRole("link", { name: "Draft" })).toHaveAttribute(
    "href",
    "/commish/draft",
  );
  expect(
    within(nav()).getByRole("link", { name: "Season setup" }),
  ).toHaveAttribute("href", "/commish/setup");
  expect(
    within(nav()).queryByRole("link", { name: "Picks" }),
  ).not.toBeInTheDocument();
  expect(
    within(modes()!).getByRole("link", { name: "Commish" }),
  ).toHaveAttribute("aria-current", "page");
  expect(window.localStorage.getItem("swan.mode")).toBe("commish");
});

test("an administrator who last used Commish on this device lands there from the home address", async () => {
  window.localStorage.setItem("swan.mode", "commish");
  open("/", { role: "Admin" });

  await waitFor(() => expect(window.location.pathname).toBe("/commish"));
  expect(within(nav()).getByRole("link", { name: "Today" })).toHaveAttribute(
    "aria-current",
    "page",
  );
});

test("a remembered Commish mode is ignored for a coach who has no commissioner access", async () => {
  window.localStorage.setItem("swan.mode", "commish");
  open("/", { role: "Coach", setupAccess: "none" });

  await screen.findByRole("heading", { level: 1, name: "Your week" });
  expect(window.location.pathname).toBe("/");
  expect(modes()).not.toBeInTheDocument();
});

test("a coach who commissions the live season switches to Commish and lands on the commish Draft page", async () => {
  open("/", {
    role: "Coach",
    setupAccess: "none",
    commissionsLiveSeason: true,
  });
  await screen.findByRole("heading", { level: 1, name: "Your week" });

  fireEvent.click(within(modes()!).getByRole("link", { name: "Commish" }));

  await waitFor(() => expect(window.location.pathname).toBe("/commish/draft"));
  expect(within(nav()).getByRole("link", { name: "Draft" })).toHaveAttribute(
    "aria-current",
    "page",
  );
  expect(
    within(nav()).queryByRole("link", { name: "Today" }),
  ).not.toBeInTheDocument();
  expect(calls.some((url) => url.includes("/api/admin"))).toBe(false);
});

test.each([
  ["/setup", "/commish/setup", "Season setup"],
  ["/draft", "/commish/draft", "Draft"],
  [`/weeks/${weekId}`, `/commish/weeks/${weekId}`, "Today"],
])(
  "the old address %s still works and moves to %s",
  async (from, to, current) => {
    open(from, { role: "Admin" });

    await waitFor(() => expect(window.location.pathname).toBe(to));
    expect(within(nav()).getByRole("link", { name: current })).toHaveAttribute(
      "aria-current",
      "page",
    );
  },
);

test("an administrator opening a week from the commish area reaches the week workspace", async () => {
  open(`/commish/weeks/${weekId}`, { role: "Admin" });

  await waitFor(() => expect(calls).toContain(`/api/admin/weeks/${weekId}`));
});

test("a coach without commissioner access cannot reach a commish page by address", async () => {
  open("/commish/setup", { role: "Coach", setupAccess: "none" });

  await screen.findByRole("heading", { level: 1, name: "Your week" });
  expect(
    calls.some(
      (url) => url.includes("/api/admin") || url.includes("/api/commissioner"),
    ),
  ).toBe(false);
});

test("the coach draft page is called Draft in coach mode", async () => {
  open("/roster", { role: "Coach", setupAccess: "none" });

  await waitFor(() => expect(calls).toContain("/api/coach/roster"));
  expect(within(nav()).getByRole("link", { name: "Draft" })).toHaveAttribute(
    "aria-current",
    "page",
  );
});

test("the pick desk has its own address in coach mode", async () => {
  open("/picks", { role: "Coach", setupAccess: "none" });

  await waitFor(() => expect(calls).toContain("/api/coach/picks/current"));
  expect(window.location.pathname).toBe("/picks");
  expect(within(nav()).getByRole("link", { name: "Picks" })).toHaveAttribute(
    "aria-current",
    "page",
  );
});
