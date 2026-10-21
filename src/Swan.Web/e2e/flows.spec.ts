import { type Browser, expect, type Page, test } from "@playwright/test";
import { sql } from "./database";
import { signIn } from "./signin";

type Coach = { email: string; name: string };
type WeekResponse = {
  games: { id: string; approvedLine: number | null; hasResult: boolean }[];
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
    await admin.goto(`/weeks/${weekId}`);
    await admin
      .getByRole("button", { name: /^Add line/ })
      .first()
      .click();
    await admin.getByLabel(/^Spread for/).fill("-3.5");
    await admin.getByRole("button", { name: "Save and approve" }).click();
    const week = (await (
      await admin.request.get(`/api/admin/weeks/${weekId}`)
    ).json()) as WeekResponse;
    for (const game of week.games.filter(
      (game) => game.approvedLine === null,
    )) {
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
      await admin.request.post(
        `/api/admin/weeks/${weekId}/dog-options/generate`,
      ),
    );
    await admin.reload();
    await admin
      .getByRole("region", { name: "Next action" })
      .getByRole("button", { name: /^Approve \d+ lines?$/ })
      .click();
    const nextAction = admin.getByRole("region", { name: "Next action" });
    await expect(
      nextAction.getByRole("button", { name: "Publish week" }),
    ).toBeVisible();
    await nextAction.getByRole("button", { name: "Publish week" }).click();
    await expect(admin.getByText("Open for picks")).toBeVisible();
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
    await prepareOpenWeek(admin);
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
    await admin
      .getByRole("button", { name: /^Enter result/ })
      .first()
      .click();
    const result = admin.getByRole("form", { name: /^Result for/ });
    await result
      .getByLabel(/ score$/)
      .first()
      .fill("17");
    await result
      .getByLabel(/ score$/)
      .nth(1)
      .fill("21");
    await result.getByRole("button", { name: "Save result" }).click();
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
