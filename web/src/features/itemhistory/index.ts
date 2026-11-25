import { IconHistory } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const itemHistoryRoutes: FeatureRoute[] = [
  {
    area: 'admin',
    path: 'item-history',
    page: () => import('./ItemHistoryPage'),
    permission: 'ViewLogs',
    bare: true,
    nav: { label: 'itemHistory:title', icon: IconHistory, group: 'logs' },
  },
  {
    area: 'admin',
    path: 'item-history/:itemId',
    page: () => import('./ItemHistoryPage'),
    permission: 'ViewLogs',
    bare: true,
  },
]
