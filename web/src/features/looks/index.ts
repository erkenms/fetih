import { IconUser } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const looksRoute: FeatureRoute = {
  area: 'admin',
  path: 'catalogs/looks',
  page: () => import('./LooksCatalogPage'),
  permission: 'ViewCatalogs',
  nav: { label: 'catalogs:nav.looks', icon: IconUser, group: 'catalogs' },
}
