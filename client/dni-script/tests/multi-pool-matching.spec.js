import { test, expect } from "@playwright/test";

// FR-050, User Story 1 Acceptance Scenario 7: a directory-style page with several static
// numbers, each belonging to a different pool, gets each occurrence independently
// replaced with that pool's own allocated number — three different tracking numbers on
// one page load, not one shared number.
test.describe("Multi-pool DNI matching", () => {
  test("each pool's occurrence is replaced with that pool's own allocated number, from one page load", async ({
    page,
  }) => {
    let allocateCallCount = 0;
    const seenBodies = [];

    await page.route("**/v1/dni/allocate", async (route) => {
      allocateCallCount += 1;
      const body = route.request().postDataJSON();
      seenBodies.push(body);

      if (!body.matched_pool_ids || body.matched_pool_ids.length === 0) {
        // First call: pool metadata only, per dni-api.md's pre-match multi-pool shape.
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

      const numbersByPool = {
        "pool-a": "01632 960391",
        "pool-b": "01632 960392",
        "pool-c": "01632 960393",
      };
      await route.fulfill({
        json: {
          session_id: "session-multi-1",
          allocations: body.matched_pool_ids.map((poolId) => ({
            pool_id: poolId,
            number: numbersByPool[poolId],
            expires_at: new Date(Date.now() + 1800000).toISOString(),
          })),
        },
      });
    });

    await page.goto("/tests/fixtures/multi-pool.html");

    await expect(page.locator("#location-a")).toContainText("01632 960391");
    await expect(page.locator("#location-b")).toContainText("01632 960392");
    await expect(page.locator("#location-c")).toContainText("01632 960393");

    // Each location shows only its own number — never another pool's.
    await expect(page.locator("#location-a")).not.toContainText("01632 960392");
    await expect(page.locator("#location-a")).not.toContainText("01632 960393");

    // One pools-map call, one matched-pool-ids call — not one call per pool.
    expect(allocateCallCount).toBe(2);
    expect(seenBodies[1].matched_pool_ids.sort()).toEqual(["pool-a", "pool-b", "pool-c"]);
  });

  test("a pool with no number available falls back to its own default number, while the others still allocate", async ({
    page,
  }) => {
    await page.route("**/v1/dni/allocate", async (route) => {
      const body = route.request().postDataJSON();

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

      // pool-a is exhausted — omitted from allocations entirely, per dni-api.md.
      await route.fulfill({
        json: {
          session_id: "session-multi-2",
          allocations: [
            {
              pool_id: "pool-b",
              number: "01632 960392",
              expires_at: new Date(Date.now() + 1800000).toISOString(),
            },
            {
              pool_id: "pool-c",
              number: "01632 960393",
              expires_at: new Date(Date.now() + 1800000).toISOString(),
            },
          ],
        },
      });
    });

    await page.goto("/tests/fixtures/multi-pool.html");

    await expect(page.locator("#location-a")).toContainText("01632 960301"); // its own default, unchanged
    await expect(page.locator("#location-b")).toContainText("01632 960392");
    await expect(page.locator("#location-c")).toContainText("01632 960393");
  });
});
