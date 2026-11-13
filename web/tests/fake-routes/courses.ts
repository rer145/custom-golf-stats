// Fake /courses routes (search, provider search and import, manual create, details, tees, versions, flags).
// Validation and status codes mirror src/CustomGolfStats.Api/Features/Courses (CourseEndpoints, CourseRules).
import { randomUUID } from 'node:crypto';
import { ok, problem, requireUser, type FakeRequest, type FakeResponse, type FakeRoute, type FakeState, type Schemas } from '../fake-kit';
import detailsFixture from '../fixtures/courses-details.json' with { type: 'json' };

type Course = Schemas['CourseDetailDto'];
type Tee = Schemas['TeeDto'];
type Version = Schemas['TeeVersionDto'];
type HoleInput = Schemas['TeeHoleInput'];
type Errors = Record<string, string[]>;

// JSON imports widen enum strings to `string`; the fixture is checked against the schema by this cast's shape.
const fixtureCourses = detailsFixture as Course[];

interface Stored {
  course: Course;
  createdBy: string | null;
}

function store(state: FakeState): Map<string, Stored> {
  let courses = state.data.get('courses') as Map<string, Stored> | undefined;
  if (!courses) {
    courses = new Map(fixtureCourses.map((c) => [c.id, { course: structuredClone(c), createdBy: null }]));
    state.data.set('courses', courses);
  }
  return courses;
}

function flags(state: FakeState): Schemas['CourseFlagDto'][] {
  let list = state.data.get('courseFlags') as Schemas['CourseFlagDto'][] | undefined;
  if (!list) {
    list = [];
    state.data.set('courseFlags', list);
  }
  return list;
}

const today = () => new Date().toISOString().slice(0, 10);
const isAdmin = (req: FakeRequest) => requireUser(req).role === 'Admin';
const add = (errors: Errors, key: string, message: string): void => {
  (errors[key] ??= []).push(message);
};
const validation = (errors: Errors) => problem(400, 'One or more validation errors occurred.', undefined, errors);
const num = (v: unknown): number | null => (v === null || v === undefined || v === '' ? null : Number(v));
const clean = (v: unknown): string | null => (typeof v === 'string' && v.trim() !== '' ? v.trim() : null);

function markCurrent(tee: Tee): void {
  const date = today();
  const inEffect = tee.versions.filter((v) => v.effectiveFrom <= date).sort((a, b) => b.effectiveFrom.localeCompare(a.effectiveFrom))[0];
  const current = inEffect ?? [...tee.versions].sort((a, b) => a.effectiveFrom.localeCompare(b.effectiveFrom))[0];
  for (const v of tee.versions) v.isCurrent = v === current;
}

function validateDetails(body: Record<string, unknown>, errors: Errors): void {
  if (!clean(body.name)) add(errors, 'name', 'Name is required.');
  const limits: [string, number][] = [['name', 200], ['clubName', 200], ['address', 300], ['city', 100], ['region', 100], ['country', 100], ['phone', 50], ['website', 300]];
  for (const [key, max] of limits) if (typeof body[key] === 'string' && (body[key] as string).trim().length > max) add(errors, key, `At most ${max} characters.`);
  const lat = num(body.latitude);
  const lon = num(body.longitude);
  if (lat !== null && (lat < -90 || lat > 90)) add(errors, 'latitude', 'Latitude must be between -90 and 90.');
  if (lon !== null && (lon < -180 || lon > 180)) add(errors, 'longitude', 'Longitude must be between -180 and 180.');
  if ((lat === null) !== (lon === null)) add(errors, lat === null ? 'latitude' : 'longitude', 'Give both latitude and longitude, or neither.');
}

