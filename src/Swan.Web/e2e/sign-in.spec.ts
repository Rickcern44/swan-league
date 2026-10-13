import { execSync } from "node:child_process";
import { expect, test } from "@playwright/test";

const mailpitUrl = process.env.E2E_MAILPIT_URL;
// There is no delete endpoint; this command removes the Setup season row so a run that had to create one leaves no trace, e.g.
// podman exec <postgres container> psql -U postgres -d swan -c 'delete from league.seasons where "State" = 0'
const resetSetupSeason = process.env.E2E_RESET_SETUP_SEASON_CMD;

async function signIn(page: import("@playwright/test").Page, email: string) {
  if (!mailpitUrl)
    throw new Error("E2E_MAILPIT_URL must point to Mailpit's HTTP endpoint.");
  await page.request.delete(`${mailpitUrl}/api/v1/messages`);
  await page.goto("/");
  await page.getByLabel("Email").fill(email);
  await page.getByRole("button", { name: "Send sign-in link" }).click();
  await expect(page.getByText("Check your email")).toBeVisible();

  let link = "";
  await expect
    .poll(async () => {
      const response = await page.request.get(`${mailpitUrl}/api/v1/messages`);
      const messages = await response.text();
      const matching = messages.match(
        new RegExp(
          `https?://[^\\s"]+/signin-callback\\?token=[A-F0-9]+(?=[\\s"<])`,
          "i",
        ),
      );
      link = matching?.[0] ?? "";
      return link;
    })
    .toMatch(/signin-callback\?token=/);

  await page.goto(link);
}

// Sign-in links are limited to one per address per minute, so each address signs in once.
test("seeded admin sets up the season and coach sees no admin controls", async ({
  page,
}) => {
  await signIn(page, "admin@swan.local");
  await expect(page.getByText("Signed in as Swan Admin · Admin")).toBeVisible();
  await expect(
    page.getByRole("heading", { name: "Season setup" }),
  ).toBeVisible();

  // The test puts back the Setup season it found, or removes the one it had to create (no delete endpoint exists, so that needs the reset command).
  const existing = await page.request.get("/api/admin/seasons/setup");
  const original = existing.ok()
    ? ((await existing.json()) as {
        year: number;
        name: string;
        isSimulation: boolean;
        settings: {
          buyIn: { amount: number };
          teamPickDeadlineEastern: string | null;
        };
        pools: { name: string; stake: { amount: number } }[];
      })
    : undefined;
  let leftBehind = false;
  try {
    // The seeded database may or may not already hold a Setup season.
    const start = page.getByRole("form", { name: "Start a new season" });
    if (await start.isVisible()) {
      await start.getByLabel("Season year").fill("2027");
      await start.getByLabel("Season name").fill("2027 Swan League");
      await start.getByRole("button", { name: "Create season" }).click();
    }

    const basics = page.getByRole("form", { name: "Basics" });
    await expect(basics).toBeVisible();
    await basics.getByLabel("Season name").fill("E2E Swan League");
    await basics.getByLabel("Simulation season").check();
    await basics.getByRole("button", { name: "Save basics" }).click();
    await expect(basics.getByText("Basics saved")).toBeVisible();

    const money = page.getByRole("form", { name: "Money" });
    await money.getByLabel("Buy-in").fill("150");
    await money.getByLabel("Main", { exact: true }).fill("90");
    await money.getByLabel("Dog").fill("30");
    await money.getByLabel("Regular Season").fill("15");
    await money.getByLabel("Playoff").fill("15");
    await expect(money.getByText("Pool total $150.00")).toBeVisible();
    await money.getByRole("button", { name: "Save money" }).click();
    await expect(money.getByText("Money saved")).toBeVisible();

    const deadline = page.getByRole("form", { name: "Weekly deadline" });
    await deadline.getByLabel("Team picks lock Sundays at").fill("11:30");
    await deadline.getByRole("button", { name: "Save deadline" }).click();
    await expect(deadline.getByText("Deadline saved")).toBeVisible();

    await money.getByLabel("Playoff").fill("5");
    await money.getByRole("button", { name: "Save money" }).click();
    await expect(money.getByRole("alert")).toContainText("add up");

    await page.reload();
    await expect(page.getByLabel("Season name")).toHaveValue("E2E Swan League");
    await expect(page.getByLabel("Buy-in")).toHaveValue("150");
    await expect(page.getByLabel("Team picks lock Sundays at")).toHaveValue(
      "11:30",
    );
  } finally {
    if (!original) {
      // The test created the season, so leaving it would change what the next run finds.
      if (resetSetupSeason) execSync(resetSetupSeason, { stdio: "inherit" });
      else leftBehind = true;
    } else {
      const stake = (name: string) =>
        original.pools.find((pool) => pool.name === name)!.stake.amount;
      const put = (url: string, data: unknown) =>
        page.request.put(url, { data });
      await put("/api/admin/seasons/setup", {
        year: original.year,
        name: original.name,
        isSimulation: original.isSimulation,
      });
      await put("/api/admin/seasons/setup/money", {
        buyIn: original.settings.buyIn.amount,
        main: stake("Main"),
        dog: stake("Dog"),
        regularSeason: stake("Regular Season"),
        playoff: stake("Playoff"),
      });
      await put("/api/admin/seasons/setup/deadline", {
        teamPickDeadlineEastern:
          original.settings.teamPickDeadlineEastern ?? "13:00:00",
      });
    }
  }

  await page.getByRole("button", { name: "Sign out" }).click();
  await expect(page.getByLabel("Email")).toBeVisible();

  await signIn(page, "coach1@swan.local");
  await expect(page.getByText("Signed in as Coach 1 · Coach")).toBeVisible();
  await expect(page.getByText("Season setup")).not.toBeVisible();

  // Checked last so it never hides a real failure above.
  expect(
    leftBehind,
    "The test created a Setup season and cannot remove it: set E2E_RESET_SETUP_SEASON_CMD (see the top of this file).",
  ).toBe(false);
});
