// Meridian & Manor Healthcare demo site — wires this static mock up to the real
// Call Attribution Platform DNI client (client/dni-script/src/index.js), the same way the
// project's own manual test fixtures do (client/dni-script/tests/fixtures/demo.html).
//
// IMPORTANT: this mock currently represents ONE seeded Website row
// (00000000-0000-0000-0000-000000000001, from scripts/seed-dev-data.sql) with ONE number
// pool, and the DNI client keys its session purely by websiteId. That means the whole site
// shares a single default number and, after consent, a single allocated tracking number —
// it is NOT capable of giving each of the three care homes below its own independent
// tracking number without seeding three separate Website/pool rows. All three "Call now"
// numbers on the results page are deliberately the same for that reason.
(function () {
  "use strict";

  var WEBSITE_ID = "00000000-0000-0000-0000-000000000001"; // matches seed-dev-data.sql
  var DEFAULT_NUMBER = "01632 960000"; // matches the seeded website's default_number
  var API_BASE_KEY = "mm-demo:apiBase";
  var CONSENT_KEY = "mm-demo:consent";
  var DEFAULT_API_BASE = "http://localhost:8080"; // nginx port from docker-compose.yml

  var params = new URLSearchParams(window.location.search);
  if (params.get("api")) {
    window.localStorage.setItem(API_BASE_KEY, params.get("api"));
  }
  var apiBaseUrl = window.localStorage.getItem(API_BASE_KEY) || DEFAULT_API_BASE;

  window.MM_DEMO = {
    WEBSITE_ID: WEBSITE_ID,
    apiBaseUrl: apiBaseUrl,
  };

  window.__attributionConfig = {
    apiBaseUrl: apiBaseUrl,
    websiteId: WEBSITE_ID,
    defaultNumber: DEFAULT_NUMBER,
    configuredNumbers: [DEFAULT_NUMBER],
    heartbeatIntervalMs: 60000, // matches the seeded website's short demo heartbeat_interval_seconds
  };

  var storedConsent = window.localStorage.getItem(CONSENT_KEY);
  if (storedConsent === "granted") {
    window.__attributionConsent = { granted: true };
  } else if (storedConsent === "declined") {
    window.__attributionConsent = { granted: false };
  }
  // Otherwise leave window.__attributionConsent unset — the DNI client treats that as
  // "pending" (FR-039) and shows the default number until the banner below is answered.

  function dispatchConsent(granted) {
    window.__attributionConsent = { granted: granted };
    window.dispatchEvent(
      new CustomEvent("attribution:consent-change", { detail: { granted: granted } })
    );
  }

  function initConsentBanner() {
    var banner = document.getElementById("consent-banner");
    if (!banner) return;
    var acceptBtn = document.getElementById("consent-accept");
    var declineBtn = document.getElementById("consent-decline");
    var manageLink = document.getElementById("manage-cookies");

    function hide() {
      banner.classList.remove("visible");
    }
    function show() {
      banner.classList.add("visible");
    }

    if (!storedConsent) {
      show();
    }

    acceptBtn.addEventListener("click", function () {
      window.localStorage.setItem(CONSENT_KEY, "granted");
      dispatchConsent(true);
      hide();
    });
    declineBtn.addEventListener("click", function () {
      window.localStorage.setItem(CONSENT_KEY, "declined");
      dispatchConsent(false);
      hide();
    });
    if (manageLink) {
      manageLink.addEventListener("click", function (event) {
        event.preventDefault();
        show();
      });
    }
  }

  function initSettingsPanel() {
    var input = document.getElementById("api-base-input");
    var saveBtn = document.getElementById("api-base-save");
    if (!input || !saveBtn) return;
    input.value = apiBaseUrl;
    saveBtn.addEventListener("click", function () {
      var value = input.value.trim().replace(/\/$/, "");
      if (value) {
        window.localStorage.setItem(API_BASE_KEY, value);
        window.location.reload();
      }
    });
  }

  function initStatusPanel() {
    var el = document.getElementById("dni-status");
    if (!el) return;
    if (!params.has("debug")) {
      el.style.display = "none";
      return;
    }
    function render() {
      var raw = window.localStorage.getItem("attribution:session:" + WEBSITE_ID);
      var session = raw ? JSON.parse(raw) : null;
      el.textContent =
        "api base:  " + apiBaseUrl + "\n" +
        "website:   " + WEBSITE_ID + "\n" +
        "consent:   " + (window.__attributionConsent ? window.__attributionConsent.granted : "pending") + "\n" +
        "session:   " + (session ? JSON.stringify(session) : "(none)");
    }
    setInterval(render, 500);
    render();
  }

  document.addEventListener("DOMContentLoaded", function () {
    initConsentBanner();
    initSettingsPanel();
    initStatusPanel();
  });
})();
