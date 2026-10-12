import { render, screen } from "@testing-library/react";
import { beforeEach, expect, test, vi } from "vitest";
import App from "./App";

beforeEach(() => {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockResolvedValue({
      json: async () => ({ message: "pong" }),
      ok: true,
    }),
  );
});

test("renders the response from the API", async () => {
  render(<App />);

  expect(await screen.findByText("API response: pong")).toBeInTheDocument();
});
