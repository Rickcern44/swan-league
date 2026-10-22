import { type Browser, expect, type Page, test } from "@playwright/test";
import { sql } from "./database";
import { signIn } from "./signin";

type Coach = { email: string; name: string };
type WeekResponse = {
  number: number;
  games: {
    id: string;
    approvedLine: number | null;
    hasResult: boolean;
    excluded: boolean;
    homeTeamName: string;
    awayTeamName: string;
  }[];
};
type RosterResponse = {
  seasonId: string;
  revision: number;
  teams: { teamId: string }[];
};

function easternInput(daysAhead: number) {
  const day = new Intl.DateTimeFormat("en-CA", {
    timeZone: "America/New_York",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date(Date.now() + daysAhead * 86_400_000));
  return `${day}T18:00`;
}

async function expectOk(response: Awaited<ReturnType<Page["request"]["get"]>>) {
  expect(response.ok(), await response.text()).toBe(true);
}

async function chooseNine(page: Page) {
  const choose = page.getByRole("button", { name: /^Choose / });
  for (let i = 0; i < 9; i++) await choose.first().click();
  await expect(page.getByText("9 of 9 chosen · ready to save")).toBeVisible();
}

test("a recent sign-in request explains when another email can be sent", async ({
  page,
}) => {
  await page.goto("/");
  await page.getByLabel("Email").fill("coach6@swan.local");
  await page.getByRole("button", { name: "Email me a sign-in link" }).click();
  await expect(
    page.getByRole("heading", { name: "Check your email" }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: /^Send another link in \d+s$/ }),
  ).toBeDisabled();

  await page.goto("/");
  await page.getByLabel("Email").fill("coach6@swan.local");
  await page.getByRole("button", { name: "Email me a sign-in link" }).click();

  await expect(page.getByRole("alert")).toHaveText(
    /A link was just sent to this address\. You can ask for another in \d:\d{2}\./,
  );
  await expect(
    page.getByRole("button", { name: "Email me a sign-in link" }),
  ).toBeDisabled();
});

async function configureSetup(page: Page, coach: Coach) {
  sql("prepare-draft-season.sql");
  const teams = (await (
    await page.request.get("/api/admin/seasons/setup/teams")
  ).json()) as { teams: { teamId: string; division: string }[] };
  const finishes = new Map<string, number>();
  const selected = teams.teams.map((team) => {
    const finishPosition = (finishes.get(team.division) ?? 0) + 1;
    finishes.set(team.division, finishPosition);
    return { teamId: team.teamId, finishPosition };
  });
  await expectOk(
    await page.request.put("/api/admin/seasons/setup/teams", {
      data: { teams: selected },
    }),
  );
  await expectOk(
    await page.request.put("/api/admin/seasons/setup/double-dog-weeks", {
      data: { weeks: [2, 12, 17] },
    }),
  );
  const payouts = ["Main", "Dog", "Regular Season", "Playoff"].map((pool) => ({
    pool,
    places: [{ position: 1, percent: 100 }],
  }));
  await expectOk(
    await page.request.put("/api/admin/seasons/setup/fines-and-payouts", {
      data: { missedPickFine: 5, payouts },
    }),
  );
  await expectOk(
    await page.request.put("/api/admin/seasons/setup/coaches", {
      data: { coaches: [coach] },
    }),
  );
  await expectOk(
    await page.request.put("/api/admin/seasons/setup/step", {
      data: { step: 6 },
    }),
  );
  await expectOk(
    await page.request.post("/api/admin/seasons/setup/review/finish"),
  );
  await expectOk(
    await page.request.post("/api/admin/seasons/setup/activate", {
      data: { rosterDraftDeadlineEastern: easternInput(3) },
    }),
  );
}

async function createDraftState(
  browser: Browser,
  year: number,
  adminEmail: string,
  coach: Coach,
) {
  sql("e2e-users.sql");
  const adminContext = await browser.newContext();
  const admin = await adminContext.newPage();
  await signIn(admin, adminEmail);
  await expectOk(
    await admin.request.post("/api/admin/seasons", {
      data: { year, name: `${year} E2E Swan League` },
    }),
  );
  await configureSetup(admin, coach);
  return { adminContext, admin };
}

async function lockRosterWithApi(browser: Browser, email: string) {
  const coachContext = await browser.newContext();
  const coach = await coachContext.newPage();
  await signIn(coach, email);
  const roster = (await (
    await coach.request.get("/api/coach/roster")
  ).json()) as RosterResponse;
  await expectOk(
    await coach.request.put("/api/coach/roster", {
      data: {
        seasonId: roster.seasonId,
        expectedRevision: roster.revision,
        teamIds: roster.teams.slice(0, 9).map((team) => team.teamId),
      },
    }),
  );
  const saved = (await (
    await coach.request.get("/api/coach/roster")
  ).json()) as RosterResponse;
  await expectOk(
    await coach.request.post("/api/coach/roster/lock", {
      data: { seasonId: saved.seasonId, expectedRevision: saved.revision },
    }),
  );
  return { coachContext, coach };
}

