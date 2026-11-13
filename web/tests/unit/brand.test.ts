import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const web = (p: string) => fileURLToPath(new URL(`../../${p}`, import.meta.url));
const brand = (p: string) => fileURLToPath(new URL(`../../../brand/${p}`, import.meta.url));
const same = (a: string, b: string) => expect(readFileSync(a).equals(readFileSync(b)), `${a} differs from ${b}`).toBe(true);

// The brand kit is the source of truth; the web copies must not drift from it.
describe('brand assets', () => {
  it('tokens.css is a verbatim copy', () => {
    same(web('src/styles/tokens.css'), brand('tokens/tokens.css'));
  });

  it('favicons, manifest and OG image are copied from brand/web', () => {
    const files = readdirSync(brand('web')).filter((f) => f !== 'head-snippet.html');
    expect(files.length).toBeGreaterThan(5);
    for (const f of files) same(web(`public/${f}`), brand(`web/${f}`));
  });

  it('fonts and their licences are copied from brand/fonts', () => {
    for (const f of readdirSync(brand('fonts')).filter((f) => f.endsWith('.ttf'))) same(web(`public/fonts/${f}`), brand(`fonts/${f}`));
    for (const f of readdirSync(brand('fonts/licenses'))) same(web(`public/fonts/licenses/${f}`), brand(`fonts/licenses/${f}`));
  });

  it('logos are copied from brand/logo/svg', () => {
    for (const f of readdirSync(web('public/logo'))) same(web(`public/logo/${f}`), brand(`logo/svg/${f}`));
  });

  it('styles and components use no colour literals outside tokens.css', () => {
    const colour = /#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(|color-mix\(/;
    expect(readFileSync(web('src/styles/global.css'), 'utf8')).not.toMatch(colour);
    const astroFiles = readdirSync(web('src'), { recursive: true, encoding: 'utf8' }).filter((f) => f.endsWith('.astro'));
    expect(astroFiles.length).toBeGreaterThan(5);
    for (const f of astroFiles) {
      // BaseHead carries the brand head snippet's theme-color meta, the one sanctioned literal.
      const text = readFileSync(web(`src/${f}`), 'utf8').replace('<meta name="theme-color" content="#0E2A21" />', '');
      expect(text, f).not.toMatch(colour);
    }
  });
});
