/**
 * Largest request body the site accepts (the pages only take small HTML form posts). Used by the Node adapter's
 * `bodySizeLimit` (astro.config.mjs) and by the middleware, which answers 413 up front when Content-Length says more.
 */
export const MAX_BODY_BYTES = 1024 * 1024;

/** True when the request declares a body larger than the limit. Chunked bodies are cut off by the adapter instead. */
export function declaresOversizedBody(request: Request, limit = MAX_BODY_BYTES): boolean {
  const length = request.headers.get('content-length');
  if (length === null) return false;
  const n = Number(length);
  return !Number.isFinite(n) || n > limit;
}
