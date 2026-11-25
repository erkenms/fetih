import { IconServer } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const serverRoute: FeatureRoute = {
  area: 'admin',
  path: 'server',
  page: () => import('./ServerControlPage'),
  permission: 'ServerControl',
  nav: { label: 'server:title', icon: IconServer, group: 'system' },
}
