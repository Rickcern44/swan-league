import { execSync } from "node:child_process";
import { expect, test } from "@playwright/test";

const mailpitUrl = process.env.E2E_MAILPIT_URL;
// There is no delete endpoint, so a run that had to create the Setup season (or fill in its teams) removes its traces with a command, e.g.
// E2E_RESET_SETUP_SEASON_CMD: psql -c 'delete from schedule.season_teams where "SeasonId" in (select "Id" from league.seasons where "State" = 0); delete from league.seasons where "State" = 0'
// E2E_RESET_SETUP_TEAMS_CMD:  psql -c 'delete from schedule.season_teams where "SeasonId" in (select "Id" from league.seasons where "State" = 0)'
// Run only against a dedicated test database.
const resetSetupSeason = process.env.E2E_RESET_SETUP_SEASON_CMD;
const resetSetupTeams = process.env.E2E_RESET_SETUP_TEAMS_CMD;
// A Setup season that had no fines and payouts configured is put back with, e.g.
// psql -c 'update league.seasons set "PayoutPlans" = (chr(91) || chr(93)), "MissedPickFine" = 5.00 where "State" = 0'
const resetSetupPayouts = process.env.E2E_RESET_SETUP_PAYOUTS_CMD;
// The Coaches section adds a person and memberships to the Setup season; this removes them (and so assumes the Setup season had no coaches), e.g.
// psql -c 'delete from league.memberships where "SeasonId" in (select "Id" from league.seasons where "State" = 0); delete from identity.users where "Email" like (chr(101) || chr(50) || chr(101) || chr(37))'
// (chr(101) || chr(50) || chr(101) || chr(37)) is e2e%, which avoids nested shell quotes.
const resetSetupCoaches = process.env.E2E_RESET_SETUP_COACHES_CMD;
const e2eCoachEmail = "e2e-new-coach@example.com";

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
  type Finesplan = {
    configured: boolean;
    missedPickFine: number | null;
    pools: { pool: string; places: { position: number; percent: number }[] }[];
  };
  const finesBefore = original
    ? ((await (
        await page.request.get("/api/admin/seasons/setup/fines-and-payouts")
      ).json()) as Finesplan)
    : undefined;
  const rulesBefore = original
    ? await (
        await page.request.get("/api/admin/seasons/setup/league-settings")
      ).json()
    : undefined;
  const coachesBefore = original
    ? (
        (await (
          await page.request.get("/api/admin/seasons/setup/coaches")
        ).json()) as { coaches: unknown[] }
      ).coaches
    : [];
  let coachesTouched = false;
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
    await expect(page.getByText("Step 1 of 9")).toBeVisible();
    await expect(page.getByRole("form", { name: "Money" })).toHaveCount(0);
    await basics.getByLabel("Season name").fill("E2E Swan League");
    await basics.getByLabel("Simulation season").check();
    await basics.getByRole("button", { name: "Save & continue" }).click();

    // Closing the browser and coming back opens the saved step.
    const money = page.getByRole("form", { name: "Money" });
    await expect(money).toBeVisible();
    await expect(page.getByText("Step 2 of 9")).toBeVisible();
    await page.reload();
    await expect(money).toBeVisible();
    await expect(page.getByText("Step 2 of 9")).toBeVisible();

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
    await expect(page.getByText("Step 3 of 9")).toBeVisible();
    await deadline.getByLabel("Team picks lock Sundays at").fill("11:30");
    await deadline.getByRole("button", { name: "Save & continue" }).click();

    const teams = page.getByRole("form", { name: "Teams and point values" });
    await expect(teams).toBeVisible();
    await expect(page.getByText("Step 4 of 9")).toBeVisible();

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

    // Saving the teams moves on to Schedule import, which only previews: the provider is contacted on demand and nothing is imported here.
    const schedule = page.getByRole("form", { name: "Schedule import" });
    await expect(schedule).toBeVisible();
    await expect(page.getByText("Step 5 of 9")).toBeVisible();
    await expect(
      schedule.getByText(/Nothing is saved until you confirm/),
    ).toBeVisible();
    await expect(page.getByText(/Coming next/)).toContainText("Coaches");
    await expect(page.getByRole("button", { name: /coaches/i })).toHaveCount(0);
    await schedule.getByRole("button", { name: "Preview schedule" }).click();
    // Whatever the provider says (a schedule, nothing published yet, or no key configured) is shown, with an explicit import step only if games came back.
    await expect(
      schedule
        .getByText(/No schedule has been published|Week 1|provider|configured/)
        .first(),
    ).toBeVisible();
    const savedSchedule = await page.request.get(
      "/api/admin/seasons/setup/schedule",
    );
    expect((await savedSchedule.json()).imported).toBe(false);

    // Reloading opens the saved Schedule page; Back returns to Teams with the saved finishes.
    await page.reload();
    await expect(schedule).toBeVisible();
    await expect(page.getByText("Step 5 of 9")).toBeVisible();

    // Weeks and dogs reads only the imported schedule: with none imported it says so instead of listing weeks.
    await page.request.put("/api/admin/seasons/setup/step", {
      data: { step: 5 },
    });
    await page.reload();
    const dogs = page.getByRole("form", { name: "Weeks and dogs" });
    await expect(dogs).toBeVisible();
    await expect(page.getByText("Step 6 of 9")).toBeVisible();
    await expect(dogs.getByRole("alert")).toContainText(
      "Import the full regular-season schedule",
    );
    await expect(dogs.getByRole("checkbox")).toHaveCount(0);
    await dogs.getByRole("button", { name: "Back" }).click();
    await expect(schedule).toBeVisible();

    // Fines and payouts needs no schedule: default to $5 and 100% first place, add a second placing, save, and see it again after a reload.
    await page.request.put("/api/admin/seasons/setup/step", {
      data: { step: 6 },
    });
    await page.reload();
    const fines = page.getByRole("form", { name: "Fines and payouts" });
    await expect(fines).toBeVisible();
    await expect(page.getByText("Step 7 of 9")).toBeVisible();
    await expect(fines.getByText(/^\d+ active coaches?$/)).toBeVisible();
    await expect(fines.getByLabel("Main 1st place percent")).toHaveValue(
      /^100/,
    );
    await fines.getByLabel("Missed team-pick fine").fill("6");
    await fines.getByRole("button", { name: "Add Main placing" }).click();
    await fines.getByLabel("Main 1st place percent").fill("70");
    await fines.getByLabel("Main 2nd place percent").fill("20");
    await expect(
      fines.getByText("Main total 90.00% — must be 100.00%"),
    ).toBeVisible();
    await fines.getByRole("button", { name: "Save & continue" }).click();
    await expect(fines.getByRole("alert")).toContainText("Main");
    await fines.getByLabel("Main 2nd place percent").fill("30");
    await expect(fines.getByText("Main total 100.00%")).toBeVisible();
    await fines.getByRole("button", { name: "Save & continue" }).click();

    // Saving moves on to League settings: defaults first, then change R17, R18 and R19 and see them again after a reload.
    const rules = page.getByRole("form", { name: "League settings" });
    await expect(rules).toBeVisible();
    await expect(page.getByText("Step 8 of 9")).toBeVisible();
    await expect(rules.getByLabel(/R17/)).toBeChecked();
    await expect(rules.getByLabel("1 point color")).toHaveValue("Green");
    await expect(rules.getByLabel("First occurrence")).toBeChecked();
    await expect(
      rules.getByRole("list", { name: "Fixed rules" }),
    ).toContainText("Each coach has exactly 9 NFL teams");
    await rules.getByLabel(/R17/).uncheck();
    await rules.getByLabel("2 point color").selectOption("Green");
    await expect(
      rules.getByText("Each color can be used for only one point value."),
    ).toBeVisible();
    await rules.getByRole("button", { name: "Save & continue" }).click();
    await expect(rules.getByRole("alert")).toContainText(
      "only one point value",
    );
    await rules.getByLabel("1 point color").selectOption("Blue");
    await rules.getByLabel("Most occurrences").check();
    await rules.getByRole("button", { name: "Save & continue" }).click();

    // Saving moves on to Coaches: the seeded demo season offers its coaches to carry forward; nothing is sent or created until saved.
    const coaches = page.getByRole("form", { name: "Coaches" });
    await expect(coaches).toBeVisible();
    await expect(page.getByText("Step 9 of 9")).toBeVisible();
    await expect(page.getByText(/Coming next/)).toContainText(
      "Review and create",
    );
    await expect(
      coaches.getByText(/No invitations or emails are sent/),
    ).toBeVisible();
    if (coachesBefore.length === 0) {
      await expect(coaches.getByText("0 coaches selected")).toBeVisible();
      await expect(coaches.getByText(/No coaches yet/)).toBeVisible();
      await coaches.getByLabel(/^Coach 1 \(coach1@swan\.local\)$/).check();
      await coaches.getByLabel(/^Coach 2 \(coach2@swan\.local\)$/).check();
      await coaches.getByRole("button", { name: "Add coach" }).click();
      await coaches.getByLabel("New coach 1 email").fill("COACH1@swan.local");
      await coaches.getByLabel("New coach 1 name").fill("Dup");
      await coaches.getByRole("button", { name: "Save & continue" }).click();
      await expect(coaches.getByRole("alert")).toContainText("more than once");
      coachesTouched = true;
      await coaches.getByLabel("New coach 1 email").fill(e2eCoachEmail);
      await coaches.getByLabel("New coach 1 name").fill("E2E New Coach");
      await expect(coaches.getByText("3 coaches selected")).toBeVisible();
      await coaches.getByRole("button", { name: "Save & continue" }).click();
      await expect(coaches.getByText("Coaches saved")).toBeVisible();
      await page.reload();
      await expect(coaches).toBeVisible();
      await expect(coaches.getByText("3 coaches selected")).toBeVisible();
      await expect(
        coaches.getByLabel(/^Coach 1 \(coach1@swan\.local\)$/),
      ).toBeChecked();
      await expect(
        coaches.getByText(`E2E New Coach (${e2eCoachEmail})`),
      ).toBeVisible();
      // The payout calculator counts the coaches saved here.
      await coaches.getByRole("button", { name: "Back" }).click();
      await rules.getByRole("button", { name: "Back" }).click();
      await expect(fines.getByText("3 active coaches")).toBeVisible();
      await fines.getByRole("button", { name: "Save & continue" }).click();
      await expect(rules).toBeVisible();
    }
    await page.reload();
    await expect(coaches).toBeVisible();
    await coaches.getByRole("button", { name: "Back" }).click();
    await expect(rules).toBeVisible();
    await expect(rules.getByLabel(/R17/)).not.toBeChecked();
    await expect(rules.getByLabel("1 point color")).toHaveValue("Blue");
    await expect(rules.getByLabel("2 point color")).toHaveValue("Green");
    await expect(rules.getByLabel("Most occurrences")).toBeChecked();
    await rules.getByRole("button", { name: "Back" }).click();
    await expect(fines).toBeVisible();
    await expect(fines.getByLabel("Missed team-pick fine")).toHaveValue("6.00");
    await expect(fines.getByLabel("Main 2nd place percent")).toHaveValue(
      "30.00",
    );
    await fines.getByRole("button", { name: "Back" }).click();
    await expect(dogs).toBeVisible();
    await dogs.getByRole("button", { name: "Back" }).click();
    await expect(schedule).toBeVisible();
    await schedule.getByRole("button", { name: "Back" }).click();
    await expect(teams).toBeVisible();
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
    if (coachesTouched) {
      // Memberships and the created person are not removed by deleting the season, so remove them first.
      if (resetSetupCoaches) execSync(resetSetupCoaches, { stdio: "inherit" });
      else leftBehind = true;
    }
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
      if (finesBefore?.configured)
        await put("/api/admin/seasons/setup/fines-and-payouts", {
          missedPickFine: finesBefore.missedPickFine,
          payouts: finesBefore.pools.map((pool) => ({
            pool: pool.pool,
            places: pool.places.map((place) => ({
              position: place.position,
              percent: place.percent,
            })),
          })),
        });
      else if (resetSetupPayouts)
        execSync(resetSetupPayouts, { stdio: "inherit" });
      else leftBehind = true;
      if (rulesBefore)
        await put("/api/admin/seasons/setup/league-settings", {
          teamMayAlsoBeDog: rulesBefore.teamMayAlsoBeDog,
          pointColors: rulesBefore.pointColors,
          recordHolder: rulesBefore.recordHolder,
        });
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
    "The test left a Setup season, team finishes, payout settings or coaches behind: set E2E_RESET_SETUP_SEASON_CMD, E2E_RESET_SETUP_TEAMS_CMD, E2E_RESET_SETUP_PAYOUTS_CMD and E2E_RESET_SETUP_COACHES_CMD against a dedicated test database (see the top of this file).",
  ).toBe(false);
});
