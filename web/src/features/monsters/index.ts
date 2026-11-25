import { IconSkull } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const monstersRoute: FeatureRoute = {
  area: 'admin',
  path: 'monsters',
  page: () => import('./MonstersPage'),
  permission: 'Studio',
  nav: { label: 'monsters:title', icon: IconSkull, group: 'studio' },
}
