import { test, expect } from "@playwright/test";

// FR-039: consent gating, grant-after-refusal, withdrawal, and the active pre-consent
// default-number write (not a no-op — the script writes the default number in even
// though the page's static markup shows something else).
test.describe("Consent gating", () => {
  test.beforeEach(async ({ page }) => {
    await page.route("**/v1/dni/allocate", async (route) => {
      await route.fulfill({
        json: {
          session_id: "session-1",
          number: "555-999-0000",
          expires_at: new Date(Date.now() + 1800000).toISOString(),
        },
      });
    });
    await page.route("**/v1/dni/consent", async (route) => {
      const body = route.request().postDataJSON();
      if (body.consent === "withdrawn") {
        await route.fulfill({ json: { number: "555-000-0000" } });
      } else {
        await route.fulfill({
          json: {
            session_id: "session-1",
            number: "555-999-0000",
            expires_at: new Date(Date.now() + 1800000).toISOString(),
          },
        });
      }
    });
  });

  test("before consent, actively writes the default number rather than leaving the page's own markup", async ({
    page,
  }) => {
    await page.goto("/tests/fixtures/consent.html");

    // The page's static markup says 555-123-4567; the configured default is 555-000-0000.
    // FR-039/FR-008: pre-consent replacement must show the default, proving an active write.
    await expect(page.locator("#text-1")).toContainText("555-000-0000");
    await expect(page.locator("#text-1")).not.toContainText("555-123-4567");
  });

  test("granting consent after initial refusal allocates and displays the tracking number", async ({
    page,
  }) => {
    await page.goto("/tests/fixtures/consent.html");
    await expect(page.locator("#text-1")).toContainText("555-000-0000"); // pre-consent state first

    await page.evaluate(() => window.grantConsentForTest());

    await expect(page.locator("#text-1")).toContainText("555-999-0000");
  });

  test("withdrawing consent reverts the page to the default number", async ({ page }) => {
    await page.goto("/tests/fixtures/consent.html");
    await page.evaluate(() => window.grantConsentForTest());
    await expect(page.locator("#text-1")).toContainText("555-999-0000");

    await page.evaluate(() => window.withdrawConsentForTest());

    await expect(page.locator("#text-1")).toContainText("555-000-0000");
  });
});
