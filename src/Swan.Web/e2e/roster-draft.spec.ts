import { execSync } from "node:child_process";
import { expect, test, type Page } from "@playwright/test";
import { signIn } from "./signin";

// A season is started and drafted from scratch, so this needs a DEDICATED database and three commands (see e2e/sql):
//   E2E_PREPARE_DRAFT_CMD         runs e2e/sql/prepare-draft-season.sql (an 18-week schedule for the Setup season, plus two extra administrators)
//   E2E_DRAFT_DEADLINE_PASSED_CMD moves the started season's roster draft deadline into the past, e.g.
//     psql -c "update league.seasons set \"RosterDraftDeadline\" = now() - interval '1 hour' where \"Name\" = '2027 E2E Swan League'"
//   E2E_RESET_DRAFT_CMD           runs e2e/sql/reset-draft-season.sql
// Without them the test is skipped. It also needs no season already in Setup.
const prepare = process.env.E2E_PREPARE_DRAFT_CMD;
const passDeadline = process.env.E2E_DRAFT_DEADLINE_PASSED_CMD;
const reset = process.env.E2E_RESET_DRAFT_CMD;
const seasonName = "2027 E2E Swan League";

// Eastern wall-clock time as a datetime-local value, a few days from now.
function easternInput(daysAhead: number, hour = 18) {
  const day = new Intl.DateTimeFormat("en-CA", {
    timeZone: "America/New_York",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date(Date.now() + daysAhead * 86_400_000));
  return `${day}T${String(hour).padStart(2, "0")}:00`;
}

type Team = { teamId: string; division: string; finishPosition: number | null };

async function chooseNine(page: Page) {
  const choose = page.getByRole("button", { name: /^Choose / });
  for (let i = 0; i < 9; i++) await choose.first().click();
  await expect(page.getByText("9 of 9 chosen · ready to save")).toBeVisible();
}

test("a commissioner starts a season, coaches draft and lock nine teams, the deadline closes it and only the commissioner corrects rosters", async ({
  browser,
}) => {
  test.skip(
    !prepare || !passDeadline || !reset,
    "Needs a dedicated database and E2E_PREPARE_DRAFT_CMD, E2E_DRAFT_DEADLINE_PASSED_CMD and E2E_RESET_DRAFT_CMD.",
  );
  test.setTimeout(240_000);
  const startedAt = new Date().toISOString();
  // Setup season creation needs no other Setup season; start clean in case an earlier run was interrupted.
  execSync(reset!, { stdio: "inherit" });

  const contexts = await Promise.all(
    ["commish", "other", "coach2", "coach3", "coach4"].map(() =>
      browser.newContext(),
    ),
  );
  const [commishCtx, otherCtx, coach2Ctx, coach3Ctx, coach4Ctx] = contexts;
  try {
    // --- The commissioner creates the season (becoming its commissioner) and finishes Setup. ---
    const commish = await commishCtx.newPage();
    await commish.goto("/");
    execSync(prepare!, { stdio: "inherit" }); // users first, the schedule needs the season below
    await signIn(commish, "commish@swan.local");
    const request = commish.request;
    const created = await request.post("/api/admin/seasons", {
      data: { year: 2027, name: seasonName, isSimulation: true },
    });
    expect(created.ok(), `${created.status()} ${await created.text()}`).toBe(
      true,
    );
    execSync(prepare!, { stdio: "inherit" }); // now the Setup season exists: add its schedule
    const catalog = (
      (await (await request.get("/api/admin/seasons/setup/teams")).json()) as {
        teams: Team[];
      }
    ).teams;
    const seen = new Map<string, number>();
    const finishes = catalog.map((team) => {
      const finish = (seen.get(team.division) ?? 0) + 1;
      seen.set(team.division, finish);
      return { teamId: team.teamId, finishPosition: finish };
    });
    expect(
      (
        await request.put("/api/admin/seasons/setup/teams", {
          data: { teams: finishes },
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
      (pool) => ({ pool, places: [{ position: 1, percent: 100 }] }),
    );
    expect(
      (
        await request.put("/api/admin/seasons/setup/fines-and-payouts", {
          data: { missedPickFine: 5, payouts },
        })
      ).ok(),
    ).toBe(true);
    const coaches = [
      ["coach2@swan.local", "Coach 2"],
      ["coach3@swan.local", "Coach 3"],
      ["coach4@swan.local", "Coach 4"],
    ].map(([email, name]) => ({ email, name }));
    expect(
      (
        await request.put("/api/admin/seasons/setup/coaches", {
          data: { coaches },
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

    await commish.goto("/setup");
    const review = commish.getByRole("form", { name: "Ready to launch" });
    await expect(review).toBeVisible();
    // Opening the draft needs a deadline, and rejects one in the past; asking only shows the confirmation.
    const launch = review.getByRole("group", { name: "Open the roster draft" });
    await launch.getByRole("button", { name: "Open roster draft" }).click();
    await expect(launch.getByRole("alert")).toContainText(
      "Choose the roster draft deadline",
    );
    await launch.getByLabel(/Roster draft deadline/).fill(easternInput(-1));
    await launch.getByRole("button", { name: "Open roster draft" }).click();
    const confirm = commish.getByRole("alertdialog", {
      name: "Open the roster draft?",
    });
    await expect(confirm).toContainText("only you, the commissioner");
    await confirm.getByRole("button", { name: "Confirm and open" }).click();
    await expect(confirm.getByRole("alert")).toContainText(
      "must be in the future",
    );
    await confirm.getByRole("button", { name: "Cancel" }).click();
    await launch.getByLabel(/Roster draft deadline/).fill(easternInput(3));
    await launch.getByRole("button", { name: "Open roster draft" }).click();
    await confirm.getByRole("button", { name: "Confirm and open" }).click();
    await expect(
      commish.getByText(/The season is live. The roster draft is open until/),
    ).toBeVisible();
    await commish.getByRole("link", { name: "Open the roster draft" }).click();
    await expect(
      commish.getByRole("heading", { level: 1, name: "Roster draft" }),
    ).toBeVisible();
    await expect(
      commish.getByText("3 missing · 0 incomplete · 0 complete · 0 locked"),
    ).toBeVisible();
    const started = (await (
      await request.get("/api/commissioner/roster-draft")
    ).json()) as {
      seasonId: string;
      coaches: { membershipId: string; name: string; revision: number }[];
      teams: { teamId: string }[];
    };

    // --- An administrator who is not this season's commissioner is refused. ---
    const other = await otherCtx.newPage();
    await signIn(other, "other.admin@swan.local");
    expect(
      (await other.request.get("/api/commissioner/roster-draft")).status(),
    ).toBe(403);
    const refusedCorrection = await other.request.post(
      "/api/commissioner/roster-draft/corrections",
      {
        data: {
          seasonId: started.seasonId,
          membershipId: started.coaches[0].membershipId,
          expectedRevision: 0,
          addTeamId: started.teams[0].teamId,
          reason: "not my season",
        },
      },
    );
    expect(refusedCorrection.status()).toBe(403);
    expect(
      (
        await other.request.post("/api/admin/seasons/setup/activate", {
          data: { rosterDraftDeadlineEastern: easternInput(4) },
        })
      ).status(),
    ).toBe(404);
    await other.goto("/draft");
    await expect(
      other.getByRole("heading", {
        name: "Only the commissioner can manage rosters",
      }),
    ).toBeVisible();

    // --- Coach 2 chooses, saves and locks nine teams. ---
    const coach2 = await coach2Ctx.newPage();
    await signIn(coach2, "coach2@swan.local");
    await coach2.getByRole("link", { name: "Roster" }).click();
    await expect(
      coach2.getByRole("heading", { name: "Choose your nine" }),
    ).toBeVisible();
    await chooseNine(coach2);
    await expect(
      coach2.getByRole("button", { name: /^Choose / }).first(),
    ).toBeDisabled();
    await coach2.getByRole("button", { name: "Save roster" }).click();
    await expect(coach2.getByText("Roster saved.")).toBeVisible();
    await coach2.getByRole("button", { name: "Lock roster" }).click();
    await coach2.getByRole("button", { name: "Confirm lock" }).click();
    await expect(
      coach2.getByRole("heading", { name: "Your roster is locked" }),
    ).toBeVisible();
    await coach2.reload();
    await expect(
      coach2.getByRole("heading", { name: "Your roster is locked" }),
    ).toBeVisible();
    // Locked before the deadline: no more changes, and a second lock is refused.
    const locked = (await (
      await coach2.request.get("/api/coach/roster")
    ).json()) as {
      seasonId: string;
      revision: number;
      teams: { teamId: string; selected: boolean }[];
    };
    const nine2 = locked.teams
      .filter((team) => team.selected)
      .map((team) => team.teamId);
    expect(nine2).toHaveLength(9);
    expect(
      (
        await coach2.request.put("/api/coach/roster", {
          data: {
            seasonId: locked.seasonId,
            expectedRevision: locked.revision,
            teamIds: locked.teams.slice(0, 9).map((team) => team.teamId),
          },
        })
      ).status(),
    ).toBe(409);
    expect(
      (
        await coach2.request.post("/api/coach/roster/lock", {
          data: {
            seasonId: locked.seasonId,
            expectedRevision: locked.revision,
          },
        })
      ).status(),
    ).toBe(409);
    expect(
      (await coach2.request.get("/api/commissioner/roster-draft")).status(),
    ).toBe(403);
    expect((await coach2.request.get("/api/admin/dashboard")).status()).toBe(
      403,
    );

    // --- Coach 3 saves nine but never locks; bad rosters are refused without touching it. ---
    const coach3 = await coach3Ctx.newPage();
    await signIn(coach3, "coach3@swan.local");
    await coach3.goto("/roster");
    await chooseNine(coach3);
    await coach3.getByRole("button", { name: "Save roster" }).click();
    await expect(coach3.getByText("Roster saved.")).toBeVisible();
    const saved3 = (await (
      await coach3.request.get("/api/coach/roster")
    ).json()) as {
      seasonId: string;
      revision: number;
      teams: { teamId: string; selected: boolean }[];
    };
    const all = saved3.teams.map((team) => team.teamId);
    const mine3 = saved3.teams
      .filter((team) => team.selected)
      .map((team) => team.teamId);
    const put = (teamIds: string[], expectedRevision = saved3.revision) =>
      coach3.request.put("/api/coach/roster", {
        data: { seasonId: saved3.seasonId, expectedRevision, teamIds },
      });
    expect((await put(all.slice(0, 8))).status()).toBe(400);
    expect((await put(all.slice(0, 10))).status()).toBe(400);
    expect((await put([...all.slice(0, 8), all[0]])).status()).toBe(400);
    expect(
      (
        await put([...all.slice(0, 8), "00000000-0000-0000-0000-00000000dead"])
      ).status(),
    ).toBe(400);
    expect((await put(all.slice(0, 9), saved3.revision - 1)).status()).toBe(
      409,
    );
    const after3 = (await (
      await coach3.request.get("/api/coach/roster")
    ).json()) as {
      revision: number;
      teams: { teamId: string; selected: boolean }[];
    };
    expect(after3.revision).toBe(saved3.revision);
    expect(
      after3.teams
        .filter((team) => team.selected)
        .map((team) => team.teamId)
        .sort(),
    ).toEqual([...mine3].sort());

    // --- The deadline passes. ---
    execSync(passDeadline!, { stdio: "inherit" });
    await coach3.reload();
    await expect(
      coach3.getByRole("heading", { name: "The roster draft is closed" }),
    ).toBeVisible();
    expect((await put(all.slice(1, 10))).status()).toBe(409);
    expect(
      (
        await coach3.request.post("/api/coach/roster/lock", {
          data: {
            seasonId: saved3.seasonId,
            expectedRevision: saved3.revision,
          },
        })
      ).status(),
    ).toBe(409);

    const coach4 = await coach4Ctx.newPage();
    await signIn(coach4, "coach4@swan.local");
    await coach4.goto("/roster");
    await expect(
      coach4.getByRole("heading", { name: "Your roster is incomplete" }),
    ).toBeVisible();
    await expect(coach4.getByRole("status")).toContainText(
      "with 0 of 9 teams chosen",
    );
    expect(
      (
        await coach4.request.put("/api/coach/roster", {
          data: {
            seasonId: saved3.seasonId,
            expectedRevision: 0,
            teamIds: all.slice(0, 9),
          },
        })
      ).status(),
    ).toBe(409);

    // --- Only the commissioner corrects rosters, with a reason that is audited. ---
    await commish.goto("/draft");
    await expect(commish.getByText(/The draft closed/)).toBeVisible();
    const coach4Row = commish
      .getByRole("listitem")
      .filter({ hasText: "Coach 4" });
    await expect(coach4Row).toContainText("needs your correction");
    await coach4Row.getByRole("button", { name: /Correct roster/ }).click();
    const form = commish.getByRole("form", {
      name: "Correct Coach 4's roster",
    });
    await form.getByRole("button", { name: "Save correction" }).click();
    await expect(form.getByRole("alert")).toContainText(
      "Choose the team to add",
    );
    await form.getByLabel("With", { exact: true }).selectOption({ index: 1 });
    await form.getByRole("button", { name: "Save correction" }).click();
    await expect(form.getByRole("alert")).toContainText("Give a reason");
    await form.getByLabel(/^Reason/).fill("E2E never drafted");
    await form.getByRole("button", { name: "Save correction" }).click();
    await expect(
      commish.getByText("Corrected Coach 4's roster."),
    ).toBeVisible();
    await expect(coach4Row).toContainText("Incomplete · 1 of 9 teams");

    // Finish coach 4's roster through the same endpoint.
    for (let i = 1; i < 9; i++) {
      const draft = (await (
        await request.get("/api/commissioner/roster-draft")
      ).json()) as typeof started & {
        coaches: {
          membershipId: string;
          name: string;
          revision: number;
          teamIds: string[];
        }[];
      };
      const coach = draft.coaches.find((entry) => entry.name === "Coach 4")!;
      const add = draft.teams.find(
        (team) => !coach.teamIds.includes(team.teamId),
      )!;
      const response = await request.post(
        "/api/commissioner/roster-draft/corrections",
        {
          data: {
            seasonId: draft.seasonId,
            membershipId: coach.membershipId,
            expectedRevision: coach.revision,
            addTeamId: add.teamId,
            reason: "E2E never drafted",
          },
        },
      );
      expect(response.status()).toBe(200);
    }
    await commish.reload();
    await expect(
      commish.getByRole("listitem").filter({ hasText: "Coach 4" }),
    ).toContainText("Complete, not locked · 9 of 9 teams");

    // Replace one team on coach 2's locked roster.
    const draft = (await (
      await request.get("/api/commissioner/roster-draft")
    ).json()) as typeof started & {
      coaches: {
        membershipId: string;
        name: string;
        revision: number;
        teamIds: string[];
      }[];
    };
    const coach2Row = commish
      .getByRole("listitem")
      .filter({ hasText: "Coach 2" });
    await expect(coach2Row).toContainText("Locked · 9 of 9 teams");
    await coach2Row.getByRole("button", { name: /Correct roster/ }).click();
    const replaceForm = commish.getByRole("form", {
      name: "Correct Coach 2's roster",
    });
    await replaceForm.getByLabel(/^Replace/).selectOption({ index: 1 });
    await replaceForm
      .getByLabel("With", { exact: true })
      .selectOption({ index: 1 });
    await replaceForm.getByLabel(/^Reason/).fill("E2E coach asked for a swap");
    await replaceForm.getByRole("button", { name: "Save correction" }).click();
    await expect(
      commish.getByText("Corrected Coach 2's roster."),
    ).toBeVisible();
    await expect(coach2Row).toContainText("Locked · 9 of 9 teams");
    const coach2Now = (
      (await (
        await request.get("/api/commissioner/roster-draft")
      ).json()) as typeof draft
    ).coaches.find((entry) => entry.name === "Coach 2")!;
    expect(coach2Now.revision).toBeGreaterThan(
      draft.coaches.find((entry) => entry.name === "Coach 2")!.revision,
    );
    expect([...coach2Now.teamIds].sort()).not.toEqual([...nine2].sort());

    // The audit trail names the commissioner and keeps every reason.
    const audit = (await (
      await request.get(
        `/api/admin/audit?action=roster.corrected&from=${encodeURIComponent(startedAt)}`,
      )
    ).json()) as unknown[];
    const text = JSON.stringify(audit);
    expect(audit.length).toBe(10);
    expect(text).toContain("E2E never drafted");
    expect(text).toContain("E2E coach asked for a swap");
    expect(text).toContain("00000000-0000-0000-0000-0000000009c1");

    // Anonymous callers get nothing.
    const anonymous = await browser.newContext();
    try {
      expect(
        (
          await anonymous.request.get("/api/commissioner/roster-draft")
        ).status(),
      ).toBe(401);
      expect((await anonymous.request.get("/api/coach/roster")).status()).toBe(
        401,
      );
    } finally {
      await anonymous.close();
    }
  } finally {
    await Promise.all(contexts.map((context) => context.close()));
    execSync(reset!, { stdio: "inherit" });
  }
});