async function currentWeek(admin: Page) {
  const dashboard = (await (
    await admin.request.get("/api/admin/dashboard")
  ).json()) as { currentWeek: { id: string } | null };
  expect(dashboard.currentWeek).not.toBeNull();
  return dashboard.currentWeek!.id;
}

async function prepareOpenWeek(admin: Page) {
  const weekId = await currentWeek(admin);
  const week = (await (
    await admin.request.get(`/api/admin/weeks/${weekId}`)
  ).json()) as WeekResponse;
  for (const game of week.games) {
    await expectOk(
      await admin.request.post(
        `/api/admin/weeks/${weekId}/games/${game.id}/lines`,
        {
          data: { spread: -3.5, approve: true },
        },
      ),
    );
  }
  await expectOk(
    await admin.request.post(`/api/admin/weeks/${weekId}/dog-options/generate`),
  );
  await expectOk(
    await admin.request.post(`/api/admin/weeks/${weekId}/lines/approve`),
  );
  await expectOk(
    await admin.request.post(`/api/admin/weeks/${weekId}/publish`),
  );
  return weekId;
}

async function prepareLockedWeek(admin: Page) {
  const weekId = await prepareOpenWeek(admin);
  await expectOk(await admin.request.post(`/api/admin/weeks/${weekId}/lock`));
  return weekId;
}

test("commissioner starts a draft and coach locks a roster", async ({
  browser,
}) => {
  test.setTimeout(90_000);
  sql("e2e-users.sql");
  const adminContext = await browser.newContext();
  const admin = await adminContext.newPage();
  const coachContext = await browser.newContext();
  try {
    await signIn(admin, "admin@swan.local");
    await admin
      .getByRole("group", { name: "Mode" })
      .getByRole("link", { name: "Commish" })
      .click();
    await admin.getByRole("link", { name: "Season setup" }).click();
    const start = admin.getByRole("form", { name: "Start a new season" });
    await start.getByLabel("Season year").fill("2027");
    await start.getByLabel("Season name").fill("2027 E2E Swan League");
    await start.getByRole("button", { name: "Create season" }).click();
    await expect(admin.getByRole("form", { name: "Basics" })).toBeVisible();
    await configureSetup(admin, {
      email: "coach2@swan.local",
      name: "Coach 2",
    });

    const coach = await coachContext.newPage();
    await signIn(coach, "coach2@swan.local");
    await coach
      .getByRole("navigation", { name: "Primary" })
      .getByRole("link", { name: "Draft" })
      .click();
    await expect(
      coach.getByRole("heading", { name: "Choose your nine" }),
    ).toBeVisible();
    await chooseNine(coach);
    await coach.getByRole("button", { name: "Save draft" }).click();
    await coach.getByRole("button", { name: "Lock roster" }).click();
    await coach.getByRole("button", { name: "Confirm lock" }).click();
    await expect(
      coach.getByRole("heading", { name: "Your roster is locked" }),
    ).toBeVisible();
  } finally {
    await Promise.all([adminContext.close(), coachContext.close()]);
  }
});

test("commissioner publishes a prepared draft week", async ({ browser }) => {
  test.setTimeout(90_000);
  const { adminContext, admin } = await createDraftState(
    browser,
    2028,
    "other.admin@swan.local",
    { email: "coach3@swan.local", name: "Coach 3" },
  );
  try {
    const weekId = await currentWeek(admin);
    const week = (await (
      await admin.request.get(`/api/admin/weeks/${weekId}`)
    ).json()) as WeekResponse;
    const included = week.games.filter((game) => !game.excluded);

    // Today leads with the week's lines as the next action.
    await admin.goto("/commish");
    const next = admin.getByRole("region", { name: "Next action" });
    await expect(next).toBeVisible();
    await next.getByRole("link", { name: `Open Week ${week.number}` }).click();
    await expect(admin).toHaveURL(new RegExp(`/commish/weeks/${weekId}`));

    // Paste every line as CSV, preview it, then save.
    await admin.getByRole("button", { name: "Paste CSV" }).click();
    const paste = admin.getByRole("region", { name: "Paste CSV" });
    await paste
      .getByLabel(/^CSV lines/)
      .fill(
        included
          .map((game) => `${game.awayTeamName},${game.homeTeamName},-3.5`)
          .join("\n"),
      );
    await paste.getByRole("button", { name: "Preview CSV" }).click();
    await paste
      .getByRole("button", {
        name: `Save ${included.length} ${included.length === 1 ? "line" : "lines"}`,
      })
      .click();
    await expect(
      admin.getByText(
        `Saved ${included.length} ${included.length === 1 ? "line" : "lines"} from CSV.`,
      ),
    ).toBeVisible();

    // Approve them all from the table.
    await admin
      .getByRole("checkbox", { name: "Select all lines that need review" })
      .check();
    await admin
      .getByRole("button", { name: `Approve ${included.length} selected` })
      .click();
    await expect(
      admin.getByRole("tab", {
        name: `Lines ${included.length}/${included.length}`,
      }),
    ).toBeVisible();

    // Build the dog list before the lines freeze.
    await admin.getByRole("tab", { name: /^Dogs/ }).click();
    await admin
      .getByRole("button", { name: "Generate from approved lines" })
      .click();
    await expect(
      admin.getByRole("list", { name: "Dog options" }).getByRole("listitem"),
    ).toHaveCount(included.length);

    const nextAction = admin.getByRole("region", { name: "Next action" });
    await nextAction
      .getByRole("button", { name: /^Approve \d+ lines?$/ })
      .click();
    await nextAction
      .getByRole("button", { name: `Publish Week ${week.number}` })
      .click();
    await expect(
      admin
        .getByRole("list", { name: "Week stages" })
        .locator('[aria-current="step"]'),
    ).toContainText("Open for picks");
  } finally {
    await adminContext.close();
  }
});

