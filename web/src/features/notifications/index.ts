import { IconBell } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const notificationsRoute: FeatureRoute = {
  area: 'admin',
  path: 'notifications',
  page: () => import('./NotificationsPage'),
  permission: 'ManageAccounts',
  nav: { label: 'notifications:title', icon: IconBell, group: 'system' },
}
