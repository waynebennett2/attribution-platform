import { test, expect } from "@playwright/test";

// FR-010, Edge Cases §Number allocation and display: the same visitor opening several
// concurrent tabs on the same site must see the one number allocated to that session in
// every tab — localStorage (shared across tabs of the same origin, unlike
// sessionStorage) is what makes this work across full page loads in separate tabs.
test.describe("Session stickiness across concurrent tabs", () => {
  test("a second tab reuses the first tab's allocated number instead of allocating again", async ({
    context,
  }) => {
    let allocateCallCount = 0;
    await context.route("**/v1/dni/allocate", async (route) => {
      allocateCallCount += 1;
      await route.fulfill({
        json: {
          session_id: "session-shared",
          number: "555-999-0000",
          expires_at: new Date(Date.now() + 1800000).toISOString(),
        },
      });
    });

    const tab1 = await context.newPage();
    await tab1.goto("/tests/fixtures/multi-tab.html");
    await expect(tab1.locator("#text-1")).toContainText("555-999-0000");

    const tab2 = await context.newPage();
    await tab2.goto("/tests/fixtures/multi-tab.html");
    await expect(tab2.locator("#text-1")).toContainText("555-999-0000");

    // Only the first tab should have called /allocate; the second recovered the
    // existing session from localStorage rather than requesting a new number.
    expect(allocateCallCount).toBe(1);
  });
});
