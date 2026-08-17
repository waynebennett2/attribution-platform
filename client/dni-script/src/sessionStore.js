// FR-010: keeps the same allocated number displayed for the whole of the visitor's
// active session — across full page navigations, not just SPA route changes — and
// satisfies the "same visitor, several concurrent tabs" edge case, since localStorage
// (unlike sessionStorage) is shared across every tab of the same origin.

const KEY_PREFIX = "attribution:session:";

function key(websiteId) {
  return `${KEY_PREFIX}${websiteId}`;
}

// FR-050: pools/allocations are present only for a multi-pool session (undefined for a
// single-pool one, leaving its stored shape exactly what it always has been) — pools is
// the website's pool->number map (fetched once, cached across page views per research.md
// §15), allocations maps pool_id -> { number, expiresAt } for every pool this session
// currently holds.
export function loadSession(websiteId) {
  try {
    const raw = window.localStorage.getItem(key(websiteId));
    if (!raw) {
      return null;
    }
    const parsed = JSON.parse(raw);
    if (!parsed.sessionId || !parsed.expiresAt || (!parsed.number && !parsed.pools)) {
      return null;
    }
    if (new Date(parsed.expiresAt).getTime() <= Date.now()) {
      return null; // expired; treat as no session rather than trusting a stale entry
    }
    return parsed;
  } catch {
    return null; // storage unavailable (e.g. private browsing) — fail open to "no session"
  }
}

export function saveSession(websiteId, { sessionId, number, expiresAt, pools, allocations }) {
  try {
    window.localStorage.setItem(
      key(websiteId),
      JSON.stringify({ sessionId, number, expiresAt, pools, allocations })
    );
  } catch {
    // Storage unavailable — the session simply won't survive a full page navigation;
    // in-memory state still covers the current page view.
  }
}

export function clearSession(websiteId) {
  try {
    window.localStorage.removeItem(key(websiteId));
  } catch {
    // Nothing to do if storage is unavailable.
  }
}
