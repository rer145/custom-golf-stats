// Round list (GET /rounds?year=) and scorecards (GET /rounds/{id}/scorecard). Rounds without a scorecard fixture 404.
import { ok, problem, type FakeRoute, type Schemas } from '../fake-kit';
import listFixture from '../fixtures/rounds-list.json' with { type: 'json' };
import scorecardFixture from '../fixtures/rounds-scorecards.json' with { type: 'json' };

const rounds = listFixture.rounds as Schemas['RoundSummaryDto'][];
const scorecards: Schemas['ScorecardResponse'][] = scorecardFixture.scorecards;

export const routes: FakeRoute[] = [
  {
    method: 'GET',
    path: '/rounds',
    handler: ({ query }) => {
      const year = query.get('year');
      if (year !== null && !/^\d{4}$/.test(year)) return problem(400, 'One or more validation errors occurred.', undefined, { year: ['Out of range.'] });
      return ok(year === null ? rounds : rounds.filter((r) => r.playedOn.startsWith(`${year}-`)));
    },
  },
  {
    method: 'GET',
    path: '/rounds/{id}/scorecard',
    handler: ({ params }) => {
      const card = scorecards.find((s) => s.roundId === params.id);
      return card ? ok(card) : problem(404, 'Not Found');
    },
  },
];
