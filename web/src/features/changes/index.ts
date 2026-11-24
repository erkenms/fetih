import { IconGitCompare } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const changesRoute: FeatureRoute = {
  area: 'admin',
  path: 'changes',
  page: () => import('./ChangesPage'),
  permission: 'Studio',
  nav: { label: 'changes:title', icon: IconGitCompare, group: 'studio' },
}
