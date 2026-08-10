// FR-049 (T117): optional per-website shadow mode. Reads whatever number another system
// (e.g. Mediahawk) already displayed and reports it via POST /v1/dni/shadow-observe,
// without replacing anything on the page — leaves the page's own markup untouched.

function toDigits(text) {
  return (text.match(/\d/g) || []).join("");
}

function findObservedNumber() {
  const telLink = Array.from(document.querySelectorAll('a[href^="tel:"]')).find((anchor) => {
    const hrefDigits = toDigits(anchor.getAttribute("href") || "");
    // Shadow mode observes whichever number is currently on the page — it doesn't need to
    // match a configured number (the whole point is to see what the OTHER system inserted).
    return hrefDigits.length >= 8;
  });
  return telLink ? toDigits(telLink.getAttribute("href")) : null;
}

export async function reportShadowObservation({ apiBaseUrl, websiteId, sessionId, arrival }) {
  const observedNumber = findObservedNumber();
  if (!observedNumber) {
    return null;
  }

  const response = await fetch(`${apiBaseUrl}/v1/dni/shadow-observe`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      website_id: websiteId,
      session_id: sessionId,
      observed_number: observedNumber,
      landing_page: arrival.landingPage,
      referrer: arrival.referrer,
      utm: arrival.utm,
      gclid: arrival.gclid,
      gbraid: arrival.gbraid,
      wbraid: arrival.wbraid,
      ga4_client_id: arrival.ga4ClientId,
    }),
  });

  if (!response.ok) {
    throw new Error(`shadow-observe failed with status ${response.status}`);
  }

  return response.json();
}
