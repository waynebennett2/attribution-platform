// FR-008, FR-009, FR-011: DOM replacement. Digit-normalized matching (a configured
// number matches any formatting variant of the same digits); click-to-call targets are
// tel: links plus a configurable marker attribute (not arbitrary onclick handlers);
// MutationObserver covers numbers rendered after initial load / SPA navigation;
// main-document (light DOM) only — no iframe or shadow-DOM traversal in this version.

const MARKER_ATTRIBUTE = "data-attribution-number";
const SKIP_TAGS = new Set(["SCRIPT", "STYLE", "NOSCRIPT", "TEXTAREA"]);

function toDigits(text) {
  return (text.match(/\d/g) || []).join("");
}

// Builds a regex that matches the configured number's digit sequence with any
// non-digit punctuation (spaces, dashes, parens, dots) optionally interleaved, so
// "555-123-4567", "(555) 123-4567" and "5551234567" are all recognized as the same number.
function buildMatchPattern(configuredNumber) {
  const digits = toDigits(configuredNumber);
  if (digits.length === 0) {
    return null;
  }
  const escaped = digits.split("").join("[^0-9]*");
  return new RegExp(`\\+?[^0-9]*${escaped}`, "g");
}

// Rewrites matchedText's digits to newNumber's digits, preserving the matched text's
// own punctuation/spacing pattern (FR-009: "written using the matched text's own visual
// pattern rather than a fixed format").
function reformatPreservingPattern(matchedText, newNumberDigits) {
  let digitIndex = 0;
  let result = "";
  for (const ch of matchedText) {
    if (/[0-9]/.test(ch)) {
      result += digitIndex < newNumberDigits.length ? newNumberDigits[digitIndex] : "";
      digitIndex += 1;
    } else {
      result += ch;
    }
  }
  return result;
}

function replaceInTextNode(node, patterns, newNumber) {
  const newDigits = toDigits(newNumber);
  let text = node.textContent;
  let changed = false;

  for (const pattern of patterns) {
    pattern.lastIndex = 0;
    text = text.replace(pattern, (match) => {
      changed = true;
      return reformatPreservingPattern(match, newDigits);
    });
  }

  if (changed) {
    node.textContent = text;
  }
}

// Digit-normalized comparison against the configured numbers' own digit sequences —
// href values carry no punctuation to normalize away, so this is a direct set lookup
// rather than reusing the text-matching regex patterns.
function replaceTelLinks(root, configuredDigitSets, newNumber) {
  const newDigits = toDigits(newNumber);
  root.querySelectorAll('a[href^="tel:"]').forEach((anchor) => {
    const hrefDigits = toDigits(anchor.getAttribute("href") || "");
    if (configuredDigitSets.has(hrefDigits)) {
      anchor.setAttribute("href", `tel:+${newDigits}`);
    }
  });
}

function replaceMarkedElements(root, newNumber) {
  root.querySelectorAll(`[${MARKER_ATTRIBUTE}]`).forEach((element) => {
    element.textContent = newNumber;
    if (element.tagName === "A" && element.hasAttribute("href")) {
      element.setAttribute("href", `tel:+${toDigits(newNumber)}`);
    }
  });
}

function walkTextNodes(root, callback) {
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
    acceptNode(node) {
      const parentTag = node.parentElement?.tagName;
      return parentTag && SKIP_TAGS.has(parentTag)
        ? NodeFilter.FILTER_REJECT
        : NodeFilter.FILTER_ACCEPT;
    },
  });
  let current = walker.nextNode();
  while (current) {
    callback(current);
    current = walker.nextNode();
  }
}

export function createReplacer({ configuredNumbers }) {
  // apply() is called repeatedly over a page view's lifetime with different numbers
  // (default, then allocated, then default again on withdrawal). Each call must be able
  // to replace whatever is CURRENTLY on the page, which after the first call is no
  // longer the originally configured number — it's whatever apply() wrote last time. So
  // match patterns are rebuilt from configuredNumbers plus the last-applied number, not
  // configuredNumbers alone.
  let lastAppliedNumber = null;

  function currentMatchTargets() {
    const numbers = lastAppliedNumber
      ? [...configuredNumbers, lastAppliedNumber]
      : configuredNumbers;
    return {
      patterns: numbers.map(buildMatchPattern).filter(Boolean),
      digitSets: new Set(numbers.map(toDigits).filter((d) => d.length > 0)),
    };
  }

  function replaceAll(root, number) {
    const { patterns, digitSets } = currentMatchTargets();
    walkTextNodes(root, (node) => replaceInTextNode(node, patterns, number));
    replaceTelLinks(root, digitSets, number);
    replaceMarkedElements(root, number);
  }

  function apply(number) {
    // Main document (light DOM) only — iframes and shadow DOM subtrees are out of scope
    // for this version (typically third-party embeds outside the site owner's control).
    replaceAll(document.body, number);
    lastAppliedNumber = number;
  }

  // getCurrentNumber is a function, not a fixed value: whichever number is currently
  // displayed (default or allocated) can change over the page view's lifetime (consent
  // grant/withdrawal), and newly-added nodes must be replaced with whatever is current
  // at the moment they appear, not whatever was current when observation started.
  function observe(getCurrentNumber) {
    const observer = new MutationObserver((mutations) => {
      for (const mutation of mutations) {
        mutation.addedNodes.forEach((added) => {
          if (added.nodeType === Node.ELEMENT_NODE || added.nodeType === Node.TEXT_NODE) {
            replaceAll(added.parentNode || document.body, getCurrentNumber());
          }
        });
      }
    });
    observer.observe(document.body, { childList: true, subtree: true, characterData: true });
    return observer;
  }

  return { apply, observe };
}
