import { IconUsers } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const accountsRoutes: FeatureRoute[] = [
  {
    area: 'admin',
    path: 'accounts',
    permission: 'AdminArea',
    page: () => import('./AccountsPage'),
    nav: { label: 'accounts:nav', icon: IconUsers, group: 'players' },
  },
  {
    area: 'admin',
    path: 'accounts/:id',
    permission: 'AdminArea',
    page: () => import('./AccountDetailPage'),
  },
]
