import { test, expect } from "@playwright/test";

// FR-049 (T114): shadow mode leaves the page's displayed numbers untouched while still
// recording the observed number.
test.describe("Shadow mode", () => {
  test("reports the observed number without replacing anything on the page", async ({ page }) => {
    let capturedBody = null;
    await page.route("**/v1/dni/shadow-observe", async (route) => {
      capturedBody = route.request().postDataJSON();
      await route.fulfill({ json: { recorded: true } });
    });

    await page.goto("/tests/fixtures/shadow.html");
    await page.waitForFunction(() => window.__shadowResult !== undefined);

    // The page's own markup — inserted by "another system" — must remain exactly as-is.
    const link = page.locator("#other-system-number");
    await expect(link).toHaveAttribute("href", "tel:5559998888");
    await expect(link).toHaveText("555-999-8888");

    expect(capturedBody).not.toBeNull();
    expect(capturedBody.observed_number).toBe("5559998888");
    expect(capturedBody.website_id).toBe("33333333-3333-3333-3333-333333333333");
  });
});
