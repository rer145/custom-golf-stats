// Fake PUT /me (full replace) and /me/handicap routes, mirroring src/CustomGolfStats.Api/Features/Me/MeFeature.cs.
// GET /me is a core route (tests/fake-api.ts): it reflects saved names; the other saved fields are recorded calls only.
import { randomUUID } from 'node:crypto';
import { ok, problem, requireUser, type FakeRoute, type FakeState, type Schemas } from '../fake-kit';
import handicapFixture from '../fixtures/profile-handicap.json' with { type: 'json' };

type Entry = Schemas['HandicapEntryResponse'];
const seed: Entry[] = handicapFixture;
/** Users from tests/fixtures/users.json start with the fixture history; new (registered) users with none. */
const SEEDED = /@example\.test$/;

function history(state: FakeState, email: string): Entry[] {
  let all = state.data.get('handicap') as Map<string, Entry[]> | undefined;
  if (!all) {
    all = new Map();
    state.data.set('handicap', all);
  }
  let list = all.get(email);
  if (!list) {
    list = SEEDED.test(email) && !email.startsWith('e2e-') ? seed.map((e) => ({ ...e })) : [];
    all.set(email, list);
  }
  return list;
}

const sorted = (list: Entry[]) => [...list].sort((a, b) => b.effectiveOn.localeCompare(a.effectiveOn) || b.id.localeCompare(a.id));

export const routes: FakeRoute[] = [
  {
    method: 'PUT',
    path: '/me',
    handler: (req) => {
      const user = requireUser(req);
      const b = (req.body ?? {}) as Schemas['UpdateProfileRequest'];
      const errors: Record<string, string[]> = {};
      if (b.gender != null && !['male', 'female', 'unspecified'].includes(b.gender.toLowerCase())) errors.gender = ['Must be one of: Male, Female, Unspecified.'];
      if (b.units != null && !['yards', 'meters'].includes(b.units.toLowerCase())) errors.units = ['Must be one of: Yards, Meters.'];
      if ((b.firstName ?? '').trim().length > 100) errors.firstName = ['Must be at most 100 characters.'];
      if ((b.lastName ?? '').trim().length > 100) errors.lastName = ['Must be at most 100 characters.'];
      if ((b.ghinNumber ?? '').trim().length > 20) errors.ghinNumber = ['Must be at most 20 characters.'];
      if (Object.keys(errors).length > 0) return problem(400, 'One or more validation errors occurred.', undefined, errors);
      user.firstName = b.firstName?.trim() ?? '';
      user.lastName = b.lastName?.trim() ?? '';
      const profile: Schemas['ProfileResponse'] = {
        id: user.id,
        email: user.email,
        firstName: user.firstName,
        lastName: user.lastName,
        gender: b.gender ?? 'Unspecified',
        ghinNumber: b.ghinNumber?.trim() || null,
        homeCourseId: b.homeCourseId ?? null,
        units: b.units ?? 'Yards',
      };
      return ok(profile);
    },
  },
  {
    method: 'GET',
    path: '/me/handicap',
    handler: (req) => ok(sorted(history(req.state, requireUser(req).email))),
  },
  {
    method: 'POST',
    path: '/me/handicap',
    handler: (req) => {
      const b = (req.body ?? {}) as Schemas['CreateHandicapEntryRequest'];
      const errors: Record<string, string[]> = {};
      if (!b.effectiveOn) errors.effectiveOn = ['A date is required.'];
      const index = b.index === null || b.index === undefined ? null : Number(b.index);
      if (index === null) errors.index = ['A handicap index is required.'];
      else if (index < -10 || index > 54) errors.index = ['Must be between -10.0 and 54.0.'];
      else if (Math.round(index * 10) / 10 !== index) errors.index = ['Must have at most one decimal place.'];
      if (Object.keys(errors).length > 0) return problem(400, 'One or more validation errors occurred.', undefined, errors);
      const entry: Entry = { id: randomUUID(), effectiveOn: String(b.effectiveOn), index: index! };
      history(req.state, requireUser(req).email).push(entry);
      return { status: 201, body: entry };
    },
  },
  {
    method: 'DELETE',
    path: '/me/handicap/{id}',
    handler: (req) => {
      const list = history(req.state, requireUser(req).email);
      const i = list.findIndex((e) => e.id === req.params.id);
      if (i < 0) return problem(404, 'Not Found');
      list.splice(i, 1);
      return { status: 204 };
    },
  },
];
