// Tailwind preset for the Custom Golf Stats website. Usage: presets: [require('./tailwind.preset.js')]
/** @type {import('tailwindcss').Config} */
module.exports = {
  theme: {
    extend: {
      colors: {
        ink: '#0E2A21', fairway: '#1B5E3F', pin: { DEFAULT: '#E2662B', deep: '#B84C17' },
        rough: '#4A5A52', chalk: '#F5F6F2', line: '#D9DED6', mist: '#B9C7BF', cup: '#081C15',
      },
      fontFamily: {
        display: ['"Space Grotesk"', 'system-ui', 'sans-serif'],
        sans: ['Inter', 'system-ui', 'sans-serif'],
        mono: ['"JetBrains Mono"', 'ui-monospace', 'monospace'],
      },
      borderRadius: { sm: '10px', md: '14px', lg: '16px', xl: '20px' },
      maxWidth: { content: '1200px' },
      minHeight: { touch: '44px' },
    },
  },
};
