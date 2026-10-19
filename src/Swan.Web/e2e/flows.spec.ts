import { expect, type Page, test } from "@playwright/test";
import { sql } from "./database";
import { signIn } from "./signin";

const seasonName = "2027 E2E Swan League";

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

test("admin runs a week, coach revises picks, and commissioner opens a roster draft", async ({
  browser,
}) => {
  test.setTimeout(240_000);
  const adminContext = await browser.newContext();
  const coachContext = await browser.newContext();
  const commissionerContext = await browser.newContext();
  const draftCoachContext = await browser.newContext();
  try {
    const admin = await adminContext.newPage();
    await test.step("admin signs in and runs a seeded week", async () => {
      await signIn(admin, "admin@swan.local");
      await expect(
        admin.getByText("Signed in as Swan Admin · Admin"),
      ).toBeVisible();
      await expect(
        admin.getByRole("navigation", { name: "Primary" }),
      ).toBeVisible();
      const dashboard = (await (
        await admin.request.get("/api/admin/dashboard")
      ).json()) as {
        currentWeek: { id: string; state: string } | null;
      };
      expect(dashboard.currentWeek?.state).toBe("Draft");
      await admin.getByRole("link", { name: /Lines need approval/ }).click();
      const dock = admin.getByRole("region", { name: "Next action" });
      await admin
        .getByRole("button", { name: /^Add line/ })
        .first()
        .click();
      await admin.getByLabel(/^Spread for/).fill("-3.5");
      await admin.getByRole("button", { name: "Save and approve" }).click();
      await dock.getByRole("button", { name: /^Approve \d+ lines?$/ }).click();
      await dock.getByRole("button", { name: "Publish week" }).click();
      await expect(
        dock.getByRole("button", { name: "Lock week" }),
      ).toBeVisible();
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
    });

    await test.step("coach signs in, saves and revises weekly picks", async () => {
      const coach = await coachContext.newPage();
      await signIn(coach, "coach1@swan.local");
      await expect(
        coach.getByText("Signed in as Coach 1 · Coach"),
      ).toBeVisible();
      await expect(
        coach.getByRole("link", { name: "Season setup" }),
      ).toHaveCount(0);
      expect((await coach.request.get("/api/admin/dashboard")).status()).toBe(
        403,
      );
      await expect(
        coach.getByRole("heading", { name: "Who are you starting?" }),
      ).toBeVisible();
      const starts = coach.getByRole("button", { name: /^Start / });
      for (let i = 0; i < 3; i++) await starts.first().click();
      const dogs = coach.getByRole("button", { name: /^Pick dog / });
      for (let i = 0; i < 2; i++) await dogs.first().click();
      await coach
        .getByRole("button", { name: "Save 3 picks and 2 dogs" })
        .click();
      await expect(coach.getByText("Picks saved.")).toBeVisible();
      await coach.reload();
      await expect(
        coach.getByRole("button", { name: /^Starting / }),
      ).toHaveCount(3);
      await expect(
        coach.getByRole("button", { name: /^Dog picked / }),
      ).toHaveCount(2);
      await coach
        .getByRole("button", { name: /^Starting / })
        .first()
        .click();
      await coach
        .getByRole("button", { name: "Save 2 picks and 2 dogs" })
        .click();
      await expect(coach.getByText("Picks saved.")).toBeVisible();
      await coach.reload();
      await expect(
        coach.getByRole("button", { name: /^Starting / }),
      ).toHaveCount(2);

      const dashboard = (await (await admin.request.get("/api/admin/dashboard")).json()) as {
        currentWeek: { id: string; state: string } | null;
      };
      expect(dashboard.currentWeek?.state).toBe("Open");
      expect(
        (
          await admin.request.post(
            `/api/admin/weeks/${dashboard.currentWeek!.id}/lock`,
          )
        ).ok(),
      ).toBe(true);
      await coach.reload();
      await expect(
        coach.getByText("No week is open for picks right now."),
      ).toBeVisible();
    });

    await test.step("commissioner opens a season and coach locks nine teams", async () => {
      sql("prepare-draft-season.sql"); // Add test administrators before sign-in.
      const commissioner = await commissionerContext.newPage();
      await signIn(commissioner, "commish@swan.local");
      await commissioner.getByRole("link", { name: "Season setup" }).click();
      const start = commissioner.getByRole("form", {
        name: "Start a new season",
      });
      await start.getByLabel("Season year").fill("2027");
      await start.getByLabel("Season name").fill(seasonName);
      await start.getByRole("button", { name: "Create season" }).click();
      await expect(
        commissioner.getByRole("form", { name: "Basics" }),
      ).toBeVisible();
      sql("prepare-draft-season.sql"); // Supply a deterministic schedule without an external API key.
      const request = commissioner.request;
      const teams = (await (
        await request.get("/api/admin/seasons/setup/teams")
      ).json()) as {
        teams: { teamId: string; division: string }[];
      };
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
      const payouts = ["Main", "Dog", "Regular Season", "Playoff"].map(
        (pool) => ({
          pool,
          places: [{ position: 1, percent: 100 }],
        }),
      );
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
            data: {
              coaches: [{ email: "coach2@swan.local", name: "Coach 2" }],
            },
          })
        ).ok(),
      ).toBe(true);
      expect(
        (
          await request.put("/api/admin/seasons/setup/step", {
            data: { step: 9 },
          })
        ).ok(),
      ).toBe(true);
      await commissioner.reload();
      const launch = commissioner.getByRole("group", {
        name: "Open the draft",
      });
      await launch.getByLabel(/Draft deadline/).fill(easternInput(3));
      await launch.getByRole("button", { name: "Open draft" }).click();
      const confirm = commissioner.getByRole("alertdialog", {
        name: "Open the draft?",
      });
      await confirm.getByRole("button", { name: "Confirm and open" }).click();
      await commissioner.getByRole("link", { name: "Open the draft" }).click();
      await expect(
        commissioner.getByRole("heading", { level: 1, name: "Draft" }),
      ).toBeVisible();

      const draftCoach = await draftCoachContext.newPage();
      await signIn(draftCoach, "coach2@swan.local");
      await draftCoach.getByRole("link", { name: "Roster" }).click();
      await expect(
        draftCoach.getByRole("heading", { name: "Choose your nine" }),
      ).toBeVisible();
      await chooseNine(draftCoach);
      await draftCoach.getByRole("button", { name: "Save roster" }).click();
      await draftCoach.getByRole("button", { name: "Lock roster" }).click();
      await draftCoach.getByRole("button", { name: "Confirm lock" }).click();
      await draftCoach.reload();
      await expect(
        draftCoach.getByRole("heading", { name: "Your roster is locked" }),
      ).toBeVisible();
    });
  } finally {
    await Promise.all([
      adminContext.close(),
      coachContext.close(),
      commissionerContext.close(),
      draftCoachContext.close(),
    ]);
  }
});
