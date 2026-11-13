# Custom Golf Stats — Brand & Integration Guide

This package is the source of truth for the Custom Golf Stats look on the **website**, the **iOS app** and the **Android app**. Coding agents: read this file first, use the tokens in `/tokens` instead of hard-coded values, and copy assets from the platform folders exactly as described below.

The product: a golf shot tracker built around strokes gained, where users can also **define their own custom stats**. The brand should feel precise and analytical, not country-club.

---

## 1. Package map

| Folder | What's in it | Used by |
|---|---|---|
| `logo/svg/` | Master vector logos: mark (4 colour variants), small-size mark, horizontal and stacked lockups. Wordmark text is outlined (no font needed). | All |
| `logo/png/` | Raster exports of the above at 256/512/1024 (mark) and @2x/@4x (lockups). | Docs, email, decks |
| `tokens/tokens.json` | Canonical design tokens (colour, type, space, radius, sizes). | All |
| `tokens/tokens.css` | CSS custom properties with light + dark themes. | Website |
| `tokens/tailwind.preset.js` | Tailwind preset (if the site uses Tailwind). | Website |
| `tokens/theme.ts` | React Native theme: colours (light/dark), text styles, spacing, radius, `formatSG()`, `deltaColor()`. | iOS + Android |
| `fonts/` | Static TTFs: Space Grotesk Medium/Bold, Inter Regular/Medium/SemiBold, JetBrains Mono Medium/Bold, plus OFL licences. | Mobile apps |
| `web/` | Favicons, Apple touch icon, PWA icons, `site.webmanifest`, Open Graph image, `head-snippet.html`. | Website |
| `ios/` | `AppIcon.appiconset` (light, dark, tinted), `SplashLogo.imageset`, brand colour sets, App Store 1024 icon. | iOS (bare RN / native) |
| `android/res/` | Legacy + round launcher PNGs for all densities, adaptive icon (vector foreground, colour background, monochrome for themed icons), splash vector, `brand_colors.xml`. | Android (bare RN / native) |
| `android/` (root) | Play Store icon 512 and feature graphic 1024×500. | Play Console |
| `expo/` | `icon.png`, adaptive foreground + monochrome, `splash-icon.png`, `favicon.png`. | Only if the app uses Expo |

---

## 2. Logo

