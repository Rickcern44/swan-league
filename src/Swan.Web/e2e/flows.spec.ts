import { type Browser, expect, type Page, test } from "@playwright/test";
import { sql } from "./database";
import { signIn } from "./signin";

function easternInput(daysAhead: number) {
  const day = new Intl.DateTimeFormat("en-CA", {
    timeZone: "America/New_York",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date(Date.now() + daysAhead * 86_400_000));
  return `${day}T18:00`;
}

async function chooseNine(page: Page) {
  const choose = page.getByRole("button", { name: /^Choose / });
  for (let i = 0; i < 9; i++) await choose.first().click();
  await expect(page.getByText("9 of 9 chosen · ready to save")).toBeVisible();
}

async function createLiveDraftSeason(
  browser: Browser,
  year: number,
  coach: { email: string; name: string },
  adminEmail = "admin@swan.local",
) {
  // Insert the alternate test administrator before sign-in. With no Setup season yet,
  // this does not add schedule data; the second invocation below does.
  sql("prepare-draft-season.sql");
  const adminContext = await browser.newContext();
  const admin = await adminContext.newPage();
  await signIn(admin, adminEmail);
  await admin
    .getByRole("group", { name: "Mode" })
    .getByRole("link", { name: "Commish" })
    .click();
  await admin.getByRole("link", { name: "Season setup" }).click();
  const start = admin.getByRole("form", { name: "Start a new season" });
  await start.getByLabel("Season year").fill(String(year));
  await start.getByLabel("Season name").fill(`${year} E2E Swan League`);
  await start.getByRole("button", { name: "Create season" }).click();
  await expect(admin.getByRole("form", { name: "Basics" })).toBeVisible();

  sql("prepare-draft-season.sql");
  const request = admin.request;
  const teams = (await (
    await request.get("/api/admin/seasons/setup/teams")
  ).json()) as { teams: { teamId: string; division: string }[] };
  const finishes = new Map<string, number>();
  const selected = teams.teams.map((team) => {
    const finishPosition = (finishes.get(team.division) ?? 0) + 1;
    finishes.set(team.division, finishPosition);
    return { teamId: team.teamId, finishPosition };
  });
  expect(
    (
      await request.put("/api/admin/seasons/setup/teams", {
        data: { teams: selected },
      })
    ).ok(),
  ).toBe(true);
  expect(
    (
      await request.put("/api/admin/seasons/setup/double-dog-weeks", {
        data: { weeks: [2, 12, 17] },
      })
    ).ok(),
  ).toBe(true);
  const payouts = ["Main", "Dog", "Regular Season", "Playoff"].map((pool) => ({
    pool,
    places: [{ position: 1, percent: 100 }],
  }));
  expect(
    (
      await request.put("/api/admin/seasons/setup/fines-and-payouts", {
        data: { missedPickFine: 5, payouts },
      })
    ).ok(),
  ).toBe(true);
  expect(
    (
      await request.put("/api/admin/seasons/setup/coaches", {
        data: { coaches: [coach] },
      })
    ).ok(),
  ).toBe(true);
  expect(
    (
      await request.put("/api/admin/seasons/setup/step", { data: { step: 6 } })
    ).ok(),
  ).toBe(true);
  expect(
    (await request.post("/api/admin/seasons/setup/review/finish")).ok(),
  ).toBe(true);
  expect(
    (
      await request.post("/api/admin/seasons/setup/activate", {
        data: { rosterDraftDeadlineEastern: easternInput(3) },
      })
    ).ok(),
  ).toBe(true);
  return { adminContext, admin };
}

async function lockRoster(page: Page, email: string) {
  await signIn(page, email);
  await page
    .getByRole("navigation", { name: "Primary" })
    .getByRole("link", { name: "Draft" })
    .click();
  await expect(
    page.getByRole("heading", { name: "Choose your nine" }),
  ).toBeVisible();
  await chooseNine(page);
  await page.getByRole("button", { name: "Save draft" }).click();
  await page.getByRole("button", { name: "Lock roster" }).click();
  await page.getByRole("button", { name: "Confirm lock" }).click();
  await expect(
    page.getByRole("heading", { name: "Your roster is locked" }),
  ).toBeVisible();
}

type WeekResponse = {
  games: { id: string; approvedLine: number | null }[];
};

test("admin creates a fresh season and coach locks a roster", async ({
  browser,
}) => {
  test.setTimeout(180_000);
  const { adminContext, admin } = await createLiveDraftSeason(browser, 2027, {
    email: "coach2@swan.local",
    name: "Coach 2",
  });
  const coachContext = await browser.newContext();
  try {
    await admin.goto("/commish/draft");
    await expect(
      admin.getByRole("heading", { level: 1, name: "Draft" }),
    ).toBeVisible();
    await lockRoster(await coachContext.newPage(), "coach2@swan.local");
  } finally {
    await Promise.all([adminContext.close(), coachContext.close()]);
  }
});

test("admin runs a full week after creating a fresh season", async ({
  browser,
}) => {
  test.setTimeout(180_000);
  const { adminContext, admin } = await createLiveDraftSeason(
    browser,
    2028,
    {
      email: "coach3@swan.local",
      name: "Coach 3",
    },
    "other.admin@swan.local",
  );
  const coachContext = await browser.newContext();
  try {
    const coach = await coachContext.newPage();
    await lockRoster(coach, "coach3@swan.local");
    const dashboard = (await (
      await admin.request.get("/api/admin/dashboard")
    ).json()) as { currentWeek: { id: string; state: string } | null };
    expect(dashboard.currentWeek?.state).toBe("Draft");
    await admin.goto(`/weeks/${dashboard.currentWeek!.id}`);
    const dock = admin.getByRole("region", { name: "Next action" });
    await admin
      .getByRole("button", { name: /^Add line/ })
      .first()
      .click();
    await admin.getByLabel(/^Spread for/).fill("-3.5");
    await admin.getByRole("button", { name: "Save and approve" }).click();
    const week = (await (
      await admin.request.get(`/api/admin/weeks/${dashboard.currentWeek!.id}`)
    ).json()) as WeekResponse;
    for (const game of week.games.filter(
      (game) => game.approvedLine === null,
    )) {
      expect(
        (
          await admin.request.post(
            `/api/admin/weeks/${dashboard.currentWeek!.id}/games/${game.id}/lines`,
            { data: { spread: -3.5 } },
          )
        ).ok(),
      ).toBe(true);
    }
    await admin.reload();
    await dock.getByRole("button", { name: /^Approve \d+ lines?$/ }).click();
    await dock.getByRole("button", { name: "Publish week" }).click();

    await coach
      .getByRole("navigation", { name: "Primary" })
      .getByRole("link", { name: "Home" })
      .click();
    await coach.getByRole("link", { name: /^Finish Week \d+ picks$/ }).click();
    const starts = coach.getByRole("button", { name: /^Start / });
    for (let i = 0; i < 3; i++) await starts.first().click();
    await coach.getByRole("button", { name: /^Dogs \d+ of \d+$/ }).click();
    const dogs = coach.getByRole("button", { name: /^Pick dog / });
    for (let i = 0; i < 2; i++) await dogs.first().click();
    await coach
      .getByRole("button", { name: "Save 3 picks and 2 dogs" })
      .click();
    await expect(coach.getByText("Picks saved.")).toBeVisible();

    await dock.getByRole("button", { name: "Lock week" }).click();
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
    await dock.getByRole("button", { name: "Finalize week" }).click();
    await expect(
      dock.getByRole("button", { name: "Recompute scores" }),
    ).toBeVisible();
  } finally {
    await Promise.all([adminContext.close(), coachContext.close()]);
  }
});
