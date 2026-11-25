import { IconShieldCheck } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const integrityRoute: FeatureRoute = {
  area: 'admin',
  path: 'integrity',
  page: () => import('./IntegrityPage'),
  permission: 'Studio',
  nav: { label: 'integrity:nav', icon: IconShieldCheck, group: 'studio' },
}
