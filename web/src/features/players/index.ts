import { IconUsers } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const playersRoutes: FeatureRoute[] = [
  {
    area: 'admin',
    path: 'players',
    permission: 'AdminArea',
    page: () => import('./PlayersPage'),
    nav: { label: 'players:nav', icon: IconUsers, group: 'players' },
  },
  {
    area: 'admin',
    path: 'players/:id',
    permission: 'AdminArea',
    page: () => import('./PlayerDetailPage'),
  },
  ...['accounts', 'characters'].map(
    (path): FeatureRoute => ({
      area: 'admin',
      path,
      permission: 'AdminArea',
      page: () => import('./redirects').then((m) => ({ default: m.PlayersRedirect })),
    }),
  ),
  {
    area: 'admin',
    path: 'accounts/:id',
    permission: 'AdminArea',
    page: () => import('./redirects').then((m) => ({ default: m.AccountRedirect })),
  },
  {
    area: 'admin',
    path: 'characters/:id',
    permission: 'AdminArea',
    page: () => import('./redirects').then((m) => ({ default: m.CharacterRedirect })),
  },
]
