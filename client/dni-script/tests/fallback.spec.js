import { test, expect } from "@playwright/test";

// FR-011: a visitor's script is blocked or fails to execute — the page must be left
// showing its static default number, never a blank or partial one.
test.describe("Script-blocked fallback", () => {
  test("with no script executing at all, the page's static default number remains untouched", async ({
    page,
  }) => {
    await page.goto("/tests/fixtures/fallback.html");

    await expect(page.locator("#text-1")).toHaveText("Call us on 555-000-0000 today.");
  });
});
