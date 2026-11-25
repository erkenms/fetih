import type { FeatureRoute } from '../../../app/feature'

export const publicHeatmapRoute: FeatureRoute = {
  area: 'public',
  path: 'world-map',
  page: () => import('./HeatmapPage'),
  bare: true,
  nav: { label: 'publicHeatmap:nav' },
}
