import { IconMoodSmile, IconSkull } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const catalogRoutes: FeatureRoute[] = [
  {
    area: 'admin',
    path: 'catalogs/monsters',
    page: () => import('./MonstersCatalogPage'),
    permission: 'ViewCatalogs',
    nav: { label: 'catalogs:nav.monsters', icon: IconSkull, group: 'catalogs' },
  },
  {
    area: 'admin',
    path: 'catalogs/faces',
    page: () => import('./FacesCatalogPage'),
    permission: 'ViewCatalogs',
    nav: { label: 'catalogs:nav.faces', icon: IconMoodSmile, group: 'catalogs' },
  },
]
