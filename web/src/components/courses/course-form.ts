/**
 * Reading the course forms (manual course entry, course details, add a tee, new tee version) into API requests.
 *
 * Every form keeps what the golfer typed as strings (so a failed post re-renders it unchanged), and field names
 * match the API's validation keys (`name`, `tees[0].rating`, `tees[0].holes[3].par`), so a 400 lands next to the
 * right input. Hole rows are fixed at 18 per tee; a row left completely blank is not sent (a 9-hole tee fills
 * nine rows). Because blank rows are dropped, the API's hole indexes are mapped back to form rows with
 * `remapHoleErrors`.
 */
import type { ApiProblem, Schemas } from '../../api/client';

export const HOLE_ROWS = 18;
export const GENDERS = ['Unspecified', 'Male', 'Female'] as const;

/** The first version of a tee created without a date applies from the start (the API's default). */
export const DEFAULT_EFFECTIVE_FROM = '1900-01-01';

export type Errors = Record<string, string[]>;

export interface HoleValues {
  par: string;
  yardage: string;
  strokeIndex: string;
}

export interface TeeValues {
  id: string;
  name: string;
  gender: string;
  effectiveFrom: string;
  rating: string;
  slope: string;
  frontRating: string;
  frontSlope: string;
  backRating: string;
  backSlope: string;
  holes: HoleValues[];
}

export interface DetailsValues {
  name: string;
  clubName: string;
  address: string;
  city: string;
  region: string;
  country: string;
  phone: string;
  website: string;
  latitude: string;
  longitude: string;
}

export const DETAIL_FIELDS = ['name', 'clubName', 'address', 'city', 'region', 'country', 'phone', 'website', 'latitude', 'longitude'] as const;

type FormLike = Pick<FormData, 'get'>;

function text(form: FormLike, name: string): string {
  const value = form.get(name);
  return typeof value === 'string' ? value : '';
}

const str = (value: unknown): string => (value === null || value === undefined ? '' : String(value));

function blankHoles(): HoleValues[] {
  return Array.from({ length: HOLE_ROWS }, () => ({ par: '', yardage: '', strokeIndex: '' }));
}

/** A new, empty tee. With `copyFrom`, par and stroke index are copied (they rarely differ between tees). */
export function emptyTee(id: string, copyFrom?: TeeValues): TeeValues {
  return {
    id,
    name: '',
    gender: 'Unspecified',
    effectiveFrom: '',
    rating: '',
    slope: '',
    frontRating: '',
    frontSlope: '',
    backRating: '',
    backSlope: '',
    holes: copyFrom ? copyFrom.holes.map((h) => ({ par: h.par, yardage: '', strokeIndex: h.strokeIndex })) : blankHoles(),
  };
}

export function emptyDetails(): DetailsValues {
  return { name: '', clubName: '', address: '', city: '', region: '', country: '', phone: '', website: '', latitude: '', longitude: '' };
}

/** Form values for a course's current details (the edit form). */
export function detailsFromCourse(course: Schemas['CourseDetailDto']): DetailsValues {
  return {
    name: course.name,
    clubName: str(course.clubName),
    address: str(course.address),
    city: str(course.city),
    region: str(course.region),
    country: str(course.country),
    phone: str(course.phone),
    website: str(course.website),
    latitude: str(course.latitude),
    longitude: str(course.longitude),
  };
}

/** Form values for a new version of a tee, starting from one of its versions' numbers (no date). */
export function teeFromVersion(tee: Schemas['TeeDto'], version: Schemas['TeeVersionDto'] | undefined): TeeValues {
  const holes = blankHoles();
  for (const h of version?.holes ?? []) {
    const n = Number(h.number);
    if (Number.isInteger(n) && n >= 1 && n <= HOLE_ROWS) holes[n - 1] = { par: str(h.par), yardage: str(h.yardage), strokeIndex: str(h.strokeIndex) };
  }
  return {
    id: tee.id,
    name: tee.name,
    gender: tee.gender,
    effectiveFrom: '',
    rating: str(version?.rating),
    slope: str(version?.slope),
    frontRating: str(version?.frontRating),
    frontSlope: str(version?.frontSlope),
    backRating: str(version?.backRating),
    backSlope: str(version?.backSlope),
    holes,
  };
}

