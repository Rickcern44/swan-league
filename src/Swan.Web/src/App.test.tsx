import {
  act,
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { afterEach, beforeEach, expect, test, vi } from "vitest";
import App from "./App";

type Reply = {
  ok: boolean;
  status: number;
  headers?: Headers;
  json?: () => Promise<unknown>;
};
let linkReplies: (Reply | "network-error")[];
let calls: { url: string; method: string; body?: unknown }[];
let redeem: Reply;

function stub() {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string, init?: RequestInit) => {
      calls.push({
        url,
        method: init?.method ?? "GET",
        body: init?.body ? JSON.parse(init.body as string) : undefined,
      });
      if (url === "/api/sign-in-links") {
        const reply = linkReplies.shift() ?? { ok: true, status: 200 };
        if (reply === "network-error") throw new Error("offline");
        return { headers: new Headers(), json: async () => ({}), ...reply };
      }
      if (url.startsWith("/api/sign-in-links/redeem")) return redeem;
      return { ok: false, status: 401, json: async () => ({}) };
    }),
  );
}

beforeEach(() => {
  linkReplies = [];
  calls = [];
  redeem = { ok: true, status: 200 };
  stub();
  window.history.replaceState({}, "", "/");
});
afterEach(() => {
  vi.useRealTimers();
  cleanup();
});

const limited = (seconds: number): Reply => ({
  ok: false,
  status: 429,
  headers: new Headers({ "Retry-After": String(seconds) }),
  json: async () => ({ retryAfterSeconds: seconds }),
});
const send = async (email = "coach@example.com") => {
  const input = await screen.findByLabelText("Email");
  fireEvent.change(input, { target: { value: email } });
  fireEvent.click(
    screen.getByRole("button", { name: "Email me a sign-in link" }),
  );
};
const tick = (seconds: number) =>
  act(() => {
    vi.advanceTimersByTime(seconds * 1000);
  });

test("the sign-in screen explains the one-time link and who can sign in", async () => {
  render(<App />);

  expect(
    await screen.findByRole("heading", { level: 1, name: "Sign in" }),
  ).toBeVisible();
  expect(screen.getByLabelText("Email")).toHaveAttribute(
    "autocomplete",
    "email",
  );
  expect(screen.getByText(/one-time link/)).toBeVisible();
  expect(
    screen.getByText(
      "Only coaches the commissioner has added to the league can sign in.",
    ),
  ).toBeVisible();
  expect(document.title).toBe("Sign in · Swan League");
});

test("sending a link moves to check your email, worded so it never confirms the address is known", async () => {
  render(<App />);
  await send("coach@example.com");

  const heading = await screen.findByRole("heading", {
    level: 1,
    name: "Check your email",
  });
  await waitFor(() => expect(heading).toHaveFocus());
  expect(screen.getByRole("status")).toHaveTextContent(
    "If coach@example.com belongs to a coach in the league, a sign-in link is on its way.",
  );
  expect(calls.find((c) => c.url === "/api/sign-in-links")).toMatchObject({
    method: "POST",
    body: { email: "coach@example.com" },
  });
  expect(document.title).toBe("Check your email · Swan League");
});

test("another link can be sent once the minute has passed", async () => {
  vi.useFakeTimers({ toFake: ["setInterval", "clearInterval"] });
  render(<App />);
  await send();
  await screen.findByRole("heading", { name: "Check your email" });

  const again = () =>
    screen.getByRole("button", { name: /^Send another link/ });
  expect(again()).toBeDisabled();
  expect(again()).toHaveTextContent("Send another link in 60s");
  tick(18);
  expect(again()).toHaveTextContent("Send another link in 42s");
  tick(42);
  expect(again()).toBeEnabled();
  expect(again()).toHaveTextContent(/^Send another link$/);

  fireEvent.click(again());
  await waitFor(() =>
    expect(calls.filter((c) => c.url === "/api/sign-in-links")).toHaveLength(2),
  );
  await waitFor(() => expect(again()).toBeDisabled());
});

test("a rate-limited request shows a countdown and keeps the button off until it ends", async () => {
  vi.useFakeTimers({ toFake: ["setInterval", "clearInterval"] });
  linkReplies = [limited(42)];
  render(<App />);
  await send();

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "A link was just sent to this address. You can ask for another in 0:42.",
  );
  const button = screen.getByRole("button", {
    name: "Email me a sign-in link",
  });
  expect(button).toBeDisabled();
  tick(30);
  expect(screen.getByRole("alert")).toHaveTextContent("in 0:12");
  tick(12);
  expect(screen.queryByRole("alert")).toBeNull();
  expect(button).toBeEnabled();
});

test("use a different email goes back to the form", async () => {
  render(<App />);
  await send("first@example.com");
  await screen.findByRole("heading", { name: "Check your email" });

  fireEvent.click(
    screen.getByRole("button", { name: "Use a different email" }),
  );

  expect(
    await screen.findByRole("heading", { level: 1, name: "Sign in" }),
  ).toBeVisible();
  expect(screen.getByLabelText("Email")).toHaveValue("first@example.com");
});

test("an unreachable server is said plainly and nothing moves on", async () => {
  linkReplies = ["network-error"];
  render(<App />);
  await send();

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "Could not reach the server. Check your connection and try again.",
  );
  expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent(
    "Sign in",
  );
});

test("a link that no longer works brings back the form with an explanation", async () => {
  redeem = { ok: false, status: 400 };
  window.history.replaceState({}, "", "/signin-callback?token=ABC");
  render(<App />);

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "That link didn't work. It may have expired or already been used. Enter your email for a new one.",
  );
  expect(window.location.pathname).toBe("/signin");
  expect(
    screen.getByRole("heading", { level: 1, name: "Sign in" }),
  ).toBeVisible();
});

test("opening a link says it is signing you in", async () => {
  let release: (reply: Reply) => void = () => {};
  vi.stubGlobal(
    "fetch",
    vi.fn((url: string) =>
      url.startsWith("/api/sign-in-links/redeem")
        ? new Promise<Reply>((resolve) => {
            release = resolve;
          })
        : Promise.resolve({ ok: false, status: 401 }),
    ),
  );
  window.history.replaceState({}, "", "/signin-callback?token=ABC");
  render(<App />);

  expect(
    await screen.findByRole("heading", { level: 1, name: "Signing you in…" }),
  ).toBeVisible();
  expect(screen.getByRole("status")).toHaveAttribute("aria-busy", "true");
  release({ ok: false, status: 400 });
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "That link didn't work.",
  );
});
