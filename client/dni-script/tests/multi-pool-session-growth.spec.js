import { test, expect } from "@playwright/test";

// FR-050, research.md §15: a later page view that matches a pool the session doesn't yet
// hold grows that same session — one more allocation added, not a second session started.
test.describe("Multi-pool session growth across page views", () => {
  test("navigating to a page matching a new pool keeps the existing session and adds the newly-matched pool's allocation", async ({
    page,
  }) => {
    const allocateBodies = [];
    let sessionCounter = 0;

    await page.route("**/v1/dni/allocate", async (route) => {
      const body = route.request().postDataJSON();
      allocateBodies.push(body);

      if (!body.matched_pool_ids || body.matched_pool_ids.length === 0) {
        await route.fulfill({
          json: {
            session_id: null,
            reason: "pending_match",
            pools: [
              { pool_id: "pool-a", default_number: "01632 960301" },
              { pool_id: "pool-b", default_number: "01632 960302" },
              { pool_id: "pool-c", default_number: "01632 960303" },
            ],
          },
        });
        return;
      }

      // The client always identifies its resumed session; a fresh first call never sends one.
      if (!body.session_id) {
        sessionCounter += 1;
      }
      const sessionId = "session-growth-1"; // only ever one real session across this test

      const numbersByPool = {
        "pool-a": "01632 960391",
        "pool-b": "01632 960392",
        "pool-c": "01632 960393",
      };
      await route.fulfill({
        json: {
          session_id: sessionId,
          allocations: body.matched_pool_ids.map((poolId) => ({
            pool_id: poolId,
            number: numbersByPool[poolId],
            expires_at: new Date(Date.now() + 1800000).toISOString(),
          })),
        },
      });
    });

    // Page 1: only pool A's number is present. Session is created holding pool A alone.
    await page.goto("/tests/fixtures/multi-pool-growth-start.html");
    await expect(page.locator("#location-a")).toContainText("01632 960391");

    // Page 2 (full navigation — a new page load, not an SPA route change): pools B and C
    // now appear too. The existing session must grow rather than a second one starting.
    await page.goto("/tests/fixtures/multi-pool.html");
    await expect(page.locator("#location-a")).toContainText("01632 960391"); // unchanged
    await expect(page.locator("#location-b")).toContainText("01632 960392"); // newly gained
    await expect(page.locator("#location-c")).toContainText("01632 960393"); // newly gained

    // Exactly one session was ever created for this visit.
    expect(sessionCounter).toBe(1);

    // The second page's matched-pool-ids call identified the already-known session so the
    // server could grow it, and did not re-request pool A (already held).
    const growthCall = allocateBodies.find((b) => b.matched_pool_ids?.includes("pool-b"));
    expect(growthCall.session_id).toBe("session-growth-1");
    expect(growthCall.matched_pool_ids).not.toContain("pool-a");
  });
});
