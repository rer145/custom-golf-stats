import { readdirSync, statSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

/** Sample values for dynamic route segments, so every page file maps to a concrete URL. */
export const SAMPLE_PARAMS: Record<string, string> = {
  year: '2025',
  id: '11111111-1111-4111-8111-111111111111',
  teeId: '22222222-2222-4222-8222-222222222222',
  versionId: '33333333-3333-4333-8333-333333333333',
};

export const PAGES_DIR = fileURLToPath(new URL('../../src/pages/', import.meta.url));

const PAGE_EXTENSIONS = ['.astro', '.ts', '.js', '.md', '.mdx', '.html'];

/** Every routable file under `src/pages` (test files and `_`-prefixed files excluded), relative, with forward slashes. */
export function listPageFiles(dir: string = PAGES_DIR): string[] {
  const out: string[] = [];
  for (const name of readdirSync(dir)) {
    if (name.startsWith('_') || name.startsWith('.')) continue;
    const full = join(dir, name);
    if (statSync(full).isDirectory()) {
      out.push(...listPageFiles(full));
    } else if (PAGE_EXTENSIONS.some((ext) => name.endsWith(ext)) && !/\.(test|spec)\.[jt]s$/.test(name) && !name.endsWith('.d.ts')) {
      out.push(relative(PAGES_DIR, full).split(sep).join('/'));
    }
  }
  return out.sort();
}

/** `stats/[year]/index.astro` → `/stats/2025`; `index.astro` → `/`; `sign-out.ts` → `/sign-out`. */
export function pageFileToPath(file: string): string {
  const withoutExt = file.replace(/\.[^.\/]+$/, '');
  const segments = withoutExt.split('/').filter((s) => s !== 'index');
  const resolved = segments.map((segment) =>
    segment.replace(/\[(?:\.\.\.)?([A-Za-z0-9_]+)\]/g, (_, name: string) => SAMPLE_PARAMS[name] ?? 'sample'),
  );
  return '/' + resolved.join('/');
}

export function listPagePaths(): { file: string; path: string }[] {
  return listPageFiles().map((file) => ({ file, path: pageFileToPath(file) }));
}
