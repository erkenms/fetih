import { IconMessageDots } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const chatRoute: FeatureRoute = {
  area: 'admin',
  path: 'chat',
  page: () => import('./ChatPage'),
  permission: 'ViewLogs',
  nav: { label: 'chat:nav', icon: IconMessageDots, group: 'logs' },
}
