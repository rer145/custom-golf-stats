// Example feature route module (see tests/fake-api.ts). Fixtures are typed against the OpenAPI schema here.
import { ok, type FakeRoute, type Schemas } from '../fake-kit';
import yearsFixture from '../fixtures/stats-years.json' with { type: 'json' };

const years: Schemas['StatsYearsResponse'] = yearsFixture;

export const routes: FakeRoute[] = [{ method: 'GET', path: '/stats/years', handler: () => ok(years) }];
