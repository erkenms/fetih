import { IconSword } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const itemsRoute: FeatureRoute = {
  area: 'admin',
  path: 'items',
  page: () => import('./ItemsPage'),
  permission: 'ViewCatalogs',
  nav: { label: 'items:title', icon: IconSword, group: 'catalogs' },
}
