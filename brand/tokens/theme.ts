// Custom Golf Stats theme for React Native — generated from tokens.json
// Load fonts first (see BRAND.md). Font family names below match the .ttf file names in /fonts.

export const brand = {
  ink: '#0E2A21',
  fairway: '#1B5E3F',
  orange: '#E2662B',
  orangeDeep: '#B84C17',
  rough: '#4A5A52',
  chalk: '#F5F6F2',
  line: '#D9DED6',
  mist: '#B9C7BF',
  white: '#FFFFFF',
  cup: '#081C15',
} as const;

export const colors = {
  light: {
    background: '#F5F6F2',
    surface: '#FFFFFF',
    surfaceInverse: '#0E2A21',
    text: '#0E2A21',
    textSecondary: '#4A5A52',
    textInverse: '#F5F6F2',
    textInverseSecondary: '#B9C7BF',
    border: '#D9DED6',
    primary: '#1B5E3F',
    onPrimary: '#FFFFFF',
    accent: '#E2662B',
    onAccent: '#081C15',
    accentText: '#B84C17',
    positive: '#1B5E3F',
    negative: '#B84C17',
    chipBg: '#DCE8E1',
  },
  dark: {
    background: '#0E2A21',
    surface: '#163A2E',
    surfaceInverse: '#F5F6F2',
    text: '#F5F6F2',
    textSecondary: '#B9C7BF',
    textInverse: '#0E2A21',
    textInverseSecondary: '#4A5A52',
    border: '#2D4A3F',
    primary: '#F5F6F2',
    onPrimary: '#0E2A21',
    accent: '#E2662B',
    onAccent: '#081C15',
    accentText: '#F0915F',
    positive: '#7FD1A4',
    negative: '#F0915F',
    chipBg: '#24483B',
  },
} as const;

export type ColorScheme = keyof typeof colors;
export type ThemeColors = typeof colors.light;

export const fonts = {
  displayMedium: 'SpaceGrotesk-Medium',
  displayBold: 'SpaceGrotesk-Bold',
  body: 'Inter-Regular',
  bodyMedium: 'Inter-Medium',
  bodySemiBold: 'Inter-SemiBold',
  monoMedium: 'JetBrainsMono-Medium',
  monoBold: 'JetBrainsMono-Bold',
} as const;

// Native fonts are static files: pick the weight by fontFamily, never by fontWeight (Android ignores it for custom fonts).
// RN letterSpacing is in points, so em values are pre-multiplied by size.
export const type = {
  displayXL: { fontFamily: fonts.displayBold, fontSize: 64, lineHeight: 66, letterSpacing: -1.9 },
  display: { fontFamily: fonts.displayBold, fontSize: 44, lineHeight: 46, letterSpacing: -0.9 },
  title: { fontFamily: fonts.displayBold, fontSize: 22, lineHeight: 26, letterSpacing: -0.2 },
  heading: { fontFamily: fonts.displayBold, fontSize: 18, lineHeight: 22 },
  body: { fontFamily: fonts.body, fontSize: 15, lineHeight: 23 },
  bodyStrong: { fontFamily: fonts.bodySemiBold, fontSize: 15, lineHeight: 23 },
  caption: { fontFamily: fonts.body, fontSize: 13, lineHeight: 18 },
  label: { fontFamily: fonts.monoMedium, fontSize: 12, letterSpacing: 1, textTransform: 'uppercase' as const },
  statXL: { fontFamily: fonts.monoBold, fontSize: 64, lineHeight: 64, fontVariant: ['tabular-nums' as const] },
  statL: { fontFamily: fonts.monoBold, fontSize: 28, lineHeight: 30, fontVariant: ['tabular-nums' as const] },
  statM: { fontFamily: fonts.monoBold, fontSize: 24, lineHeight: 26, fontVariant: ['tabular-nums' as const] },
  statS: { fontFamily: fonts.monoBold, fontSize: 15, lineHeight: 18, fontVariant: ['tabular-nums' as const] },
} as const;

export const space = { 0: 0, 1: 4, 2: 8, 3: 12, 4: 16, 5: 20, 6: 24, 8: 32, 10: 40, 12: 48, 16: 64 } as const;
export const radius = { sm: 10, md: 14, lg: 16, xl: 20, pill: 999 } as const;
export const size = { touchTarget: 44, buttonHeight: 44, buttonHeightLarge: 52, inputHeight: 44, tabBarItem: 48 } as const;

/** Format a strokes-gained value: always signed, one decimal, real minus sign. */
export function formatSG(v: number): string {
  const s = Math.abs(v).toFixed(1);
  return v > 0 ? `+${s}` : v < 0 ? `\u2212${s}` : '0.0';
}

/** Colour for a signed stat; the sign must be shown too (never rely on colour alone). */
export function deltaColor(v: number, scheme: ColorScheme = 'light'): string {
  const c = colors[scheme];
  return v > 0 ? c.positive : v < 0 ? c.negative : c.textSecondary;
}
