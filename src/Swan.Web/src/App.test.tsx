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
