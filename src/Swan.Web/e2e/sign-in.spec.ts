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

  // The admin lands on the Week Dashboard, which shows what the read-only API says rather than anything invented.
  const dashboard = (await (
    await page.request.get("/api/admin/dashboard")
  ).json()) as {
    season: { name: string } | null;
    currentWeek: { id: string; number: number; state: string } | null;
    attention: { kind: string; title: string; reason: string }[];
  };
  await expect(page.getByRole("navigation", { name: "Primary" })).toBeVisible();
  if (!dashboard.season)
    await expect(
      page.getByRole("heading", { name: "No season is live" }),
    ).toBeVisible();
  else {
    await expect(page.getByText(dashboard.season.name).first()).toBeVisible();
    if (dashboard.currentWeek)
      await expect(
        page.getByRole("heading", {
          level: 1,
          name: `Week ${dashboard.currentWeek.number}`,
        }),
      ).toBeVisible();
    for (const item of dashboard.attention.slice(0, 3))
      await expect(page.getByText(item.reason).first()).toBeVisible();
  }

  // The Week Workspace walk consumes the seeded Draft week, so it runs only against a fresh dedicated database (a used one is already past Draft).
  const fresh =
    dashboard.currentWeek?.state === "Draft" &&
    dashboard.attention[0]?.kind === "lines-missing";
  if (fresh) {
    const weekUrl = `/api/admin/weeks/${dashboard.currentWeek!.id}`;
    await page.getByRole("link", { name: /Lines need approval/ }).click();
    await expect(
      page.getByRole("heading", { level: 1, name: /^Week \d+$/ }),
    ).toBeVisible();
    await expect(page).toHaveURL(
      new RegExp(`/weeks/${dashboard.currentWeek!.id}$`),
    );
    const dock = page.getByRole("region", { name: "Next action" });
    await expect(dock).toContainText("still needs an approved line");

    // Draft: add a line and approve it in one explicit step, then approve the week's lines.
    await page
      .getByRole("button", { name: /^Add line/ })
      .first()
      .click();
    // Out-of-range or odd numbers are refused beside the editor before anything is saved.
    for (const bad of ["35", "1e3"]) {
      await page.getByLabel(/^Spread for/).fill(bad);
      await page.getByRole("button", { name: "Save and approve" }).click();
      await expect(page.getByRole("alert")).toContainText("from -30 to 30");
    }
    await page.getByLabel(/^Spread for/).fill("-3.5");
    await page.getByRole("button", { name: "Save and approve" }).click();
    await expect(page.getByText(/-3\.5 · approved/)).toBeVisible();
    await dock.getByRole("button", { name: /^Approve \d+ lines?$/ }).click();
    await expect(
      dock.getByRole("button", { name: "Publish week" }),
    ).toBeVisible();

    // LinesApproved -> Open.
    await dock.getByRole("button", { name: "Publish week" }).click();
    await expect(dock.getByRole("button", { name: "Lock week" })).toBeVisible();

    // A rejected change keeps the page and shows the server's reason: lock it behind the page's back, then press Lock.
    expect((await page.request.post(`${weekUrl}/lock`)).ok()).toBe(true);
    await dock.getByRole("button", { name: "Lock week" }).click();
    await expect(dock.getByRole("alert")).toContainText("Week must be open");
    await expect(dock.getByRole("button", { name: "Lock week" })).toBeVisible();
    await page.reload();

    // Locked: enter every result, then finalize.
    await expect(dock).toContainText("still needs a result");
    await page
      .getByRole("button", { name: /^Enter result/ })
      .first()
      .click();
    const result = page.getByRole("form", { name: /^Result for/ });
    await result
      .getByLabel(/ score$/)
      .first()
      .fill("101");
    await result
      .getByLabel(/ score$/)
      .nth(1)
      .fill("21");
    await result.getByRole("button", { name: "Save result" }).click();
    await expect(result.getByRole("alert")).toContainText("from 0 to 100");
    await result
      .getByLabel(/ score$/)
      .first()
      .fill("17");
    await result
      .getByLabel(/ score$/)
      .nth(1)
      .fill("21");
    await result.getByRole("button", { name: "Save result" }).click();
    await dock.getByRole("button", { name: "Finalize week" }).click();
    await expect(
      dock.getByRole("button", { name: "Recompute scores" }),
    ).toBeVisible();
    await expect(
      page.getByRole("listitem").filter({ hasText: "Final · current" }),
    ).toBeVisible();

    // Recompute asks first: cancelling changes nothing, confirming calls the endpoint.
    await dock.getByRole("button", { name: "Recompute scores" }).click();
    await expect(
      dock.getByRole("group", { name: "Confirm recompute" }),
    ).toBeVisible();
    await dock.getByRole("button", { name: "Cancel" }).click();
    await expect(dock.getByRole("group")).toHaveCount(0);
    await dock.getByRole("button", { name: "Recompute scores" }).click();
    await dock.getByRole("button", { name: "Confirm recompute" }).click();
    await expect(page.getByText("Recompute scores: done.")).toBeVisible();

    await page.getByRole("button", { name: "‹ League desk" }).click();
    // The seeded Week 2 is open for coach picks, so nothing needs a decision but the season is not finished.
    await expect(
      page.getByText("Nothing needs a decision right now."),
    ).toBeVisible();
  }
  await page
    .getByRole("navigation", { name: "Primary" })
    .getByRole("link", { name: "Season setup" })
    .click();
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
    // `original` came from the API, so branch on that rather than on what the page happens to be showing while it is still loading.
    if (!original) {
      await expect(start).toBeVisible();
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
    await expect(page.getByText("Step 1 of 10")).toBeVisible();
    await expect(page.getByRole("form", { name: "Money" })).toHaveCount(0);
    await basics.getByLabel("Season name").fill("E2E Swan League");
    await basics.getByLabel("Simulation season").check();
    await basics.getByRole("button", { name: "Save & continue" }).click();

    // Closing the browser and coming back opens the saved step.
    const money = page.getByRole("form", { name: "Money" });
    await expect(money).toBeVisible();
    await expect(page.getByText("Step 2 of 10")).toBeVisible();
    await page.reload();
    await expect(money).toBeVisible();
    await expect(page.getByText("Step 2 of 10")).toBeVisible();

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
    await expect(page.getByText("Step 3 of 10")).toBeVisible();
    await deadline.getByLabel("Team picks lock Sundays at").fill("11:30");
    await deadline.getByRole("button", { name: "Save & continue" }).click();

    const teams = page.getByRole("form", { name: "Teams and point values" });
    await expect(teams).toBeVisible();
    await expect(page.getByText("Step 4 of 10")).toBeVisible();

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
    await expect(page.getByText("Step 5 of 10")).toBeVisible();
    await expect(
      schedule.getByText(/Nothing is saved until you confirm/),
    ).toBeVisible();
    await expect(page.getByText(/Coming next/)).toHaveCount(0);
    await expect(
      page.getByRole("button", { name: /review and create/i }),
    ).toHaveCount(0);
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
    await expect(page.getByText("Step 5 of 10")).toBeVisible();

    // Weeks and dogs reads only the imported schedule: with none imported it says so instead of listing weeks.
    await page.request.put("/api/admin/seasons/setup/step", {
      data: { step: 5 },
    });
    await page.reload();
    const dogs = page.getByRole("form", { name: "Weeks and dogs" });
    await expect(dogs).toBeVisible();
    await expect(page.getByText("Step 6 of 10")).toBeVisible();
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
    await expect(page.getByText("Step 7 of 10")).toBeVisible();
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
    await expect(page.getByText("Step 8 of 10")).toBeVisible();
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
    const review = page.getByRole("form", { name: "Review and create" });
    await expect(coaches).toBeVisible();
    await expect(page.getByText("Step 9 of 10")).toBeVisible();
    await expect(page.getByText(/Coming next/)).toHaveCount(0);
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

      // Saving moves on to Review and create. This run imports no schedule and saves no double-dog weeks, so it shows what still needs fixing and finishing stays disabled.
      await expect(review).toBeVisible();
      await expect(page.getByText("Step 10 of 10")).toBeVisible();
      await expect(review.getByText("3 active coaches")).toBeVisible();
      await expect(
        review.getByText(/0 of 18 weeks are imported/),
      ).toBeVisible();
      await expect(
        review.getByText(/Choose the 3 or 4 double-dog weeks/),
      ).toBeVisible();
      await expect(
        review.getByText(/Finishing setup only marks this draft/),
      ).toContainText("does not activate the season");
      await expect(
        review.getByRole("button", { name: "Finish setup" }),
      ).toBeDisabled();
      await review
        .getByRole("button", { name: "Fix: Schedule import" })
        .click();
      await expect(schedule).toBeVisible();
      // Reloading resumes at the saved step (Review and create), not at the page that was just visited.
      await page.reload();
      await expect(review).toBeVisible();
      await review.getByRole("button", { name: "Back" }).click();
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
    // The saved resume point is Review and create when coaches were saved, otherwise still Coaches.
    if (coachesBefore.length === 0) {
      await expect(review).toBeVisible();
      await review.getByRole("button", { name: "Back" }).click();
    }
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
  expect((await page.request.get("/api/admin/dashboard")).status()).toBe(403);

  // The coach desk: pages the coach opens may only ever call coach endpoints.
  const calledPaths: string[] = [];
  page.on("request", (request) =>
    calledPaths.push(new URL(request.url()).pathname),
  );
  const desk = async () =>
    (await (await page.request.get("/api/coach/picks/current")).json()) as {
      state: string;
      revision: number;
      week: { id: string; isDoubleDogWeek: boolean };
      games: { teamId: string; selected: boolean }[];
      dogs: { teamId: string; selected: boolean }[];
    };
  const before = await desk();
  // Picks are only saved on a fresh volume (a used one already holds a ballot from an earlier run).
  if (before.state === "open" && before.revision === 0) {
    await page.reload();
    await expect(
      page.getByRole("heading", { name: "Who are you starting?" }),
    ).toBeVisible();
    await expect(page.getByText("Choose 2 dogs this week")).toBeVisible();
    const starts = page.getByRole("button", { name: /^Start / });
    for (let i = 0; i < 3; i++) await starts.first().click();
    await expect(page.getByText("3 of 3 chosen · all set")).toBeVisible();
    // A fourth start is blocked.
    await expect(starts.first()).toBeDisabled();
    const dogs = page.getByRole("button", { name: /^Pick dog / });
    for (let i = 0; i < 2; i++) await dogs.first().click();
    await page.getByRole("button", { name: "Save 3 picks and 2 dogs" }).click();
    await expect(page.getByText("Picks saved.")).toBeVisible();

    // The saved ballot survives a reload.
    await page.reload();
    await expect(page.getByRole("button", { name: /^Starting / })).toHaveCount(
      3,
    );
    await expect(
      page.getByRole("button", { name: /^Dog picked / }),
    ).toHaveCount(2);

    // Revise: drop one start and save again.
    await page
      .getByRole("button", { name: /^Starting / })
      .first()
      .click();
    await page.getByRole("button", { name: "Save 2 picks and 2 dogs" }).click();
    await expect(page.getByText("Picks saved.")).toBeVisible();
    await page.reload();
    await expect(page.getByRole("button", { name: /^Starting / })).toHaveCount(
      2,
    );

    // The server refuses bad saves and keeps what is saved: a stale revision, a team that is not theirs, a fourth start.
    const saved = await desk();
    const put = (data: object) =>
      page.request.put("/api/coach/picks/current", { data });
    const mine = saved.games
      .filter((game) => game.selected)
      .map((game) => game.teamId);
    const stale = await put({
      weekId: saved.week.id,
      expectedRevision: 0,
      teamIds: mine,
      dogTeamIds: [],
    });
    expect(stale.status()).toBe(409);
    expect(((await stale.json()) as { code: string }).code).toBe(
      "ballot.stale",
    );
    const stranger = await put({
      weekId: saved.week.id,
      expectedRevision: saved.revision,
      teamIds: [crypto.randomUUID()],
      dogTeamIds: [],
    });
    expect(stranger.status()).toBe(400);
    const four = await put({
      weekId: saved.week.id,
      expectedRevision: saved.revision,
      teamIds: saved.games.slice(0, 4).map((game) => game.teamId),
      dogTeamIds: [],
    });
    expect(four.status()).toBe(400);
    const unchanged = await desk();
    expect(unchanged.revision).toBe(saved.revision);
    expect(
      unchanged.games
        .filter((game) => game.selected)
        .map((game) => game.teamId)
        .sort(),
    ).toEqual([...mine].sort());
    expect(unchanged.dogs.filter((dog) => dog.selected)).toHaveLength(2);
  }
  expect(calledPaths.filter((path) => path.startsWith("/api/admin"))).toEqual(
    [],
  );

  // Checked last so it never hides a real failure above.
  expect(
    leftBehind,
    "The test left a Setup season, team finishes, payout settings or coaches behind: set E2E_RESET_SETUP_SEASON_CMD, E2E_RESET_SETUP_TEAMS_CMD, E2E_RESET_SETUP_PAYOUTS_CMD and E2E_RESET_SETUP_COACHES_CMD against a dedicated test database (see the top of this file).",
  ).toBe(false);
});
