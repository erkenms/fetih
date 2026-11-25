import { IconTimeline } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export { LogTimeline } from './LogTimeline'

export const logsRoute: FeatureRoute = {
  area: 'admin',
  path: 'logs',
  page: () => import('./LogsPage'),
  permission: 'ViewLogs',
  nav: { label: 'logs:title', icon: IconTimeline, group: 'logs' },
}
