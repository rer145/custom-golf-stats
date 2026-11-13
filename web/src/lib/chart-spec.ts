import { formatAverage, formatDecimal, formatInteger, formatPercent, formatToPar, type Numeric } from './format';

/** How a chart's values are written (axis ticks, tooltips and the fallback table all use format.ts). */
export type ValueFormat = 'percent' | 'average' | 'integer' | 'toPar' | 'decimal';

export interface ChartSeries {
  name: string;
  /** null leaves a gap (a stat with no data). */
  values: (number | null)[];
}

/** What Chart.astro serialises into `data-chart` for the client island. Plain JSON: no functions, no colours. */
export interface ChartSpec {
  kind: 'bar' | 'hbar' | 'line';
  labels: string[];
  series: ChartSeries[];
  format: ValueFormat;
  /** Index of the one bar or point drawn in the accent: the number that matters. */
  highlight?: number;
  min?: number;
  max?: number;
}

export function formatValue(format: ValueFormat, value: Numeric): string {
  switch (format) {
    case 'percent':
      return formatPercent(value);
    case 'average':
      return formatAverage(value);
    case 'integer':
      return formatInteger(value);
    case 'toPar':
      return formatToPar(value);
    case 'decimal':
      return formatDecimal(value);
  }
}

/** An API number (`number | string | null`) as a chart value. */
export function chartValue(value: Numeric): number | null {
  if (value === null || value === undefined || value === '') return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}
