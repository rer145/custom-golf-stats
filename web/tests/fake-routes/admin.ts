// Fake of the /admin API (src/CustomGolfStats.Api/Features/Admin) for end-to-end tests. Admin only (403 otherwise).
//
// Data is seeded per entity so parallel tests each use their own (see the fixtures: admin-users, admin-courses,
// admin-flags). The admin pages read a course with the public GET /courses/{id}; the real route belongs to the
// courses feature, so this module answers only for its own course ids (`ad000000-0000-4000-8000-…`) to avoid
// shadowing it.
import { ok, problem, requireUser, type FakeRequest, type FakeResponse, type FakeRoute, type Schemas } from '../fake-kit';
import coursesSeed from '../fixtures/admin-courses.json' with { type: 'json' };
import flagsSeed from '../fixtures/admin-flags.json' with { type: 'json' };
import usersSeed from '../fixtures/admin-users.json' with { type: 'json' };

const COURSE_PREFIX = 'ad000000-0000-4000-8000-';
const courseId = (n: number) => `${COURSE_PREFIX}${String(n).padStart(12, '0')}`;
const teeId = (n: number) => `ad222222-0000-4000-8000-${String(n).padStart(12, '0')}`;
const versionId = (n: number) => `ad444444-0000-4000-8000-${String(n).padStart(12, '0')}`;

const PARS = [4, 4, 3, 5, 4, 4, 3, 4, 5, 4, 4, 3, 5, 4, 4, 3, 4, 5];
const SI = [7, 1, 15, 11, 3, 13, 17, 5, 9, 8, 2, 16, 12, 4, 14, 18, 6, 10];

function holes(): Schemas['TeeHoleDto'][] {
  return PARS.map((par, i) => ({ number: i + 1, par, yardage: 340 + i * 12, strokeIndex: SI[i]! }));
}

interface CourseEntry {
  detail: Schemas['CourseDetailDto'];
  playedHoles: number[];
  updatedAt: string;
}

interface AdminState {
  users: Schemas['AdminUserDto'][];
  courses: Map<string, CourseEntry>;
  flags: Schemas['AdminFlagDto'][];
  trialShotRounds: number;
}

type Seed = {
  n: number;
  name: string;
  clubName: string;
  city: string;
  region: string;
  country: string;
  status: Schemas['CourseStatus'];
  source: Schemas['CourseSource'];
  playedHoles?: number[];
};

function seed(): AdminState {
  const courses = new Map<string, CourseEntry>();
  for (const s of coursesSeed as Seed[]) {
    const detail: Schemas['CourseDetailDto'] = {
      id: courseId(s.n),
      name: s.name,
      clubName: s.clubName,
      address: null,
      city: s.city,
      region: s.region,
      country: s.country,
      phone: null,
      website: null,
      latitude: null,
      longitude: null,
      status: s.status,
      source: s.source,
      providerCourseId: null,
      lastVerifiedAt: s.status === 'Verified' ? '2026-05-01T00:00:00Z' : null,
      mergedIntoCourseId: null,
      tees: [
        {
          id: teeId(s.n),
          name: 'Blue',
          gender: 'Male',
          versions: [
            {
              id: versionId(s.n),
              effectiveFrom: '2020-01-01',
              isCurrent: true,
              par: PARS.reduce((a, b) => a + b, 0),
              rating: 71.4,
              slope: 128,
              frontPar: null,
              frontRating: null,
              frontSlope: null,
              backPar: null,
              backRating: null,
              backSlope: null,
              source: s.source,
              changedAt: '2026-01-01T00:00:00Z',
              holes: holes(),
            },
          ],
        },
      ],
    };
    courses.set(detail.id, { detail, playedHoles: s.playedHoles ?? [], updatedAt: '2026-06-01T00:00:00Z' });
  }
  return {
    users: (usersSeed as Schemas['AdminUserDto'][]).map((u) => ({ ...u })),
    courses,
    flags: (flagsSeed as Schemas['AdminFlagDto'][]).map((f) => ({ ...f })),
    trialShotRounds: 3,
  };
}

function admin(state: FakeRequest['state']): AdminState {
  let s = state.data.get('admin') as AdminState | undefined;
  if (!s) {
    s = seed();
    state.data.set('admin', s);
  }
  return s;
}