export function readDetails(form: FormLike): DetailsValues {
  const values = emptyDetails();
  for (const key of DETAIL_FIELDS) values[key] = text(form, key);
  return values;
}

/** A tee's values from a post; `prefix` is `tees[0].` on the manual course form and '' on single-tee forms. */
export function readTee(form: FormLike, prefix: string): TeeValues {
  const f = (name: string) => text(form, prefix + name);
  return {
    id: f('id'),
    name: f('name'),
    gender: f('gender') || 'Unspecified',
    effectiveFrom: f('effectiveFrom'),
    rating: f('rating'),
    slope: f('slope'),
    frontRating: f('frontRating'),
    frontSlope: f('frontSlope'),
    backRating: f('backRating'),
    backSlope: f('backSlope'),
    holes: Array.from({ length: HOLE_ROWS }, (_, i) => ({
      par: f(`holes[${i}].par`),
      yardage: f(`holes[${i}].yardage`),
      strokeIndex: f(`holes[${i}].strokeIndex`),
    })),
  };
}

function add(errors: Errors, key: string, message: string): void {
  (errors[key] ??= []).push(message);
}

/** A decimal from a text field: null when blank; an error (and null) when it isn't a number. */
function decimal(value: string, key: string, errors: Errors): number | null {
  const t = value.trim();
  if (t === '') return null;
  const n = Number(t);
  if (!Number.isFinite(n) || !/^[+-]?(\d+\.?\d*|\.\d+)$/.test(t)) {
    add(errors, key, 'Enter a number.');
    return null;
  }
  return n;
}

function integer(value: string, key: string, errors: Errors): number | null {
  const t = value.trim();
  if (t === '') return null;
  if (!/^[+-]?\d+$/.test(t)) {
    add(errors, key, 'Enter a whole number.');
    return null;
  }
  return Number(t);
}

const orNull = (value: string): string | null => (value.trim() === '' ? null : value.trim());

export function buildDetails(values: DetailsValues): { request: Schemas['CourseDetailsRequest']; errors: Errors } {
  const errors: Errors = {};
  if (values.name.trim() === '') add(errors, 'name', 'Name is required.');
  const latitude = decimal(values.latitude, 'latitude', errors);
  const longitude = decimal(values.longitude, 'longitude', errors);
  return {
    request: {
      name: orNull(values.name),
      clubName: orNull(values.clubName),
      address: orNull(values.address),
      city: orNull(values.city),
      region: orNull(values.region),
      country: orNull(values.country),
      phone: orNull(values.phone),
      website: orNull(values.website),
      latitude,
      longitude,
    },
    errors,
  };
}

export interface BuiltTee {
  request: Schemas['CreateTeeRequest'];
  errors: Errors;
  /** `holeRows[j]` is the form row of the request's `holes[j]`. */
  holeRows: number[];
}

/**
 * The API request for a tee (or a tee version: same numbers, plus `effectiveFrom`). Checks only what the API can't be
 * sent (text that isn't a number, a half-filled hole row, no holes at all); the API validates the rest.
 */
