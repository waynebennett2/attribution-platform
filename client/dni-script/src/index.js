// FR-008, FR-039, FR-050: entry point. Holds only presentation and session-continuity
// state (the allocated number(s), the entry URL for the life of this page view) — no
// allocation, attribution or qualification decision is ever made here (constitution:
// Insertion client boundary).

import { createAllocationClient } from "./allocation.js";
import { createReplacer, matchesPage } from "./replace.js";
import { readInitialConsent, subscribeToConsentChanges } from "./consent.js";
import { loadSession, saveSession, clearSession } from "./sessionStore.js";

function captureArrivalDetails() {
  const params = new URLSearchParams(window.location.search);
  return {
    landingPage: window.location.href,
    referrer: document.referrer || null,
    utm: {
      source: params.get("utm_source"),
      medium: params.get("utm_medium"),
      campaign: params.get("utm_campaign"),
      term: params.get("utm_term"),
      content: params.get("utm_content"),
    },
    gclid: params.get("gclid"),
    gbraid: params.get("gbraid"),
    wbraid: params.get("wbraid"),
    ga4ClientId: window.gtag ? undefined : null, // populated by the GA4 snippet if present
  };
}

export function initAttribution(config) {
  const {
    apiBaseUrl,
    websiteId,
    defaultNumber,
    configuredNumbers,
    heartbeatIntervalMs = 300000,
  } = config;

  const allocationClient = createAllocationClient({ apiBaseUrl, websiteId });
  const replacer = createReplacer({ configuredNumbers });

  // FR-050: one replacer per pool, built lazily and reused for the life of the page view —
  // each targets only that pool's own default_number, so pools never cross-replace.
  // Reuses the replacer's own observe() (added-nodes only — never re-fires on the
  // characterData change its own apply() just made, unlike a naive whole-document
  // observer would) so post-load/SPA-rendered content is covered per pool exactly as the
  // single-pool case already covers it below.
  const poolReplacers = new Map();
  function replacerForPool(pool) {
    if (!poolReplacers.has(pool.pool_id)) {
      const instance = createReplacer({ configuredNumbers: [pool.default_number] });
      instance.observe(() => multiPool?.allocations[pool.pool_id]?.number ?? pool.default_number);
      poolReplacers.set(pool.pool_id, instance);
    }
    return poolReplacers.get(pool.pool_id);
  }

  // Arrival details are captured once, at script load, and retained for the life of this
  // page view (FR-014) so they're still available if consent arrives later on this page.
  const arrivalAtLoad = captureArrivalDetails();

  let sessionId = null;
  let heartbeatTimer = null;
  let currentNumber = defaultNumber;

  // FR-050 multi-pool state: null for a single-pool website/session. pools is the
  // website's pool->number map; allocations maps pool_id -> { number, expiresAt } for
  // every pool this session currently holds.
  let multiPool = null;

  function showDefault() {
    // FR-008, FR-039: pre-consent replacement actively writes the default number in,
    // rather than depending on whatever the page's own static markup contains.
    sessionId = null;
    multiPool = null;
    currentNumber = defaultNumber;
    replacer.apply(currentNumber);
  }

  function showDefaultForPools(pools) {
    sessionId = null;
    multiPool = null;
    for (const pool of pools) {
      replacerForPool(pool).apply(pool.default_number);
    }
  }

  function stopHeartbeat() {
    if (heartbeatTimer) {
      clearInterval(heartbeatTimer);
      heartbeatTimer = null;
    }
  }

  function applyMultiPoolNumbers(pools, allocations) {
    for (const pool of pools) {
      const held = allocations[pool.pool_id];
      replacerForPool(pool).apply(held ? held.number : pool.default_number);
    }
  }

  function startHeartbeat() {
    stopHeartbeat();
    heartbeatTimer = setInterval(async () => {
      try {
        const result = await allocationClient.heartbeatWithRetry(sessionId);
        if (!result.still_valid) {
          stopHeartbeat();
          clearSession(websiteId);
          if (multiPool) {
            showDefaultForPools(multiPool.pools);
          } else {
            showDefault();
          }
          return;
        }

        if (multiPool && result.allocations) {
          for (const entry of result.allocations) {
            if (entry.still_valid) {
              multiPool.allocations[entry.pool_id] = { number: entry.number, expiresAt: undefined };
            } else {
              delete multiPool.allocations[entry.pool_id];
            }
          }
          applyMultiPoolNumbers(multiPool.pools, multiPool.allocations);
          const expiresAt = new Date(Date.now() + heartbeatIntervalMs * 6).toISOString();
          saveSession(websiteId, {
            sessionId,
            expiresAt,
            pools: multiPool.pools,
            allocations: multiPool.allocations,
          });
        } else {
          // Keep the stored expiry roughly in step with the server's, so a later
          // full-page navigation still recognizes this session as current rather than as expired.
          saveSession(websiteId, {
            sessionId,
            number: currentNumber,
            expiresAt: new Date(Date.now() + heartbeatIntervalMs * 6).toISOString(),
          });
        }
      } catch {
        // FR-012: the client already retried internally; a persistent failure here just
        // waits for the next scheduled tick rather than escalating further.
      }
    }, heartbeatIntervalMs);
  }

  function adopt(newSessionId, number, expiresAt) {
    sessionId = newSessionId;
    currentNumber = number;
    replacer.apply(currentNumber);
    saveSession(websiteId, { sessionId, number, expiresAt });
    startHeartbeat();
  }

  function adoptMultiPool(newSessionId, pools, allocations) {
    sessionId = newSessionId;
    multiPool = { pools, allocations };
    applyMultiPoolNumbers(pools, allocations);
    saveSession(websiteId, {
      sessionId,
      pools,
      allocations,
      expiresAt: new Date(Date.now() + heartbeatIntervalMs * 6).toISOString(),
    });
    startHeartbeat();
  }

  // FR-050: matches this page's content against the website's pool->number map and, for
  // whichever pools it finds that the session (if any) doesn't already hold, requests
  // their allocation — growing an existing session rather than starting a new one when one
  // is already known (research.md §15).
  async function runMultiPool(pools, existingSessionId, existingAllocations) {
    const allocations = { ...existingAllocations };
    const newlyMatchedPoolIds = pools
      .filter((pool) => !allocations[pool.pool_id])
      .filter((pool) => matchesPage(document.body, pool.default_number))
      .map((pool) => pool.pool_id);

    if (newlyMatchedPoolIds.length > 0) {
      const result = await allocationClient.allocate({
        consentGranted: true,
        arrival: arrivalAtLoad,
        matchedPoolIds: newlyMatchedPoolIds,
        sessionId: existingSessionId,
      });

      if (result.session_id) {
        for (const allocation of result.allocations || []) {
          allocations[allocation.pool_id] = { number: allocation.number };
        }
        adoptMultiPool(result.session_id, pools, allocations);
        return;
      }
    }

    if (existingSessionId) {
      // Nothing new matched (or the allocation attempt failed) — keep showing whatever
      // this session already holds.
      adoptMultiPool(existingSessionId, pools, allocations);
    } else {
      showDefaultForPools(pools);
    }
  }

  async function grantConsent() {
    const result = await allocationClient.allocate({
      consentGranted: true,
      arrival: arrivalAtLoad,
    });

    if (result.pools) {
      // FR-050: multi-pool website — the first call intentionally carries no
      // matched_pool_ids, so this response is pool metadata only; match locally, then
      // request the matched pools' allocations.
      await runMultiPool(result.pools, null, {});
      return;
    }

    if (result.session_id) {
      adopt(result.session_id, result.number, result.expires_at);
    } else {
      showDefault();
    }
  }

  async function withdrawConsent() {
    stopHeartbeat();
    if (sessionId) {
      await allocationClient.reportConsent({ sessionId, consent: "withdrawn" });
    }
    clearSession(websiteId);
    if (multiPool) {
      showDefaultForPools(multiPool.pools);
    } else {
      showDefault();
    }
  }

  // FR-010: a full page navigation re-runs this module from scratch — recover an
  // already-active session from localStorage (shared across tabs, per the "same visitor,
  // several concurrent tabs" edge case) rather than allocating a fresh one on every page.
  // FR-050: a cached multi-pool session still re-runs matching against the new page and,
  // if it finds a pool it doesn't yet hold, extends the existing session — the one
  // deliberate exception to "no second allocation call" (research.md §15).
  const existing = loadSession(websiteId);
  if (existing && existing.pools) {
    sessionId = existing.sessionId;
    multiPool = { pools: existing.pools, allocations: existing.allocations || {} };
    applyMultiPoolNumbers(existing.pools, existing.allocations || {});
    startHeartbeat();
    runMultiPool(existing.pools, existing.sessionId, existing.allocations || {});
  } else if (existing) {
    adopt(existing.sessionId, existing.number, existing.expiresAt);
  } else if (readInitialConsent()) {
    grantConsent(); // FR-039
  } else {
    showDefault();
  }

  subscribeToConsentChanges((granted) => {
    if (granted && !sessionId) {
      grantConsent();
    } else if (!granted && sessionId) {
      withdrawConsent();
    }
  });

  // Keeps post-load/SPA-rendered numbers in sync with whatever is currently shown. A
  // multi-pool page's per-pool coverage is wired directly into replacerForPool() above,
  // as each pool's replacer is created.
  replacer.observe(() => currentNumber);
}

if (typeof window !== "undefined" && window.__attributionConfig) {
  initAttribution(window.__attributionConfig);
}
