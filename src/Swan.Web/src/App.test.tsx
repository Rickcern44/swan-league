import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";
import App from "./App";

beforeEach(() => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: false }));
  window.history.replaceState({}, "", "/");
});

test("shows an email sign-in form", async () => {
  render(<App />);

  expect(await screen.findByLabelText("Email")).toBeInTheDocument();
  expect(
    screen.getByRole("button", { name: "Send sign-in link" }),
  ).toBeInTheDocument();
});

test("shows the check-email state after submitting", async () => {
  render(<App />);
  const input = await screen.findByLabelText("Email");
  fireEvent.change(input, { target: { value: "admin@swan.local" } });
  fireEvent.submit(input.closest("form")!);

  expect(await screen.findByText("Check your email")).toBeInTheDocument();
});

test("explains when a recent sign-in request is still rate limited", async () => {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockResolvedValue({
      ok: false,
      status: 429,
      headers: new Headers({ "Retry-After": "42" }),
    }),
  );
  render(<App />);

  const input = await screen.findByLabelText("Email");
  fireEvent.change(input, { target: { value: "admin@swan.local" } });
  fireEvent.submit(input.closest("form")!);

  expect(await screen.findByRole("alert")).toHaveTextContent(
    "A sign-in link was recently sent. Try again in 42 seconds.",
  );
});
