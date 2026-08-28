// Meridian & Manor Healthcare demo site — wires this static mock up to the real
// Call Attribution Platform DNI client (client/dni-script/src/index.js), the same way the
// project's own manual test fixtures do (client/dni-script/tests/fixtures/demo.html).
//
// This mock represents ONE seeded Website row (00000000-0000-0000-0000-000000000001, from
// scripts/seed-dev-data.sql) with multi_pool_enabled=1 and three number pools (FR-050), one
// per care home on care-homes.html — each pool has its own default_number, matched locally
// against the page content, so each care home gets its own independently allocated tracking
// number rather than sharing one across the site.
(function () {
  "use strict";

  var WEBSITE_ID = "00000000-0000-0000-0000-000000000001"; // matches seed-dev-data.sql
  var DEFAULT_NUMBER = "01632 960000"; // matches the seeded website's default_number
  var API_BASE_KEY = "mm-demo:apiBase";
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
    // No page-wide configured number to replace: each of the three pools' own
    // default_number (matched locally against the page) drives its own replacement.
    configuredNumbers: [],
    heartbeatIntervalMs: 60000, // matches the seeded website's short demo heartbeat_interval_seconds
  };

  // Tracking numbers are injected directly with no consent gate — the DNI client is
  // told consent is granted before it ever runs.
  window.__attributionConsent = { granted: true };

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
    initSettingsPanel();
    initStatusPanel();
  });
})();