export function buildTee(values: TeeValues, prefix: string, options: { requireName?: boolean; requireEffectiveFrom?: boolean } = {}): BuiltTee {
  const errors: Errors = {};
  if (options.requireName && values.name.trim() === '') add(errors, `${prefix}name`, 'A tee name is required.');
  const effectiveFrom = values.effectiveFrom.trim();
  if (options.requireEffectiveFrom && effectiveFrom === '') add(errors, `${prefix}effectiveFrom`, 'The date the change took effect is required.');
  else if (effectiveFrom !== '' && !/^\d{4}-\d{2}-\d{2}$/.test(effectiveFrom)) add(errors, `${prefix}effectiveFrom`, 'Enter a date.');

  const rating = decimal(values.rating, `${prefix}rating`, errors);
  const slope = integer(values.slope, `${prefix}slope`, errors);
  if (values.rating.trim() === '') add(errors, `${prefix}rating`, 'Rating is required.');
  if (values.slope.trim() === '') add(errors, `${prefix}slope`, 'Slope is required.');
  const frontRating = decimal(values.frontRating, `${prefix}frontRating`, errors);
  const frontSlope = integer(values.frontSlope, `${prefix}frontSlope`, errors);
  const backRating = decimal(values.backRating, `${prefix}backRating`, errors);
  const backSlope = integer(values.backSlope, `${prefix}backSlope`, errors);

  const holes: Schemas['TeeHoleInput'][] = [];
  const holeRows: number[] = [];
  values.holes.forEach((hole, i) => {
    const cells = [hole.par, hole.yardage, hole.strokeIndex];
    if (cells.every((c) => c.trim() === '')) return;
    const key = `${prefix}holes[${i}].`;
    const read = (name: keyof HoleValues, label: string) => {
      if (hole[name].trim() === '') {
        add(errors, key + name, `${label} is required.`);
        return null;
      }
      return integer(hole[name], key + name, errors);
    };
    const par = read('par', 'Par');
    const yardage = read('yardage', 'Yardage');
    const strokeIndex = read('strokeIndex', 'Stroke index');
    if (par !== null && yardage !== null && strokeIndex !== null) {
      holes.push({ number: i + 1, par, yardage, strokeIndex });
      holeRows.push(i);
    }
  });
  if (holeRows.length === 0 && !Object.keys(errors).some((k) => k.startsWith(`${prefix}holes[`))) {
    add(errors, `${prefix}holes`, 'Enter par, yardage and stroke index for at least one hole.');
  }

  const gender = (GENDERS as readonly string[]).includes(values.gender) ? (values.gender as Schemas['Gender']) : 'Unspecified';
  return {
    request: {
      id: values.id.trim() === '' ? null : values.id.trim(),
      name: orNull(values.name),
      gender,
      effectiveFrom: effectiveFrom === '' ? null : effectiveFrom,
      rating,
      slope,
      frontRating,
      frontSlope,
      backRating,
      backSlope,
      holes,
    },
    errors,
    holeRows,
  };
}

/** The tee version part of a built tee (body of `POST .../versions`). */
export function versionRequest(tee: Schemas['CreateTeeRequest']): Schemas['TeeVersionInput'] {
  const { effectiveFrom, rating, slope, frontRating, frontSlope, backRating, backSlope, holes } = tee;
  return { effectiveFrom, rating, slope, frontRating, frontSlope, backRating, backSlope, holes };
}

/** Maps the API's `{prefix}holes[j].x` keys (index into the holes sent) back to the form's row indexes. */
export function remapHoleErrors(fieldErrors: Errors, prefix: string, holeRows: number[]): Errors {
  const out: Errors = {};
  const escaped = prefix.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const pattern = new RegExp(`^(${escaped}holes)\\[(\\d+)\\](.*)$`, 'i');
  for (const [key, messages] of Object.entries(fieldErrors)) {
    const match = pattern.exec(key);
    const row = match ? holeRows[Number(match[2])] : undefined;
    const mapped = match && row !== undefined ? `${match[1]}[${row}]${match[3]}` : key;
    out[mapped] = [...(out[mapped] ?? []), ...messages];
  }
  return out;
}

/** Every validation message under a key prefix (for a tee's summary, e.g. errors on hidden fields). */
export function errorsUnder(problem: ApiProblem | null | undefined, prefix: string): string[] {
  if (!problem) return [];
  const p = prefix.toLowerCase();
  return Object.entries(problem.fieldErrors)
    .filter(([key]) => key.toLowerCase().startsWith(p))
    .flatMap(([, messages]) => messages);
}

/** How many tees a manual course form posted (`teeCount`, clamped to 1..12). */
export function postedTeeCount(form: FormLike): number {
  const n = Number(text(form, 'teeCount'));
  return Number.isInteger(n) ? Math.min(Math.max(n, 1), MAX_TEES) : 1;
}

export const MAX_TEES = 12;