const FORBIDDEN: FakeResponse = { status: 403 };
const adminOnly =
  (handler: (req: FakeRequest, s: AdminState) => FakeResponse): FakeRoute['handler'] =>
  (req) =>
    requireUser(req).role !== 'Admin' ? FORBIDDEN : handler(req, admin(req.state));

const validation = (errors: Record<string, string[]>) => problem(400, 'One or more validation errors occurred.', undefined, errors);
const has = (value: string | null | undefined, needle: string) => (value ?? '').toLowerCase().includes(needle.toLowerCase());

function summary(c: CourseEntry, s: AdminState): Schemas['AdminCourseSummaryDto'] {
  const d = c.detail;
  return {
    id: d.id,
    name: d.name,
    clubName: d.clubName,
    city: d.city,
    region: d.region,
    country: d.country,
    status: d.status,
    source: d.source,
    lastVerifiedAt: d.lastVerifiedAt,
    mergedIntoCourseId: d.mergedIntoCourseId,
    openFlags: s.flags.filter((f) => f.courseId === d.id && f.status === 'Open').length,
    updatedAt: c.updatedAt,
  };
}

function touch(c: CourseEntry): void {
  c.updatedAt = new Date().toISOString();
  if (c.detail.status !== 'Verified') c.detail.status = 'UserEdited';
}

const closeFlag = (decision: 'Resolved' | 'Dismissed'): FakeRoute['handler'] =>
  adminOnly((req, s) => {
    const flag = s.flags.find((f) => f.id === req.params.id);
    if (!flag) return { status: 404 };
    // (flag-conflict) is closed behind the admin's back, like another admin got there first.
    if (flag.status !== 'Open' || flag.note.includes('(flag-conflict)')) return problem(409, 'Flag already closed', `This flag is already ${flag.status === 'Open' ? 'Resolved' : flag.status}.`);
    flag.status = decision;
    flag.resolvedAt = new Date().toISOString();
    flag.resolvedByAccountId = requireUser(req).id;
    return ok(flag);
  });