function validateVersion(body: Record<string, unknown>, errors: Errors, requireEffectiveFrom: boolean, prefix = ''): void {
  if (requireEffectiveFrom && !body.effectiveFrom) add(errors, `${prefix}effectiveFrom`, 'EffectiveFrom is required.');
  if (body.rating == null) add(errors, `${prefix}rating`, 'Rating is required.');
  for (const key of ['rating', 'frontRating', 'backRating']) {
    const v = num(body[key]);
    if (v !== null && (v < 1 || v > 99.9)) add(errors, prefix + key, 'Rating must be between 1.0 and 99.9.');
  }
  if (body.slope == null) add(errors, `${prefix}slope`, 'Slope is required.');
  for (const key of ['slope', 'frontSlope', 'backSlope']) {
    const v = num(body[key]);
    if (v !== null && (v < 55 || v > 155)) add(errors, prefix + key, 'Slope must be between 55 and 155.');
  }
  const holes = body.holes as HoleInput[] | null | undefined;
  if (!holes || holes.length === 0) {
    add(errors, `${prefix}holes`, 'At least one hole is required.');
    return;
  }
  if (holes.length > 18) {
    add(errors, `${prefix}holes`, 'A tee has at most 18 holes.');
    return;
  }
  const numbers = new Set<number>();
  const indexes = new Set<number>();
  holes.forEach((h, i) => {
    const key = `${prefix}holes[${i}].`;
    const n = Number(h.number);
    const par = Number(h.par);
    const yardage = Number(h.yardage);
    const si = Number(h.strokeIndex);
    if (n < 1 || n > 18) add(errors, `${key}number`, 'Hole number must be between 1 and 18.');
    else if (numbers.has(n)) add(errors, `${key}number`, `Hole number ${n} is used more than once.`);
    numbers.add(n);
    if (par < 3 || par > 6) add(errors, `${key}par`, 'Par must be between 3 and 6.');
    if (yardage < 0 || yardage > 1000) add(errors, `${key}yardage`, 'Yardage must be between 0 and 1000.');
    if (si < 1 || si > holes.length) add(errors, `${key}strokeIndex`, `Stroke index must be between 1 and ${holes.length}.`);
    else if (indexes.has(si)) add(errors, `${key}strokeIndex`, `Stroke index ${si} is used more than once.`);
    indexes.add(si);
  });
}

function validateTee(body: Record<string, unknown>, errors: Errors, prefix: string): void {
  const name = clean(body.name);
  if (!name || name.length > 100) add(errors, `${prefix}name`, 'A tee name of at most 100 characters is required.');
  validateVersion(body, errors, false, prefix);
}

function buildVersion(body: Record<string, unknown>, effectiveFrom: string): Version {
  const holes = ((body.holes as HoleInput[]) ?? [])
    .map((h) => ({ number: Number(h.number), par: Number(h.par), yardage: Number(h.yardage), strokeIndex: Number(h.strokeIndex) }))
    .sort((a, b) => a.number - b.number);
  const full = holes.length === 18;
  const sum = (f: (n: number) => boolean) => holes.filter((h) => f(h.number)).reduce((a, h) => a + h.par, 0);
  return {
    id: randomUUID(),
    effectiveFrom,
    isCurrent: false,
    par: sum(() => true),
    rating: Number(body.rating),
    slope: Number(body.slope),
    frontPar: full ? sum((n) => n <= 9) : null,
    frontRating: num(body.frontRating),
    frontSlope: num(body.frontSlope),
    backPar: full ? sum((n) => n >= 10) : null,
    backRating: num(body.backRating),
    backSlope: num(body.backSlope),
    source: 'Manual',
    changedAt: new Date().toISOString(),
    holes,
  };
}

function buildTee(body: Record<string, unknown>): Tee {
  const gender = ['Male', 'Female', 'Unspecified'].includes(String(body.gender)) ? (body.gender as Schemas['Gender']) : 'Unspecified';
  const tee: Tee = {
    id: typeof body.id === 'string' ? body.id : randomUUID(),
    name: clean(body.name) ?? '',
    gender,
    versions: [buildVersion(body, typeof body.effectiveFrom === 'string' ? body.effectiveFrom : '1900-01-01')],
  };
  markCurrent(tee);
  return tee;
}

function propose(req: FakeRequest, courseId: string, teeId: string | null, what: string): FakeResponse {
  const flag: Schemas['CourseFlagDto'] = {
    id: randomUUID(),
    courseId,
    teeId,
    holeNumber: null,
    note: `Proposed change to ${what}: ${JSON.stringify(req.body)}`,
    status: 'Open',
    createdAt: new Date().toISOString(),
  };
  flags(req.state).push(flag);
  return { status: 202, body: flag };
}

const summary = (c: Course, distanceMiles: number | null = null): Schemas['CourseSummaryDto'] => ({
  id: c.id,
  name: c.name,
  clubName: c.clubName,
  city: c.city,
  region: c.region,
  country: c.country,
  latitude: c.latitude,
  longitude: c.longitude,
  status: c.status,
  distanceMiles,
});

/** Provider courses: two fixed ones (already imported), plus one made up from any query (so tests import fresh courses). */
function providerResults(state: FakeState, query: string): Schemas['ProviderSearchResultDto'][] {
  const imported = new Map([...store(state).values()].filter((s) => s.course.providerCourseId).map((s) => [s.course.providerCourseId!, s.course.id]));
  const slug = query.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
  const title = query.trim().replace(/\b\w/g, (c) => c.toUpperCase());
  const catalog = [
    { providerCourseId: 'gca-1001', clubName: 'Cedar Ridge Golf Club', courseName: 'Cedar Ridge', city: 'Lakeview', region: 'OH', country: 'United States' },
    { providerCourseId: 'gca-1002', clubName: 'Heron Bay Links', courseName: 'Heron Bay Links', city: 'Port Clement', region: 'ME', country: 'United States' },
  ].filter((c) => `${c.clubName} ${c.courseName} ${c.city}`.toLowerCase().includes(query.toLowerCase()));
  const made = slug ? [{ providerCourseId: `gca-q-${slug}`, clubName: `${title} Golf Club`, courseName: `${title} Championship`, city: 'Fairview', region: 'CA', country: 'United States' }] : [];
  return [...catalog, ...made].map((c) => ({ ...c, alreadyImported: imported.has(c.providerCourseId), courseId: imported.get(c.providerCourseId) ?? null }));
}

