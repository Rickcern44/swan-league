import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";
import PageState from "./PageState";

afterEach(cleanup);

test("loading is announced as a busy status with its message", () => {
  render(<PageState kind="loading" message="Loading your week…" />);

  const status = screen.getByRole("status");
  expect(status).toHaveAttribute("aria-busy", "true");
  expect(status).toHaveTextContent("Loading your week…");
  expect(status).toHaveClass("page-state", "loading");
});

test("an error is an alert with its title, the reason and Try again", () => {
  const retry = vi.fn();
  render(
    <PageState
      kind="error"
      title="Could not load your week."
      message="The server answered 500."
      onRetry={retry}
    />,
  );

  const alert = screen.getByRole("alert");
  expect(alert).toHaveTextContent(
    "Could not load your week. The server answered 500.",
  );
  fireEvent.click(screen.getByRole("button", { name: "Try again" }));
  expect(retry).toHaveBeenCalledOnce();
});

test("an error without a retry offers no button", () => {
  render(<PageState kind="error" title="Could not load." message="Nope." />);

  expect(screen.queryByRole("button")).toBeNull();
});

test("an empty state has a heading, its message and any actions", () => {
  render(
    <PageState kind="empty" title="Nothing to pick yet">
      <p>The week opens once the lines are published.</p>
      <button type="button">Check again</button>
    </PageState>,
  );

  expect(
    screen.getByRole("heading", { level: 2, name: "Nothing to pick yet" }),
  ).toBeVisible();
  expect(
    screen.getByText("The week opens once the lines are published."),
  ).toBeVisible();
  expect(screen.getByRole("button", { name: "Check again" })).toBeVisible();
  expect(screen.queryByRole("alert")).toBeNull();
});

test("an empty state can be marked as a status for messages that change", () => {
  render(
    <PageState kind="empty" title="The draft has not opened yet" live>
      <p>Come back once the commissioner opens it.</p>
    </PageState>,
  );

  expect(screen.getByRole("status")).toHaveTextContent(
    "The draft has not opened yet",
  );
});
