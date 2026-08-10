import { test, expect } from "@playwright/test";

// FR-009: replacement covers single-page applications, including numbers rendered
// after initial page load (the MutationObserver path), not just what's present at
// DOMContentLoaded.
test.describe("SPA / post-load replacement", () => {
  test.beforeEach(async ({ page }) => {
    await page.addInitScript(() => {
      window.__attributionConsent = { granted: true };
    });
    await page.route("**/v1/dni/allocate", async (route) => {
      await route.fulfill({
        json: {
          session_id: "session-1",
          number: "555-999-0000",
          expires_at: new Date(Date.now() + 1800000).toISOString(),
        },
      });
    });
  });

  test("replaces a number present at load", async ({ page }) => {
    await page.goto("/tests/fixtures/spa.html");

    await expect(page.locator("#text-1")).toContainText("555-999-0000");
  });

  test("replaces a number rendered later via an in-app route change, no page reload", async ({
    page,
  }) => {
    await page.goto("/tests/fixtures/spa.html");
    await expect(page.locator("#text-1")).toContainText("555-999-0000"); // wait for initial allocation

    await page.click("#navigate"); // simulates an SPA route change injecting new DOM content

    await expect(page.locator("#text-2")).toContainText("555-999-0000");
  });
});
