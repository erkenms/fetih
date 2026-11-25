import { IconSkull } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const pkRoute: FeatureRoute = {
  area: 'admin',
  path: 'pk',
  page: () => import('./PkPage'),
  permission: 'AdminArea',
  nav: { label: 'pk:title', icon: IconSkull, group: 'players' },
}
