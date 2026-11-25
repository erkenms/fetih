import { IconRadar2 } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const liveRoutes: FeatureRoute[] = [
  {
    area: 'admin',
    path: 'live',
    page: () => import('./LiveWorldPage'),
    permission: 'ViewLive',
    nav: { label: 'live:nav', icon: IconRadar2, group: 'world' },
  },
  {
    area: 'admin',
    path: 'live/:mapId',
    page: () => import('./LiveWorldPage'),
    permission: 'ViewLive',
  },
]