test("coach saves picks for an open week", async ({ browser }) => {
  test.setTimeout(90_000);
  const { adminContext, admin } = await createDraftState(
    browser,
    2029,
    "e2e.admin2@swan.local",
    { email: "coach4@swan.local", name: "Coach 4" },
  );
  const { coachContext, coach } = await lockRosterWithApi(
    browser,
    "coach4@swan.local",
  );
  try {
    const weekId = await prepareOpenWeek(admin);
    await coach.goto("/");
    await coach.getByRole("link", { name: /^Finish Week \d+ picks$/ }).click();
    const starts = coach.getByRole("button", { name: /^Start / });
    for (let i = 0; i < 3; i++) await starts.first().click();
    await coach.getByRole("button", { name: /^Dogs \d+ of \d+$/ }).click();
    await coach
      .getByRole("button", { name: /^Pick dog / })
      .first()
      .click();
    await coach.getByRole("button", { name: "Save 3 picks and 1 dog" }).click();
    await expect(coach.getByText("Picks saved.")).toBeVisible();

    // Standings are reachable from the coach navigation; nothing is scored yet this season.
    await coach
      .getByRole("navigation", { name: "Primary" })
      .getByRole("link", { name: "Standings" })
      .click();
    await expect(
      coach.getByRole("heading", { level: 1, name: "Standings" }),
    ).toBeVisible();
    await expect(
      coach.getByText(/^Standings start once Week 1 is final\./),
    ).toBeVisible();

    // The commissioner sees the coach's full card on the week's Picks tab.
    await admin.goto(`/commish/weeks/${weekId}?tab=picks`);
    const card = admin
      .getByRole("list", { name: "Coach cards" })
      .getByRole("listitem")
      .filter({ hasText: "Coach 4" });
    await expect(card).toContainText("Complete");
  } finally {
    await Promise.all([adminContext.close(), coachContext.close()]);
  }
});

test("commissioner finalizes a locked week", async ({ browser }) => {
  test.setTimeout(90_000);
  const { adminContext, admin } = await createDraftState(
    browser,
    2030,
    "e2e.admin3@swan.local",
    { email: "coach5@swan.local", name: "Coach 5" },
  );
  try {
    const weekId = await prepareLockedWeek(admin);
    await admin.goto(`/weeks/${weekId}`);
    // Enter the first game's scores in the Results table, then save that row.
    const results = admin.getByRole("table", { name: "Results" });
    await results.getByRole("textbox").nth(0).fill("17");
    await results.getByRole("textbox").nth(1).fill("21");
    await results
      .getByRole("button", { name: /^Save result for / })
      .first()
      .click();
    await expect(admin.getByText("Result saved.")).toBeVisible();
    const week = (await (
      await admin.request.get(`/api/admin/weeks/${weekId}`)
    ).json()) as WeekResponse;
    for (const game of week.games.filter((game) => !game.hasResult)) {
      await expectOk(
        await admin.request.post(
          `/api/admin/weeks/${weekId}/games/${game.id}/result`,
          {
            data: { homeScore: 17, awayScore: 21 },
          },
        ),
      );
    }
    await admin.reload();
    await admin
      .getByRole("region", { name: "Next action" })
      .getByRole("button", { name: "Finalize week" })
      .click();
    await expect(
      admin.getByRole("button", { name: "Recompute scores" }),
    ).toBeVisible();
  } finally {
    await adminContext.close();
  }
});