**The mark ("Ball Chart")** is a golf-ball ring containing three rising bars. The **tallest bar is on the right and is always Pin Orange** (#E2662B); the other two bars match the ring.

| Variant | File | Use on |
|---|---|---|
| Colour | `mark-color.svg` | Light backgrounds (Chalk, white) |
| Reversed | `mark-reversed.svg` | Fairway Ink or Fairway green backgrounds |
| Mono ink / mono white | `mark-mono-*.svg` | One-colour print, embossing, system tint contexts |
| Small sizes | `mark-small-sizes.svg` | Anything rendered under ~24 px (heavier ring and bars) |

**Wordmark:** "custom golf stats", always lowercase, Space Grotesk Bold, −0.02em tracking. Use the outlined lockup files rather than typesetting it live.

**Lockups:** `lockup-horizontal-*` for headers and nav bars; `lockup-stacked-*` for splash screens and square spaces.

**Rules**
- Minimum mark size 16 px (use the small-size variant below 24 px). Minimum horizontal lockup height 24 px.
- Clear space around the mark is at least 25% of its height on every side.
- Do not recolour the bars, reorder them, rotate the mark, add shadows/gradients, or put the colour mark on a mid-tone background.

---

## 3. Colour

| Token | Hex | Role |
|---|---|---|
| Fairway Ink | `#0E2A21` | Primary text; dark surfaces; app icon background |
| Fairway | `#1B5E3F` | Primary brand colour; primary buttons; positive deltas (light theme) |
| Pin Orange | `#E2662B` | Single accent: "add" actions, highlight bar, hero metrics on dark |
| Pin Deep | `#B84C17` | Orange **text** at normal sizes on light; negative deltas (light theme) |
| Rough | `#4A5A52` | Secondary text |
| Chalk | `#F5F6F2` | App/page background (light) |
| Line | `#D9DED6` | Borders, dividers |
| Mist | `#B9C7BF` | Secondary text on dark |
| Cup | `#081C15` | Text **on** Pin Orange buttons |

Full semantic light/dark palettes are in `tokens.json` → `color.light` / `color.dark`. Build UI against the semantic names (`text`, `surface`, `primary`, `accent`, `positive`, `negative`…), not brand hexes.

**Accessibility (measured WCAG contrast):**
- Pin Orange on white is 3.4:1 → **never** use it for body/label text on light. Use Pin Deep (5.1:1 on white) instead.
- Orange buttons use **Cup** text (5.2:1). Fairway Ink on orange is 4.497:1, which narrowly fails AA, so don't use it.
- On dark surfaces, orange text at normal size uses `dark.accentText` (#F0915F). Pin Orange itself is fine for big stat numerals (≥24 px bold).
- Positive/negative stats differ in lightness as well as hue. **Always show the sign** (`+1.1`, `−0.4`, using a real minus U+2212). Use `formatSG()` in `theme.ts`.
- Use only one accent per screen region. Orange means "action" or "the number that matters". Don't use it for decoration.

---

## 4. Typography

| Role | Font | Weight | Notes |
|---|---|---|---|
| Display / headings / wordmark | Space Grotesk | 700 (500 for de-emphasised words) | −0.02em; −0.03em at 44 px+ |
| Body / UI | Inter | 400, 500, 600 | 15 px base in apps, 15–19 px on web |
| Stats / numerals / labels | JetBrains Mono | 500, 700 | Tabular figures so columns align; labels are 12 px uppercase with 0.08em tracking |

Text styles (`displayXL`, `display`, `title`, `heading`, `body`, `bodyStrong`, `caption`, `label`, `statXL`…`statS`) are defined in `tokens.json` → `type` and in `theme.ts` → `type`.

- **Web:** load from Google Fonts (link in `web/head-snippet.html`), or self-host from `/fonts`.
- **React Native:** bundle the static TTFs from `/fonts`. Select weight by **`fontFamily`** (e.g. `SpaceGrotesk-Bold`) and do not set `fontWeight` on custom fonts, because Android won't synthesize it correctly.

---

## 5. Layout & components

- **Spacing:** 4 px base scale (`space` tokens). Screen padding is 20 px on mobile and 24 px on web; card padding is 16–28 px.
- **Radius:** inputs and small buttons `sm` 10, stat tiles `md` 14, cards `lg` 16 / `xl` 20, chips `pill`.
- **Touch targets:** at least 44×44 px everywhere. Buttons are 44 px tall (52 px for hero CTAs). Tab bar items are 48 px.
- **Cards:** white surface with 1 px `border` on Chalk. On dark, use `surface` with no border. No drop shadows, gradients or left-border accent strips.
- **Buttons:**
  - Primary: Fairway background, white text.
  - Accent: Pin Orange background, Cup text. Reserve this for "add" actions, such as **+ Add stat**.
  - Secondary: transparent background with 1 px border.
- **Stat tile:** big value in a mono `statM` style, with an Inter 13 px caption in `textSecondary` below it. The empty "add a stat" slot uses a 2 px dashed `mist` border.
- **Round header:** Fairway Ink band at the top of the screen showing the score (`statXL`) and total strokes gained in Pin Orange.
- **Icons:** stroked line icons (2 px stroke, round caps), coloured `currentColor`. No emoji in UI.
- **Web:** content max width is 1200 px, layouts are fluid, and the nav wraps on phones. Support light and dark themes through `tokens.css`, which also respects `prefers-color-scheme`.
- **Voice:** short, direct, and golfer-literate (SG, GIR, up-and-down). Tagline: *"Track the stats nobody else thought to."*

---

## 6. Platform integration

### Website
1. Copy everything in `web/` (except `head-snippet.html`) to the public/static root.
2. Paste `web/head-snippet.html` into the `<head>`.
3. Import `tokens/tokens.css` globally, or add `tokens/tailwind.preset.js` to the Tailwind `presets`.
4. Use `logo/svg/lockup-horizontal-on-dark.svg` in a dark header, or the `-on-light` version on light pages.

### iOS (bare React Native / native)
1. Replace `ios/<App>/Images.xcassets/AppIcon.appiconset` with `ios/AppIcon.appiconset`. It uses a single 1024 size with light, dark and tinted appearances (Xcode 15+/iOS 18). The light icon is opaque with no alpha channel, as Apple requires.
2. Add `SplashLogo.imageset` and the `Brand*.colorset` folders to the same asset catalog. In `LaunchScreen.storyboard`, set the background to `BrandInk` and centre an image view of `SplashLogo` at 120×120 pt.
3. Add the TTFs from `/fonts` to the Xcode target and list them under `UIAppFonts` in `Info.plist`. With RN CLI you can instead put them in `assets/fonts` and run `npx react-native-asset`.
4. Upload `ios/AppStore-1024.png` in App Store Connect if it's requested separately.

### Android (bare React Native / native)
1. Merge `android/res/*` into `android/app/src/main/res/`, overwriting the existing `mipmap-*` launcher icons. API 26+ uses the adaptive icon: a vector foreground on an Ink background, plus a `monochrome` layer for Android 13 themed icons. Older devices fall back to the PNGs.
2. For the Android 12+ splash, use `Theme.SplashScreen` with `windowSplashScreenBackground = @color/brand_ink` and `windowSplashScreenAnimatedIcon = @drawable/splash_logo`.
3. Copy the TTFs to `android/app/src/main/assets/fonts/`, or let `react-native-asset` do it.
4. In Play Console, use `android/play-store-icon-512.png` and `android/play-feature-graphic-1024x500.png`.

### If the app is Expo
Skip the native folders and use `expo/`:
```json
{
  "expo": {
    "name": "Custom Golf Stats",
    "icon": "./assets/brand/icon.png",
    "backgroundColor": "#0E2A21",
    "splash": { "image": "./assets/brand/splash-icon.png", "resizeMode": "contain", "backgroundColor": "#0E2A21" },
    "android": {
      "adaptiveIcon": {
        "foregroundImage": "./assets/brand/adaptive-icon-foreground.png",
        "monochromeImage": "./assets/brand/adaptive-icon-monochrome.png",
        "backgroundColor": "#0E2A21"
      }
    },
    "web": { "favicon": "./assets/brand/favicon.png" }
  }
}
```
On newer SDKs the splash config moved to the `expo-splash-screen` config plugin, so follow the installed SDK's docs. The images stay the same. Load fonts with `expo-font` (`useFonts({ 'SpaceGrotesk-Bold': require('./assets/fonts/SpaceGrotesk-Bold.ttf'), … })`), using the same names as `theme.ts`.

### Using the theme in React Native
```tsx
import { useColorScheme, Text, View } from 'react-native';
import { colors, type, space, radius, formatSG, deltaColor } from './theme';

export function SGRow({ label, value }: { label: string; value: number }) {
  const scheme = useColorScheme() === 'dark' ? 'dark' : 'light';
  const c = colors[scheme];
  return (
    <View style={{ flexDirection: 'row', justifyContent: 'space-between', paddingVertical: space[2] }}>
      <Text style={[type.body, { color: c.text }]}>{label}</Text>
      <Text style={[type.statS, { color: deltaColor(value, scheme) }]}>{formatSG(value)}</Text>
    </View>
  );
}
```
For the logo inside the app, render `logo/svg/mark-*.svg` with `react-native-svg` (via `react-native-svg-transformer`, or by pasting the SVG into a component).

---

## 7. Licences
Space Grotesk, Inter and JetBrains Mono are under the SIL Open Font License 1.1 (see `fonts/licenses/`). They're free to bundle in apps and on websites. Keep the licence files with the fonts.