export const routes: FakeRoute[] = [
  // ---- Users ----
  {
    method: 'GET',
    path: '/admin/users',
    handler: adminOnly((req, s) => {
      const q = req.query.get('query')?.trim() ?? '';
      const me = requireUser(req);
      const self: Schemas['AdminUserDto'] = {
        id: me.id,
        email: me.email,
        firstName: me.firstName,
        lastName: me.lastName,
        role: me.role,
        tier: me.tier,
        trialShotRoundsUsed: 0,
        disabledAt: null,
        createdAt: '2026-01-01T00:00:00Z',
      };
      const list = [self, ...s.users].filter((u) => !q || has(u.email, q) || has(u.firstName, q) || has(u.lastName, q));
      return ok(list.sort((a, b) => a.email.localeCompare(b.email)).slice(0, 100));
    }),
  },
  {
    method: 'PUT',
    path: '/admin/users/{id}',
    handler: adminOnly((req, s) => {
      const body = (req.body ?? {}) as { tier?: string | null; role?: string | null; disabled?: boolean | null };
      const errors: Record<string, string[]> = {};
      const parse = <T extends string>(value: string | null | undefined, names: readonly T[]): T | undefined => names.find((n) => n.toLowerCase() === value?.trim().toLowerCase());
      const tier = body.tier == null ? undefined : parse(body.tier, ['Free', 'Paid'] as const);
      const role = body.role == null ? undefined : parse(body.role, ['User', 'Admin'] as const);
      if (body.tier != null && !tier) errors.tier = ['Tier must be one of Free, Paid.'];
      if (body.role != null && !role) errors.role = ['Role must be one of User, Admin.'];
      const me = requireUser(req);
      const isSelf = req.params.id === me.id;
      if (isSelf && role && role !== 'Admin') errors.role = ["You can't remove your own Admin role."];
      if (isSelf && body.disabled === true) errors.disabled = ["You can't disable your own account."];
      if (Object.keys(errors).length > 0) return validation(errors);
      const user = s.users.find((u) => u.id === req.params.id);
      if (!user) return { status: 404 };
      user.tier = tier ?? user.tier;
      user.role = role ?? user.role;
      if (body.disabled != null) user.disabledAt = body.disabled ? (user.disabledAt ?? new Date().toISOString()) : null;
      return ok(user);
    }),
  },

  // ---- Courses ----
  {
    method: 'GET',
    path: '/admin/courses',
    handler: adminOnly((req, s) => {
      const status = req.query.get('status')?.trim();
      const q = req.query.get('query')?.trim();
      let wanted: string | undefined;
      if (status) {
        wanted = ['Imported', 'UserEdited', 'Verified'].find((n) => n.toLowerCase() === status.toLowerCase());
        if (!wanted) return validation({ status: ['Status must be one of Imported, UserEdited, Verified.'] });
      }
      const list = [...s.courses.values()]
        .filter((c) => !wanted || c.detail.status === wanted)
        .filter((c) => !q || has(c.detail.name, q) || has(c.detail.clubName, q) || has(c.detail.city, q))
        .map((c) => summary(c, s))
        .sort((a, b) => a.name.localeCompare(b.name))
        .slice(0, 200);
      return ok(list);
    }),
  },
  {
    // Answers only for this module's courses; see the header comment.
    method: 'GET',
    path: `/courses/${COURSE_PREFIX}{rest}`,
    handler: (req) => {
      const c = admin(req.state).courses.get(`${COURSE_PREFIX}${req.params.rest}`);
      return c ? ok(c.detail) : problem(404, 'Not Found');
    },
  },
  {
    method: 'PUT',
    path: '/admin/courses/{id}',
    handler: adminOnly((req, s) => {
      const c = s.courses.get(req.params.id!);
      if (!c) return { status: 404 };
      const b = (req.body ?? {}) as Partial<Schemas['CourseDetailsRequest']>;
      if (!b.name?.trim()) return validation({ Name: ['Name is required.'] });
      if (b.latitude != null && Math.abs(Number(b.latitude)) > 90) return validation({ Latitude: ['Latitude must be between -90 and 90.'] });
      const d = c.detail;
      d.name = b.name.trim();
      d.clubName = b.clubName ?? null;
      d.address = b.address ?? null;
      d.city = b.city ?? null;
      d.region = b.region ?? null;
      d.country = b.country ?? null;
      d.phone = b.phone ?? null;
      d.website = b.website ?? null;
      d.latitude = b.latitude ?? null;
      d.longitude = b.longitude ?? null;
      touch(c);
      return ok(d);
    }),
  },
  {
    method: 'POST',
    path: '/admin/courses/{id}/verify',
    handler: adminOnly((req, s) => {
      const c = s.courses.get(req.params.id!);
      if (!c) return { status: 404 };
      c.detail.status = 'Verified';
      c.detail.lastVerifiedAt = new Date().toISOString();
      c.updatedAt = c.detail.lastVerifiedAt;
      return ok(c.detail);
    }),
  },
  {
    method: 'PUT',
    path: '/admin/courses/{id}/tees/{teeId}/versions/{versionId}',
    handler: adminOnly((req, s) => {
      const c = s.courses.get(req.params.id!);
      const tee = c?.detail.tees.find((t) => t.id === req.params.teeId);
      const version = tee?.versions.find((v) => v.id === req.params.versionId);
      if (!c || !version) return { status: 404 };
      const b = (req.body ?? {}) as Partial<Schemas['TeeVersionInput']>;
      const errors: Record<string, string[]> = {};
      if (b.rating == null) errors.Rating = ['Rating is required.'];
      if (b.slope == null || Number(b.slope) < 55 || Number(b.slope) > 155) errors.Slope = ['Slope must be between 55 and 155.'];
      const sent = b.holes ?? [];
      sent.forEach((h, i) => {
        if (Number(h.par) < 3 || Number(h.par) > 6) errors[`Holes[${i}].Par`] = ['Par must be between 3 and 6.'];
      });
      if (Object.keys(errors).length > 0) return validation(errors);
      const sentNumbers = new Set(sent.map((h) => Number(h.number)));
      const removed = c.playedHoles.filter((n) => !sentNumbers.has(n));
      if (removed.length > 0) {
        const p = problem(409, "Played holes can't be removed", 'Rounds on this tee version have played holes the change removes. Keep those holes, or create a new version with the date the change took effect.');
        (p.body as Record<string, unknown>).holeNumbers = removed;
        return p;
      }
      version.effectiveFrom = b.effectiveFrom ?? version.effectiveFrom;
      version.rating = Number(b.rating);
      version.slope = Number(b.slope);
      version.frontRating = b.frontRating ?? null;
      version.frontSlope = b.frontSlope ?? null;
      version.backRating = b.backRating ?? null;
      version.backSlope = b.backSlope ?? null;
      version.holes = sent.map((h) => ({ number: Number(h.number), par: Number(h.par), yardage: Number(h.yardage), strokeIndex: Number(h.strokeIndex) }));
      version.par = version.holes.reduce((sum, h) => sum + Number(h.par), 0);
      touch(c);
      return ok(version);
    }),
  },
  {
    method: 'POST',
    path: '/admin/courses/{id}/merge',
    handler: adminOnly((req, s) => {
      const source = s.courses.get(req.params.id!);
      if (!source) return { status: 404 };
      const into = (req.body as { intoCourseId?: string | null } | undefined)?.intoCourseId;
      if (!into) return validation({ intoCourseId: ['intoCourseId is required.'] });
      if (into === source.detail.id) return validation({ intoCourseId: ["A course can't be merged into itself."] });
      const target = s.courses.get(into);
      if (!target) return validation({ intoCourseId: ["The course to merge into doesn't exist."] });
      if (target.detail.mergedIntoCourseId) return validation({ intoCourseId: ['The course to merge into has itself been merged; merge into the course it was merged into.'] });
      if (source.detail.mergedIntoCourseId) return problem(409, 'Course already merged', 'This course has already been merged into another course.');
      if (source.detail.name.includes('Busy')) return problem(409, 'Course is busy', 'Rounds kept being saved on this course while merging. Nothing was changed; try again.');
      if (source.detail.name === 'Fern Valley') {
        const p = problem(409, "Tee versions don't match", "Rounds use tee versions that have no match (same tee name and gender, same effective date) in the course to merge into. Add matching tees or versions there, then merge again. Nothing was changed.");
        const unmatched: Record<string, unknown>[] = [
          { teeId: teeId(7), teeName: 'Blue', gender: 'Male', versionId: versionId(7), effectiveFrom: '2020-01-01', rounds: 4, reason: 'NoMatchingVersion' },
          { teeId: teeId(70), teeName: 'Red', gender: 'Female', versionId: versionId(70), effectiveFrom: '2018-04-01', rounds: 2, reason: 'AmbiguousTee' },
          { teeId: teeId(71), teeName: 'White', gender: 'Male', versionId: versionId(71), effectiveFrom: '2019-06-01', rounds: 1, reason: 'MissingHoles' },
        ];
        (p.body as Record<string, unknown>).unmatched = unmatched;
        return p;
      }
      source.detail.mergedIntoCourseId = target.detail.id;
      const flagsMoved = s.flags.filter((f) => f.courseId === source.detail.id && f.status === 'Open');
      for (const f of flagsMoved) {
        f.courseId = target.detail.id;
        f.courseName = target.detail.name;
      }
      const result: Schemas['CourseMergeResultDto'] = { courseId: source.detail.id, intoCourseId: target.detail.id, roundsMoved: 12, flagsMoved: flagsMoved.length };
      return ok(result);
    }),
  },

  // ---- Flags ----
  {
    method: 'GET',
    path: '/admin/flags',
    handler: adminOnly((req, s) => {
      const raw = req.query.get('status')?.trim();
      let wanted: string | undefined = 'Open';
      if (raw) {
        wanted = ['Open', 'Resolved', 'Dismissed'].find((n) => n.toLowerCase() === raw.toLowerCase());
        if (!wanted) return validation({ status: ['Status must be one of Open, Resolved, Dismissed.'] });
      }
      return ok(s.flags.filter((f) => f.status === wanted).sort((a, b) => a.createdAt.localeCompare(b.createdAt)));
    }),
  },
  { method: 'POST', path: '/admin/flags/{id}/resolve', handler: closeFlag('Resolved') },
  { method: 'POST', path: '/admin/flags/{id}/dismiss', handler: closeFlag('Dismissed') },

  // ---- Settings ----
  {
    method: 'GET',
    path: '/admin/settings',
    handler: adminOnly((_req, s) => ok({ trialShotRounds: s.trialShotRounds } satisfies Schemas['AdminSettingsDto'])),
  },
  {
    method: 'PUT',
    path: '/admin/settings',
    handler: adminOnly((req, s) => {
      const n = (req.body as { trialShotRounds?: number | null } | undefined)?.trialShotRounds;
      if (n === null || n === undefined || !Number.isInteger(n) || n < 0) return validation({ TrialShotRounds: ['TrialShotRounds must be a whole number, 0 or more.'] });
      s.trialShotRounds = n;
      return ok({ trialShotRounds: n } satisfies Schemas['AdminSettingsDto']);
    }),
  },
];
