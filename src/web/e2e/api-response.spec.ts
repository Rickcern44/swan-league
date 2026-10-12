import { expect, test } from "@playwright/test";

test("renders the API response", async ({ page }) => {
  await page.goto("/");

  await expect(page.getByText("API response: pong")).toBeVisible();
});
