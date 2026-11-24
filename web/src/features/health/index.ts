import { IconHeartbeat } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

// The example feature: copy this folder (and locales/*/health.json) to start a new one,
// then add its route to app/routes.tsx.
export const healthRoute: FeatureRoute = {
  area: 'admin',
  path: 'health',
  page: () => import('./HealthPage'),
  permission: 'AdminArea',
  nav: { label: 'health:title', icon: IconHeartbeat, group: 'system' },
}