function findImported(state: FakeState, providerCourseId: string): Course | undefined {
  return [...store(state).values()].find((s) => s.course.providerCourseId === providerCourseId)?.course;
}

const body = (req: FakeRequest) => ((req.body ?? {}) as Record<string, unknown>);

export const routes: FakeRoute[] = [
  {
    method: 'GET',
    path: '/courses',
    handler: ({ query, state }) => {
      const text = (query.get('query') ?? '').trim().toLowerCase();
      if (!text && !query.get('near')) return validation({ query: ['Give a query, near=latitude,longitude, or both.'] });
      const results = [...store(state).values()]
        .map((s) => s.course)
        .filter((c) => !c.mergedIntoCourseId && [c.name, c.clubName, c.city].some((v) => v?.toLowerCase().includes(text)))
        .sort((a, b) => a.name.localeCompare(b.name))
        .slice(0, 50)
        .map((c) => summary(c));
      return ok(results);
    },
  },
  {
    method: 'GET',
    path: '/courses/provider-search',
    handler: ({ query, state }) => {
      const text = (query.get('query') ?? '').trim();
      if (!text) return validation({ query: ['A query is required.'] });
      if (text.toLowerCase().includes('unavailable')) return problem(502, 'Course provider unavailable', 'The course provider could not be reached. Try again later.');
      return ok(providerResults(state, text));
    },
  },
  {
    method: 'POST',
    path: '/courses/import',
    handler: (req) => {
      const providerCourseId = clean(body(req).providerCourseId);
      if (!providerCourseId) return validation({ providerCourseId: ['A provider course id of at most 100 characters is required.'] });
      const existing = findImported(req.state, providerCourseId);
      if (existing) return ok(existing);
      // A query containing "notees" searches normally but its course has no usable tee (CourseEndpoints: 502, nothing saved).
      if (providerCourseId.startsWith('gca-q-') && providerCourseId.includes('notees')) {
        return problem(502, 'Provider course has no usable tees', 'The course provider returned no tee with 1 to 18 holes for this course, so nothing was imported.');
      }
      if (!providerCourseId.startsWith('gca-q-')) return problem(404, 'Not Found');
      const result = providerResults(req.state, providerCourseId.slice('gca-q-'.length).replace(/-/g, ' ')).find((r) => r.providerCourseId === providerCourseId)!;
      const template = fixtureCourses[0]!;
      const course: Course = {
        ...structuredClone(template),
        id: randomUUID(),
        name: result.courseName,
        clubName: result.clubName,
        city: result.city,
        region: result.region,
        country: result.country,
        status: 'Imported',
        source: 'GolfCourseApi',
        providerCourseId,
        tees: structuredClone(template.tees).map((t) => ({ ...t, id: randomUUID(), versions: t.versions.map((v) => ({ ...v, id: randomUUID() })) })),
      };
      store(req.state).set(course.id, { course, createdBy: requireUser(req).email });
      return { status: 201, body: course };
    },
  },
  {
    method: 'POST',
    path: '/courses',
    handler: (req) => {
      const b = body(req);
      const errors: Errors = {};
      validateDetails(b, errors);
      const tees = (b.tees as Record<string, unknown>[] | null | undefined) ?? [];
      if (tees.length === 0) add(errors, 'tees', 'At least one tee is required.');
      tees.forEach((t, i) => validateTee(t ?? {}, errors, `tees[${i}].`));
      if (Object.keys(errors).length > 0) return validation(errors);
      const user = requireUser(req);
      const courses = store(req.state);
      const id = typeof b.id === 'string' ? b.id : randomUUID();
      const existing = courses.get(id);
      if (existing) {
        return existing.createdBy === user.email && existing.course.name === clean(b.name)
          ? ok(existing.course)
          : problem(409, 'Id already in use', 'A course with this id already exists.');
      }
      const course: Course = {
        id,
        name: clean(b.name)!,
        clubName: clean(b.clubName),
        address: clean(b.address),
        city: clean(b.city),
        region: clean(b.region),
        country: clean(b.country),
        phone: clean(b.phone),
        website: clean(b.website),
        latitude: num(b.latitude),
        longitude: num(b.longitude),
        status: 'UserEdited',
        source: 'Manual',
        providerCourseId: null,
        lastVerifiedAt: null,
        mergedIntoCourseId: null,
        tees: tees.map((t) => buildTee(t)),
      };
      courses.set(id, { course, createdBy: user.email });
      return { status: 201, body: course };
    },
  },
  {
    method: 'GET',
    path: '/courses/{id}',
    handler: ({ params, state }) => {
      const stored = store(state).get(params.id!);
      return stored ? ok(stored.course) : problem(404, 'Not Found');
    },
  },
  {
    method: 'PUT',
    path: '/courses/{id}',
    handler: (req) => {
      const stored = store(req.state).get(req.params.id!);
      if (!stored) return problem(404, 'Not Found');
      const b = body(req);
      const errors: Errors = {};
      validateDetails(b, errors);
      if (Object.keys(errors).length > 0) return validation(errors);
      const c = stored.course;
      if (c.status === 'Verified' && !isAdmin(req)) return propose(req, c.id, null, 'course details');
      Object.assign(c, {
        name: clean(b.name)!,
        clubName: clean(b.clubName),
        address: clean(b.address),
        city: clean(b.city),
        region: clean(b.region),
        country: clean(b.country),
        phone: clean(b.phone),
        website: clean(b.website),
        latitude: num(b.latitude),
        longitude: num(b.longitude),
      });
      if (c.status !== 'Verified') c.status = 'UserEdited';
      return ok(c);
    },
  },
  {
    method: 'POST',
    path: '/courses/{id}/tees',
    handler: (req) => {
      const stored = store(req.state).get(req.params.id!);
      if (!stored) return problem(404, 'Not Found');
      const b = body(req);
      const errors: Errors = {};
      validateTee(b, errors, '');
      if (Object.keys(errors).length > 0) return validation(errors);
      const c = stored.course;
      if (typeof b.id === 'string') {
        for (const s of store(req.state).values()) {
          const existing = s.course.tees.find((t) => t.id === b.id);
          if (existing) return s.course.id === c.id && existing.name === clean(b.name) ? ok(existing) : problem(409, 'Id already in use', 'A tee with this id already exists.');
        }
      }
      if (c.status === 'Verified' && !isAdmin(req)) return propose(req, c.id, null, 'new tee');
      const tee = buildTee(b);
      c.tees.push(tee);
      if (c.status !== 'Verified') c.status = 'UserEdited';
      return { status: 201, body: tee };
    },
  },
  {
    method: 'POST',
    path: '/courses/{id}/tees/{teeId}/versions',
    handler: (req) => {
      const stored = store(req.state).get(req.params.id!);
      const tee = stored?.course.tees.find((t) => t.id === req.params.teeId);
      if (!stored || !tee) return problem(404, 'Not Found');
      const b = body(req);
      const errors: Errors = {};
      validateVersion(b, errors, true);
      if (Object.keys(errors).length > 0) return validation(errors);
      if (stored.course.status === 'Verified' && !isAdmin(req)) return propose(req, stored.course.id, tee.id, `new version of tee "${tee.name}"`);
      const effectiveFrom = String(b.effectiveFrom);
      if (tee.versions.some((v) => v.effectiveFrom === effectiveFrom)) {
        return validation({ effectiveFrom: [`This tee already has a version effective from ${effectiveFrom}.`] });
      }
      const version = buildVersion(b, effectiveFrom);
      tee.versions.push(version);
      tee.versions.sort((x, y) => x.effectiveFrom.localeCompare(y.effectiveFrom));
      markCurrent(tee);
      return { status: 201, body: version };
    },
  },
  {
    method: 'POST',
    path: '/courses/{id}/flags',
    handler: (req) => {
      const stored = store(req.state).get(req.params.id!);
      if (!stored) return problem(404, 'Not Found');
      const b = body(req);
      const errors: Errors = {};
      const note = clean(b.note);
      if (!note) add(errors, 'note', 'A note is required.');
      else if (note.length > 4000) add(errors, 'note', 'At most 4000 characters.');
      const hole = num(b.holeNumber);
      if (hole !== null && (hole < 1 || hole > 18)) add(errors, 'holeNumber', 'Hole number must be between 1 and 18.');
      const teeId = clean(b.teeId);
      if (teeId && !stored.course.tees.some((t) => t.id === teeId)) add(errors, 'teeId', "The tee doesn't belong to this course.");
      if (Object.keys(errors).length > 0) return validation(errors);
      const flag: Schemas['CourseFlagDto'] = {
        id: randomUUID(),
        courseId: stored.course.id,
        teeId,
        holeNumber: hole,
        note: note!,
        status: 'Open',
        createdAt: new Date().toISOString(),
      };
      flags(req.state).push(flag);
      return { status: 201, body: flag };
    },
  },
];
