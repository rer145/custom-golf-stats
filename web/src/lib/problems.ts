import type { ApiProblem } from '../api/client';

/** A 400 raised by the page itself (unparsable numbers, missing fields), shaped like the API's so Field shows it. Null when there are no errors. */
export function localProblem(errors: Record<string, string[]>): ApiProblem | null {
  if (Object.keys(errors).length === 0) return null;
  return { status: 400, message: 'Check the highlighted fields.', fieldErrors: errors };
}

/**
 * The status to answer a page request with when an API call failed: 400, 403, 404, 409 and 503 (API unreachable)
 * are passed through, any other 5xx is a 502, and anything else a 400.
 */
export function pageStatus(apiStatus: number): number {
  if (apiStatus === 400 || apiStatus === 403 || apiStatus === 404 || apiStatus === 409 || apiStatus === 503) return apiStatus;
  return apiStatus >= 500 ? 502 : 400;
}
