import type { ApiProblem } from '../../api/client';

/**
 * What a form's `<Problem>` should say. Field errors whose key matches a field on the form are shown next to that
 * field; any other field error (a key the form has no field for) would otherwise be lost, so its messages are added
 * to the problem message instead.
 */
export function formProblem(problem: ApiProblem | null | undefined, isField: (key: string) => boolean): ApiProblem | null {
  if (!problem) return null;
  const entries = Object.entries(problem.fieldErrors);
  if (entries.length === 0) return problem;
  const unmatched = entries.filter(([key]) => !isField(key)).flatMap(([, messages]) => messages);
  if (unmatched.length === 0) return problem;
  const someMatched = unmatched.length < entries.flatMap(([, m]) => m).length;
  return { ...problem, message: [someMatched ? 'Check the highlighted fields.' : null, ...unmatched].filter(Boolean).join(' ') };
}

/** A matcher for a fixed set of field names (case-insensitive, as `fieldErrors` matches them). */
export function fields(...names: string[]): (key: string) => boolean {
  const set = new Set(names.map((n) => n.toLowerCase()));
  return (key) => set.has(key.toLowerCase());
}

const TEE_FIELDS = ['id', 'name', 'gender', 'effectiveFrom', 'rating', 'slope', 'frontRating', 'frontSlope', 'backRating', 'backSlope', 'holes'];
const HOLE_FIELDS = ['par', 'yardage', 'strokeIndex'];

/** Keys shown by TeeFields (and the tee id summary) under `prefix`, e.g. `tees[0].` or ''. */
export function isTeeField(key: string, prefix: string): boolean {
  const k = key.toLowerCase();
  const p = prefix.toLowerCase();
  if (!k.startsWith(p)) return false;
  const rest = key.slice(prefix.length);
  if (TEE_FIELDS.some((f) => f.toLowerCase() === rest.toLowerCase())) return true;
  const hole = /^holes\[(\d+)\]\.(\w+)$/i.exec(rest);
  return !!hole && Number(hole[1]) < 18 && HOLE_FIELDS.some((f) => f.toLowerCase() === hole[2]!.toLowerCase());
}
