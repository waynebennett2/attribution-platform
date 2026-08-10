// FR-003, FR-010, FR-012: allocation and heartbeat calls per contracts/dni-api.md.
// Holds no allocation/attribution/qualification decision itself (constitution:
// Insertion client boundary) — it only calls the server and relays the result.

const HEARTBEAT_RETRY_DELAYS_MS = [2000, 5000]; // quick backoff within one 5-minute interval

function randomClientToken() {
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

export function createAllocationClient({
  apiBaseUrl,
  websiteId,
  clientToken = randomClientToken(),
}) {
  async function postJson(path, body) {
    const response = await fetch(`${apiBaseUrl}${path}`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Attribution-Client-Token": clientToken,
      },
      body: JSON.stringify(body),
    });

    if (!response.ok) {
      throw new Error(`${path} failed with status ${response.status}`);
    }

    return response.json();
  }

  async function allocate({ consentGranted, arrival }) {
    return postJson("/v1/dni/allocate", {
      website_id: websiteId,
      client_token: clientToken,
      consent_granted: consentGranted,
      landing_page: arrival.landingPage,
      referrer: arrival.referrer,
      utm: arrival.utm,
      gclid: arrival.gclid,
      gbraid: arrival.gbraid,
      wbraid: arrival.wbraid,
      ga4_client_id: arrival.ga4ClientId,
    });
  }

  // FR-012: retried with quick backoff before giving up until the next scheduled
  // heartbeat — a transient network blip should not compound into a bigger gap in the
  // activity signal than the failure itself caused. The server-side 30-minute timeout is
  // unaffected either way.
  async function heartbeatWithRetry(sessionId) {
    let lastError;
    for (const delay of [0, ...HEARTBEAT_RETRY_DELAYS_MS]) {
      if (delay > 0) {
        await new Promise((resolve) => setTimeout(resolve, delay));
      }
      try {
        return await postJson("/v1/dni/heartbeat", { session_id: sessionId });
      } catch (error) {
        lastError = error;
      }
    }
    throw lastError;
  }

  async function reportConsent({ sessionId, consent, arrival }) {
    return postJson("/v1/dni/consent", {
      session_id: sessionId,
      client_token: clientToken,
      website_id: websiteId,
      consent,
      arrival_details: arrival
        ? {
            landing_page: arrival.landingPage,
            referrer: arrival.referrer,
            utm: arrival.utm,
            gclid: arrival.gclid,
            gbraid: arrival.gbraid,
            wbraid: arrival.wbraid,
            ga4_client_id: arrival.ga4ClientId,
          }
        : undefined,
    });
  }

  return { allocate, heartbeatWithRetry, reportConsent, clientToken };
}
