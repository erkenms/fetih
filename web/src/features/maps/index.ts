import { IconMap2 } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const mapsRoutes: FeatureRoute[] = [
  {
    area: 'admin',
    path: 'maps',
    page: () => import('./MapBrowserPage'),
    permission: 'ViewCatalogs',
    nav: { label: 'maps:nav', icon: IconMap2, group: 'studio' },
  },
  {
    area: 'admin',
    path: 'maps/:id',
    page: () => import('./MapViewerPage'),
    permission: 'ViewCatalogs',
  },
]
