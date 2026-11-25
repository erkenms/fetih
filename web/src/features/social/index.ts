import { IconUsersGroup } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const socialRoute: FeatureRoute = {
  area: 'admin',
  path: 'social',
  page: () => import('./SocialPage'),
  permission: 'AdminArea',
  nav: { label: 'social:title', icon: IconUsersGroup, group: 'social' },
}
