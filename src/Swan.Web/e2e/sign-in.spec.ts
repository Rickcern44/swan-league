import { execSync } from "node:child_process";
import { expect, test } from "@playwright/test";

const mailpitUrl = process.env.E2E_MAILPIT_URL;
// There is no delete endpoint, so a run that had to create the Setup season (or fill in its teams) removes its traces with a command, e.g.
// E2E_RESET_SETUP_SEASON_CMD: psql -c 'delete from schedule.season_teams where "SeasonId" in (select "Id" from league.seasons where "State" = 0); delete from league.seasons where "State" = 0'
// E2E_RESET_SETUP_TEAMS_CMD:  psql -c 'delete from schedule.season_teams where "SeasonId" in (select "Id" from league.seasons where "State" = 0)'
// Run only against a dedicated test database.
const resetSetupSeason = process.env.E2E_RESET_SETUP_SEASON_CMD;
const resetSetupTeams = process.env.E2E_RESET_SETUP_TEAMS_CMD;

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
        setupStep: number;
      })
    : undefined;
  const teamsBefore = original
    ? (
        (await (
          await page.request.get("/api/admin/seasons/setup/teams")
        ).json()) as {
          teams: { teamId: string; finishPosition: number | null }[];
        }
      ).teams
    : [];
  let leftBehind = false;
  try {
    // The seeded database may or may not already hold a Setup season.
    const start = page.getByRole("form", { name: "Start a new season" });
    if (await start.isVisible()) {
      await start.getByLabel("Season year").fill("2027");
      await start.getByLabel("Season name").fill("2027 Swan League");
      await start.getByRole("button", { name: "Create season" }).click();
    } else {
      // Begin at the first step whatever progress the existing season had.
      await page.request.put("/api/admin/seasons/setup/step", {
        data: { step: 0 },
      });
      await page.reload();
    }

    const basics = page.getByRole("form", { name: "Basics" });
    await expect(basics).toBeVisible();
    await expect(page.getByText("Step 1 of 4")).toBeVisible();
    await expect(page.getByRole("form", { name: "Money" })).toHaveCount(0);
    await basics.getByLabel("Season name").fill("E2E Swan League");
    await basics.getByLabel("Simulation season").check();
    await basics.getByRole("button", { name: "Save & continue" }).click();

    // Closing the browser and coming back opens the saved step.
    const money = page.getByRole("form", { name: "Money" });
    await expect(money).toBeVisible();
    await expect(page.getByText("Step 2 of 4")).toBeVisible();
    await page.reload();
    await expect(money).toBeVisible();
    await expect(page.getByText("Step 2 of 4")).toBeVisible();

    // Back navigates without saving; reloading still resumes at Money.
    await money.getByRole("button", { name: "Back" }).click();
    await expect(basics).toBeVisible();
    await expect(basics.getByLabel("Season name")).toHaveValue(
      "E2E Swan League",
    );
    await page.reload();
    await expect(money).toBeVisible();

    await money.getByLabel("Playoff").fill("5");
    await money.getByRole("button", { name: "Save & continue" }).click();
    await expect(money.getByRole("alert")).toContainText("add up");
    await expect(money).toBeVisible();

    await money.getByLabel("Buy-in").fill("150");
    await money.getByLabel("Main", { exact: true }).fill("90");
    await money.getByLabel("Dog").fill("30");
    await money.getByLabel("Regular Season").fill("15");
    await money.getByLabel("Playoff").fill("15");
    await expect(money.getByText("Pool total $150.00")).toBeVisible();
    await money.getByRole("button", { name: "Save & continue" }).click();

    const deadline = page.getByRole("form", { name: "Weekly deadline" });
    await expect(deadline).toBeVisible();
    await expect(page.getByText("Step 3 of 4")).toBeVisible();
    await deadline.getByLabel("Team picks lock Sundays at").fill("11:30");
    await deadline.getByRole("button", { name: "Save & continue" }).click();

    const teams = page.getByRole("form", { name: "Teams and point values" });
    await expect(teams).toBeVisible();
    await expect(page.getByText("Step 4 of 4")).toBeVisible();

    // Each division, one team per finish (wait for the catalog to load first: all() does not).
    await expect(teams.getByRole("group")).toHaveCount(8);
    const east = teams.getByRole("group", { name: "AFC East" });
    for (const group of await teams.getByRole("group").all()) {
      const selects = await group.getByRole("combobox").all();
      for (const [index, select] of selects.entries())
        await select.selectOption(String(index + 1));
    }

    // Two teams on the same finish in one division are explained and nothing is saved.
    await east.getByRole("combobox").nth(1).selectOption("1");
    await teams.getByRole("button", { name: "Save & continue" }).click();
    await expect(teams.getByRole("alert")).toContainText(
      "AFC East needs exactly one team at each of 1st, 2nd, 3rd and 4th.",
    );
    await east.getByRole("combobox").nth(1).selectOption("2");

    await expect(east.getByText("Point value 3")).toBeVisible();
    await teams.getByRole("button", { name: "Save & continue" }).click();
    await expect(teams.getByText("Teams saved")).toBeVisible();

    // Schedule import is only listed, never reachable.
    await expect(page.getByText(/Coming next/)).toContainText(
      "Schedule import",
    );
    await expect(
      page.getByRole("button", { name: /schedule|weeks/i }),
    ).toHaveCount(0);

    // Reloading opens the saved Teams page with the saved finishes.
    await page.reload();
    await expect(teams).toBeVisible();
    await expect(page.getByText("Step 4 of 4")).toBeVisible();
    await expect(
      teams
        .getByRole("group", { name: "NFC West" })
        .getByRole("combobox")
        .nth(3),
    ).toHaveValue("4");
    await teams.getByRole("button", { name: "Back" }).click();
    await expect(deadline.getByLabel("Team picks lock Sundays at")).toHaveValue(
      "11:30",
    );
    await deadline.getByRole("button", { name: "Back" }).click();
    await expect(money.getByLabel("Buy-in")).toHaveValue("150");
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
      if (
        teamsBefore.length &&
        teamsBefore.every((team) => team.finishPosition)
      )
        await put("/api/admin/seasons/setup/teams", {
          teams: teamsBefore.map((team) => ({
            teamId: team.teamId,
            finishPosition: team.finishPosition,
          })),
        });
      else if (resetSetupTeams) execSync(resetSetupTeams, { stdio: "inherit" });
      else leftBehind = true;
      await put("/api/admin/seasons/setup/step", { step: original.setupStep });
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
    "The test left a Setup season or team finishes behind: set E2E_RESET_SETUP_SEASON_CMD and E2E_RESET_SETUP_TEAMS_CMD against a dedicated test database (see the top of this file).",
  ).toBe(false);
});
