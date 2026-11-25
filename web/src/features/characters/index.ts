import { IconUserSearch } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const charactersRoutes: FeatureRoute[] = [
  {
    area: 'admin',
    path: 'characters',
    permission: 'AdminArea',
    page: () => import('./CharactersPage'),
    nav: { label: 'characters:nav', icon: IconUserSearch, group: 'players' },
  },
  {
    area: 'admin',
    path: 'characters/:id',
    permission: 'AdminArea',
    page: () => import('./CharacterDetailPage'),
  },
]
