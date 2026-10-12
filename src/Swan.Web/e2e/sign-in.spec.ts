import { expect, test } from "@playwright/test";

const mailpitUrl = process.env.E2E_MAILPIT_URL;

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

test("seeded admin and coach complete the magic-link flow", async ({
  page,
}) => {
  await signIn(page, "admin@swan.local");
  await expect(page.getByText("Signed in as Swan Admin · Admin")).toBeVisible();
  await expect(page.getByText("Admin", { exact: true })).toBeVisible();

  await page.getByRole("button", { name: "Sign out" }).click();
  await expect(page.getByLabel("Email")).toBeVisible();

  await signIn(page, "coach1@swan.local");
  await expect(page.getByText("Signed in as Coach 1 · Coach")).toBeVisible();
  await expect(page.getByText("Admin", { exact: true })).not.toBeVisible();
});
