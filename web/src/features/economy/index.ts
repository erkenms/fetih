import { IconCoins } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const economyRoute: FeatureRoute = {
  area: 'admin',
  path: 'economy',
  page: () => import('./EconomyPage'),
  permission: 'AdminArea',
  bare: true,
  nav: { label: 'economy:title', icon: IconCoins, group: 'overview' },
}
