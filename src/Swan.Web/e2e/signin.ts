import { expect, type Page } from "@playwright/test";

const mailpitUrl = process.env.E2E_MAILPIT_URL;

// Signs in the way a person does: asks for a link, finds it in Mailpit and opens it. Sign-in links are limited to one per address per minute.
export async function signIn(page: Page, email: string) {
  if (!mailpitUrl)
    throw new Error("E2E_MAILPIT_URL must point to Mailpit's HTTP endpoint.");
  await page.request.delete(`${mailpitUrl}/api/v1/messages`);
  await page.goto("/");
  await page.getByLabel("Email").fill(email);
  await page.getByRole("button", { name: "Email me a sign-in link" }).click();
  await expect(
    page.getByRole("heading", { name: "Check your email" }),
  ).toBeVisible();

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
  // The app redeems the link after it loads; wait for it so the session cookie exists before the test makes requests.
  await expect(page.getByText(/Signed in as/)).toBeVisible();
}
