// color-contrast.util.ts - adaptive text-colour helper for coloured chips.
//
// Purpose: given an arbitrary hex background colour (e.g. a route colour
// coming from the vehicle feed), decide whether black or white text will
// be readable on top of it. Chip active-state uses the route colour as
// background, so foreground text needs to flip based on background
// luminance - otherwise T1 yellow-orange gets white text and disappears.
//
// Consumed by: RouteChipsComponent (SP1-10 filter refactor).
//
// Approach: simple relative-luminance approximation. Not full WCAG sRGB
// gamma-correction - portfolio-scale palette (Sydney Trains lines) is
// small enough that the perceptual approximation is indistinguishable
// from the WCAG formula in practice and half the code.

/**
 * Return the CSS colour string (`'#000000'` or `'#ffffff'`) that will be
 * legible on top of the given hex background.
 *
 * Acceptance criteria:
 * - Input format: 7-char hex string with leading `#` (matches the shape
 *   the backend sends on `Vehicle.routeColor`, e.g. `'#0098CD'`).
 * - Parse the 6 hex digits into R, G, B integers (0-255 each).
 * - Compute perceived luminance using the standard weighted formula:
 *     luminance = (0.299 * R + 0.587 * G + 0.114 * B) / 255
 *   Weights approximate human sensitivity to red, green, blue channels.
 * - Threshold at 0.5: luminance > 0.5 -> light background -> return
 *   `'#000000'`; otherwise -> dark background -> return `'#ffffff'`.
 * - No validation on input format - upstream contract (Vehicle.routeColor
 *   with leading `#`) guarantees the shape. If the input is malformed
 *   the Number.parseInt will yield NaN and the fallback branch returns
 *   `'#000000'`, which is a safe default (black on white surfaces).
 *
 * Not covered here:
 * - RGBA / short-hand hex (`#0CD`) - backend only emits 7-char form
 * - Named CSS colours - not used anywhere in the app
 * - Unit tests - deferred to SP-21 with the rest of the frontend suite
 */
export function pickReadableTextColor(
  backgroundHex: string,
): '#000000' | '#ffffff' {
  const rgb = parseHexToRgb(backgroundHex);
  const lightBackgroundTxt = '#000000';
  const darkBackgroundTxt = '#ffffff';
  if (!rgb) return lightBackgroundTxt;

  return (0.299 * rgb[0] + 0.587 * rgb[1] + 0.114 * rgb[2]) / 255 > 0.5
    ? lightBackgroundTxt
    : darkBackgroundTxt;
}

/**
 * Returns a number array of length 3 - [R, G, B] for a hex color code like #123 or #112233
 */
function parseHexToRgb(backgroundHex: string): [number, number, number] | null {
  const regex = /^#([A-Fa-f0-9]{3}|[A-Fa-f0-9]{6})$/;
  if (!regex.test(backgroundHex)) {
    return null;
  }

  // Remove the leading #
  let value = backgroundHex.slice(1);

  // Expand shorthand (#123 → #112233)
  if (value.length === 3) {
    value = value
      .split('')
      .map((c) => c + c)
      .join('');
  }

  // Now value is always 6 chars
  const r = parseInt(value.substring(0, 2), 16);
  const g = parseInt(value.substring(2, 4), 16);
  const b = parseInt(value.substring(4, 6), 16);

  return [r, g, b];
}
