// Stats section endpoints. 2025 has full fixtures; every other year answers like the real API does for a year with no
// rounds (zero counts, null values, empty lists), with the handicap index as of that year's end.
import { ok, type FakeRoute, type Schemas } from '../fake-kit';
import yearFixture from '../fixtures/stats-2025.json' with { type: 'json' };
import careerFixture from '../fixtures/stats-career.json' with { type: 'json' };

interface YearFixture {
  overall: Schemas['OverallResponse'];
  season: Schemas['SeasonResponse'];
  results: Schemas['ResultsResponse'];
  scoring: Schemas['ScoringResponse'];
  offTheTee: Schemas['OffTheTeeResponse'];
  approach: Schemas['ApproachResponse'];
  putting: Schemas['PuttingResponse'];
  courses: Schemas['CoursesResponse'];
}

const FULL_YEAR = 2025;
const y2025: YearFixture = yearFixture;
const career: Schemas['CareerResponse'] = careerFixture.career;
const handicap: Schemas['HandicapHistoryResponse'] = careerFixture.handicap;

const avg = (): Schemas['AverageDto'] => ({ value: null, total: 0, count: 0 });
const ratio = (): Schemas['RatioDto'] => ({ value: null, numerator: 0, denominator: 0 });
const buckets = (edges: [number | null, number | null][]): Schemas['ScoreBucketDto'][] => edges.map(([min, max]) => ({ min, max, count: 0 }));

function emptyYear(year: number): YearFixture {
  const latest = [...handicap.entries].reverse().find((e) => e.effectiveOn <= `${year}-12-31`) ?? null;
  return {
    overall: { year, scoringAverage18: avg(), holesPlayed: 0, toPar: 0, handicapIndex: latest?.index ?? null, handicapEffectiveOn: latest?.effectiveOn ?? null },
    season: {
      rounds9: 0,
      rounds18: 0,
      roundsOtherLength: 0,
      histogram9: buckets([[null, 39], [40, 49], [50, null]]),
      histogram18: buckets([[null, 79], [80, 89], [90, 99], [100, null]]),
    },
    results: { rounds: [] },
    scoring: {
      scoringAverage9: avg(), scoringAverage18: avg(), lowest9: null, highest9: null, lowest18: null, highest18: null,
      birdies: 0, eagles: 0, parBreakers: ratio(), par3Average: avg(), par4Average: avg(), par5Average: avg(), birdieOrBetter: ratio(),
    },
    offTheTee: {
      drivingAccuracy: ratio(), leftTendency: ratio(), rightTendency: ratio(),
      par4ToParFromFairway: avg(), par4ToParFromLeft: avg(), par4ToParFromRight: avg(),
      par5ToParFromFairway: avg(), par5ToParFromLeft: avg(), par5ToParFromRight: avg(),
    },
    approach: { gir: ratio(), girFromFairway: ratio(), girFromLeft: ratio(), girFromRight: ratio(), sandSave: ratio(), scrambling: ratio(), holeOut: ratio() },
    putting: {
      puttsPerGir: avg(), puttsPerHole: avg(), onePutt: ratio(), threePuttRate: ratio(),
      puttsPerRound9: avg(), puttsPerRound18: avg(), birdieConversion: ratio(),
    },
    courses: { courses: [] },
  };
}

const forYear = (year: string | undefined): YearFixture => (Number(year) === FULL_YEAR ? y2025 : emptyYear(Number(year)));

const section = (path: string, pick: (y: YearFixture) => unknown): FakeRoute => ({
  method: 'GET',
  path: `/stats/{year}/${path}`,
  handler: ({ params }) => ok(pick(forYear(params.year))),
});

export const routes: FakeRoute[] = [
  section('overall', (y) => y.overall),
  section('season', (y) => y.season),
  section('results', (y) => y.results),
  section('scoring', (y) => y.scoring),
  section('off-the-tee', (y) => y.offTheTee),
  section('approach', (y) => y.approach),
  section('putting', (y) => y.putting),
  { method: 'GET', path: '/stats/career', handler: () => ok(career) },
  { method: 'GET', path: '/stats/handicap', handler: () => ok(handicap) },
  {
    method: 'GET',
    path: '/stats/courses',
    handler: ({ query }) => {
      const year = query.get('year');
      return ok(year === null ? y2025.courses : forYear(year).courses);
    },
  },
];
