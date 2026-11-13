/**
 * The Chart.js island: draws every `figure[data-chart]` on the page from its JSON spec. Runs in the browser only and
 * talks to nobody: the data is already in the page. Colours come from the brand's CSS custom properties at draw time
 * and are re-read when the colour scheme changes. Until a chart draws, its figure shows the data table instead.
 */
import {
  BarController,
  BarElement,
  CategoryScale,
  Chart,
  LinearScale,
  LineController,
  LineElement,
  PointElement,
  Tooltip,
  type ChartConfiguration,
} from 'chart.js';
import { formatValue, type ChartSpec } from './chart-spec';

Chart.register(BarController, BarElement, CategoryScale, LinearScale, LineController, LineElement, PointElement, Tooltip);

interface Palette {
  primary: string;
  accent: string;
  border: string;
  textSecondary: string;
  inverse: string;
  textInverse: string;
  mono: string;
}

function palette(): Palette {
  const s = getComputedStyle(document.documentElement);
  const v = (name: string) => s.getPropertyValue(name).trim();
  return {
    primary: v('--color-primary'),
    accent: v('--color-accent'),
    border: v('--color-border'),
    textSecondary: v('--color-text-secondary'),
    inverse: v('--color-surface-inverse'),
    textInverse: v('--color-text-inverse'),
    mono: v('--font-mono'),
  };
}

function config(spec: ChartSpec, p: Palette): ChartConfiguration<'bar' | 'line'> {
  const horizontal = spec.kind === 'hbar';
  const colours = spec.labels.map((_, i) => (i === spec.highlight ? p.accent : p.primary));
  const valueAxis = {
    ...(spec.min !== undefined ? { min: spec.min } : {}),
    ...(spec.max !== undefined ? { max: spec.max } : {}),
    beginAtZero: spec.kind !== 'line',
    grid: { color: p.border },
    border: { display: false },
    ticks: {
      color: p.textSecondary,
      font: { family: p.mono, size: 12 },
      callback: (v: string | number) => formatValue(spec.format, Number(v)),
      maxTicksLimit: 6,
      maxRotation: 0,
      autoSkipPadding: 12,
      // Counts are whole: no 0.5-round ticks.
      ...(spec.format === 'integer' ? { precision: 0 } : {}),
    },
  };
  const categoryAxis = {
    grid: { display: false },
    border: { color: p.border },
    ticks: { color: p.textSecondary, font: { family: p.mono, size: 12 }, maxRotation: 0, autoSkipPadding: 12 },
  };
  return {
    type: spec.kind === 'line' ? 'line' : 'bar',
    data: {
      labels: spec.labels,
      datasets: spec.series.map((s) => ({
        label: s.name,
        data: s.values,
        backgroundColor: spec.kind === 'line' ? p.primary : colours,
        borderColor: p.primary,
        borderWidth: spec.kind === 'line' ? 2 : 0,
        borderRadius: spec.kind === 'line' ? 0 : 4,
        maxBarThickness: 44,
        pointRadius: spec.kind === 'line' ? 3 : 0,
        pointBackgroundColor: spec.kind === 'line' ? colours : p.primary,
        pointBorderWidth: 0,
        spanGaps: true,
      })),
    },
    options: {
      indexAxis: horizontal ? 'y' : 'x',
      responsive: true,
      maintainAspectRatio: false,
      animation: false,
      plugins: {
        legend: { display: false },
        tooltip: {
          backgroundColor: p.inverse,
          titleColor: p.textInverse,
          bodyColor: p.textInverse,
          titleFont: { family: p.mono },
          bodyFont: { family: p.mono },
          displayColors: false,
          callbacks: { label: (item) => `${item.dataset.label ?? ''}: ${formatValue(spec.format, item.raw as number | null)}` },
        },
      },
      scales: horizontal ? { x: valueAxis, y: categoryAxis } : { x: categoryAxis, y: valueAxis },
    },
  };
}

const charts: Chart[] = [];

function mount(figure: HTMLElement): void {
  if (figure.dataset.ready) return;
  const canvas = figure.querySelector('canvas');
  const raw = figure.dataset.chart;
  if (!canvas || !raw) return;
  try {
    const spec = JSON.parse(raw) as ChartSpec;
    const chart = new Chart(canvas, config(spec, palette()));
    (chart as Chart & { spec?: ChartSpec }).spec = spec;
    charts.push(chart);
    figure.dataset.ready = 'true';
    // The chart is up: tuck the data table away (still one click from anyone who wants it).
    figure.querySelector('details')?.removeAttribute('open');
  } catch {
    // Leave the data table showing.
  }
}

export function mountCharts(): void {
  document.querySelectorAll<HTMLElement>('figure[data-chart]').forEach(mount);
}

/** Light/dark switched: redraw with the other theme's tokens. */
function repaint(): void {
  const p = palette();
  for (const chart of charts) {
    const spec = (chart as Chart & { spec?: ChartSpec }).spec;
    if (!spec) continue;
    const next = config(spec, p);
    chart.data = next.data;
    chart.options = next.options ?? {};
    chart.update();
  }
}

window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', repaint);
