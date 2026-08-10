// FR-039, contracts/consent-contract.md: reads the platform-defined consent
// event/callback contract and notifies subscribers of the current state and any later
// change, for the life of the page view.

export function readInitialConsent() {
  return Boolean(window.__attributionConsent && window.__attributionConsent.granted);
}

export function subscribeToConsentChanges(onChange) {
  const handler = (event) => {
    const granted = Boolean(event.detail && event.detail.granted);
    onChange(granted);
  };
  window.addEventListener("attribution:consent-change", handler);
  return () => window.removeEventListener("attribution:consent-change", handler);
}
