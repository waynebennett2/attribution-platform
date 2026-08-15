import { test, expect } from "@playwright/test";

// FR-008: replaces every configured phone number occurrence — displayed text (in any
// digit-normalized formatting variant) and click-to-call targets (tel: links, and
// elements carrying the configurable marker attribute) — with the visitor's allocated
// tracking number.
test.describe("DNI number replacement", () => {
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

  test("replaces displayed text in every formatting variant with the allocated number", async ({
    page,
  }) => {
    await page.goto("/tests/fixtures/multi-page.html");

    // Each occurrence keeps its own original punctuation pattern (FR-009), so a
    // dash-separated source and a parens-separated source reformat differently even
    // though both received the identical new digit sequence.
    await expect(page.locator("#text-1")).toContainText("555-999-0000");
    await expect(page.locator("#text-2")).toContainText("(555) 999-0000");
  });

  test("replaces the tel: link href and its displayed text", async ({ page }) => {
    // Overrides the describe-level mock with a realistic allocated number: the real API
    // always returns the tracking number's DID as stored (E.164, per DidValidator), never
    // a bare national-format string like the describe-level mock uses for the
    // pattern-preservation tests above.
    await page.route("**/v1/dni/allocate", async (route) => {
      await route.fulfill({
        json: {
          session_id: "session-1",
          number: "+15559990000",
          expires_at: new Date(Date.now() + 1800000).toISOString(),
        },
      });
    });
    await page.goto("/tests/fixtures/multi-page.html");

    const telLink = page.locator("#tel-link");
    await expect(telLink).toHaveAttribute("href", "tel:+15559990000");
    // Digit counts differ (10-digit displayed text vs. 11-digit E.164 allocated number),
    // so the displayed text falls back to the number's plain +E.164 form rather than
    // guessing a pattern-preserving placement for the extra country-code digit.
    await expect(telLink).toContainText("+15559990000");
  });

  test("replaces an element carrying the marker attribute", async ({ page }) => {
    await page.goto("/tests/fixtures/multi-page.html");

    await expect(page.locator("#marker")).toHaveText("555-999-0000");
  });
});
