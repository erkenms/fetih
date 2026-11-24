import { IconTimeline } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export { AccountTimelineTab } from './AccountTimelineTab'
export { CharacterTimelineTab } from './CharacterTimelineTab'
export { LogTimeline } from './LogTimeline'

export const logsRoute: FeatureRoute = {
  area: 'admin',
  path: 'logs',
  page: () => import('./LogsPage'),
  permission: 'ViewLogs',
  nav: { label: 'logs:title', icon: IconTimeline, group: 'logs' },
}
