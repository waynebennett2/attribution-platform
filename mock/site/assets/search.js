// Page 1 postcode search. This is deliberately a pure front-end demo: any postcode that
// looks roughly UK-shaped is accepted, and Page 2 always shows the same three mock care
// homes — there is no real postcode lookup behind this (per mock/Mock website.md).
(function () {
  "use strict";

  var UK_POSTCODE_RE = /^[A-Za-z]{1,2}\d[A-Za-z\d]?\s*\d[A-Za-z]{2}$/;

  document.addEventListener("DOMContentLoaded", function () {
    var form = document.getElementById("postcode-form");
    var input = document.getElementById("postcode-input");
    var error = document.getElementById("postcode-error");
    if (!form) return;

    form.addEventListener("submit", function (event) {
      event.preventDefault();
      var value = input.value.trim();

      if (!value) {
        error.textContent = "Please enter a postcode to search.";
        return;
      }
      if (!UK_POSTCODE_RE.test(value)) {
        error.textContent = "That doesn't look like a UK postcode — please check and try again.";
        return;
      }

      error.textContent = "";
      window.location.href = "care-homes.html?postcode=" + encodeURIComponent(value.toUpperCase());
    });
  });
})();
