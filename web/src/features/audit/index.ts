import { IconHistory } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const auditRoute: FeatureRoute = {
  area: 'admin',
  path: 'audit',
  page: () => import('./AuditPage'),
  permission: 'ViewAudit',
  nav: { label: 'audit:title', icon: IconHistory, group: 'logs' },
}
