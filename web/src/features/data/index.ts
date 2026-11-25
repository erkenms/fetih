import { IconDatabaseEdit } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const dataRoute: FeatureRoute = {
  area: 'admin',
  path: 'data',
  page: () => import('./DataEditorsPage'),
  permission: 'Studio',
  nav: { label: 'data:navTitle', icon: IconDatabaseEdit, group: 'studio' },
}
