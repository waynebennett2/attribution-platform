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

// This project's numbers always follow the standard Ofcom UK numbering plan: E.164 form
// is "44" + national significant number, with the national format's leading trunk "0"
// dropped (e.g. "01632 960000" -> "+441632960000"). Stripping whichever of those two
// prefixes is present recovers the "core" digits, which can then be re-expressed in
// either form as needed.
function ukCoreDigits(digits) {
  if (digits.startsWith("44")) {
    return digits.slice(2);
  }
  if (digits.startsWith("0")) {
    return digits.slice(1);
  }
  return null;
}

// Expands a digit string into every form (national and/or E.164) that should be
// recognized as the same underlying number. Needed because a number already rewritten on
// the page in one form by an earlier apply() call — e.g. a tel: href written as E.164 for
// a number that was configured in national format — must still be found and updated by a
// later apply() call, even though the two calls' own number strings are in different forms.
function ukFormVariants(digits) {
  const core = ukCoreDigits(digits);
  return core === null ? [digits] : [`0${core}`, `44${core}`];
}

// Builds a regex that matches the configured number's digit sequence with any
// non-digit punctuation (spaces, dashes, parens, dots) optionally interleaved, so
// "555-123-4567", "(555) 123-4567" and "5551234567" are all recognized as the same number.
//
// Deliberately has no leading `[^0-9]*` before the first digit: since the match is
// unanchored, a search for the digit sequence already skips over any preceding text
// (e.g. "(" before an area code) without needing to consume it. An earlier version added
// `\+?[^0-9]*` here to optionally absorb a leading "+" or punctuation immediately before
// the number, but because that group can match zero-or-more of *any* non-digit run, and
// JS regex tries the leftmost start position first, it actually swallowed everything back
// to the start of the text node whenever no other digit character interrupted it — e.g.
// matching all of "Call us on " in "Call us on 555-000-0000", not just the number itself.
//
// The one exception is a single, literal `(` immediately before the digits: it's bounded
// (matches at most one specific character, so it can't reintroduce the swallowing bug
// above), and pulling it into the match pairs it with the `)` that a pattern like
// "(555) 000-0000" already captures via the interleaved gap between digit groups. Without
// this, a case where the digit counts don't line up (see reformatPreservingPattern) would
// drop the interior `)` but leave the `(` behind, producing a visibly unbalanced bracket.
function buildMatchPattern(digits) {
  if (!digits || digits.length === 0) {
    return null;
  }
  const escaped = digits.split("").join("[^0-9]*");
  return new RegExp(`\\(?${escaped}`, "g");
}

// Rewrites matchedText's digits to newNumber's digits, preserving the matched text's
// own punctuation/spacing pattern (FR-009: "written using the matched text's own visual
// pattern rather than a fixed format").
function reformatPreservingPattern(matchedText, newNumberDigits) {
  const matchedDigitCount = (matchedText.match(/\d/g) || []).length;
  let digitsToMap = newNumberDigits;

  if (newNumberDigits.length !== matchedDigitCount) {
    // A mismatched digit count generally can't be safely mapped onto the old pattern
    // without locale-specific phone number knowledge — treating any extra leading digits
    // as "a country code, so just prefix them" is right for NANP (+1 555-000-0000 is the
    // 10-digit national number with a single digit "1" prepended) but wrong for most other
    // countries, since e.g. UK numbers also drop the national format's leading trunk "0"
    // when adding "+44". This project's numbers are always standard Ofcom UK numbers
    // though (per the user), so that conversion is known and safe to apply: re-express
    // the new number in whichever of national ("0"-prefixed) or E.164 ("44"-prefixed) form
    // matches the old pattern's own digit count, then map onto it as normal below.
    const core = ukCoreDigits(newNumberDigits);
    const asNational = core === null ? null : `0${core}`;
    const asE164 = core === null ? null : `44${core}`;
    if (asNational && asNational.length === matchedDigitCount) {
      digitsToMap = asNational;
    } else if (asE164 && asE164.length === matchedDigitCount) {
      digitsToMap = asE164;
    } else {
      // Not a recognizable UK national/E.164 pairing — fall back to the number's own
      // plain +E.164 form, which is always correct even though it won't preserve the
      // original pattern's formatting.
      return `+${newNumberDigits}`;
    }
  }

  let digitIndex = 0;
  let result = "";
  for (const ch of matchedText) {
    if (/[0-9]/.test(ch)) {
      result += digitsToMap[digitIndex];
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

// Builds a dialable tel: URI for newNumber. If newNumber's own text already starts with
// "+" it's already E.164 (as the allocated tracking number always is) and is used as-is.
// Otherwise it's a website's configured default number, typically written in UK
// national/local format (e.g. "01632 960000", leading trunk "0" included) — converted to
// proper E.164 via the standard Ofcom UK rule (drop the leading "0", prepend "44") so the
// link works from any device/network, not just within the UK.
function toTelHref(newNumber) {
  const digits = toDigits(newNumber);
  if (newNumber.trim().startsWith("+")) {
    return `tel:+${digits}`;
  }
  const core = ukCoreDigits(digits);
  return core === null ? `tel:${digits}` : `tel:+44${core}`;
}

// Digit-normalized comparison against the configured numbers' own digit sequences —
// href values carry no punctuation to normalize away, so this is a direct set lookup
// rather than reusing the text-matching regex patterns.
function replaceTelLinks(root, configuredDigitSets, newNumber) {
  const telHref = toTelHref(newNumber);
  root.querySelectorAll('a[href^="tel:"]').forEach((anchor) => {
    const hrefDigits = toDigits(anchor.getAttribute("href") || "");
    if (configuredDigitSets.has(hrefDigits)) {
      anchor.setAttribute("href", telHref);
    }
  });
}

function replaceMarkedElements(root, newNumber) {
  root.querySelectorAll(`[${MARKER_ATTRIBUTE}]`).forEach((element) => {
    element.textContent = newNumber;
    if (element.tagName === "A" && element.hasAttribute("href")) {
      element.setAttribute("href", toTelHref(newNumber));
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

// FR-050: does root currently display `number` anywhere (text or a tel: link)? Used to
// locally match a multi-pool website's pools against the current page — the same
// digit-normalized matching FR-009 already defines for a single configured number, reused
// rather than reimplemented.
export function matchesPage(root, number) {
  const digits = toDigits(number);
  if (!digits) {
    return false;
  }

  const variants = ukFormVariants(digits);
  const patterns = variants.map(buildMatchPattern).filter(Boolean);
  const text = root.textContent || "";
  if (
    patterns.some((pattern) => {
      pattern.lastIndex = 0;
      return pattern.test(text);
    })
  ) {
    return true;
  }

  const digitSets = new Set(variants);
  return Array.from(root.querySelectorAll('a[href^="tel:"]')).some((anchor) =>
    digitSets.has(toDigits(anchor.getAttribute("href") || ""))
  );
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
    // Each number expands to both its national and E.164 digit forms (see
    // ukFormVariants), since the page may currently show either form depending on what a
    // previous apply() call last wrote.
    const digitVariants = numbers
      .map(toDigits)
      .filter((d) => d.length > 0)
      .flatMap(ukFormVariants);
    return {
      patterns: digitVariants.map(buildMatchPattern).filter(Boolean),
      digitSets: new Set(digitVariants),
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
