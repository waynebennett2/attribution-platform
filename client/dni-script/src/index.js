// FR-008, FR-039: entry point. Holds only presentation and session-continuity state
// (the allocated number, the entry URL for the life of this page view) — no allocation,
// attribution or qualification decision is ever made here (constitution: Insertion
// client boundary).

import { createAllocationClient } from "./allocation.js";
import { createReplacer } from "./replace.js";
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

  // Arrival details are captured once, at script load, and retained for the life of this
  // page view (FR-014) so they're still available if consent arrives later on this page.
  const arrivalAtLoad = captureArrivalDetails();

  let sessionId = null;
  let heartbeatTimer = null;
  let currentNumber = defaultNumber;

  function showDefault() {
    // FR-008, FR-039: pre-consent replacement actively writes the default number in,
    // rather than depending on whatever the page's own static markup contains.
    sessionId = null;
    currentNumber = defaultNumber;
    replacer.apply(currentNumber);
  }

  function stopHeartbeat() {
    if (heartbeatTimer) {
      clearInterval(heartbeatTimer);
      heartbeatTimer = null;
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
          showDefault();
        } else {
          // Keep the stored expiry roughly in step with the server's, so a later full-page
          // navigation still recognizes this session as current rather than as expired.
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

  async function grantConsent() {
    const result = await allocationClient.allocate({
      consentGranted: true,
      arrival: arrivalAtLoad,
    });
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
    showDefault();
  }

  // FR-010: a full page navigation re-runs this module from scratch — recover an
  // already-active session from localStorage (shared across tabs, per the "same visitor,
  // several concurrent tabs" edge case) rather than allocating a fresh one on every page.
  const existing = loadSession(websiteId);
  if (existing) {
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

  // Keeps post-load/SPA-rendered numbers in sync with whatever is currently shown.
  replacer.observe(() => currentNumber);
}

if (typeof window !== "undefined" && window.__attributionConfig) {
  initAttribution(window.__attributionConfig);
}
