import { IconSpeakerphone } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const broadcastsRoute: FeatureRoute = {
  area: 'admin',
  path: 'broadcasts',
  page: () => import('./BroadcastsPage'),
  permission: 'Moderate',
  nav: { label: 'broadcasts:title', icon: IconSpeakerphone, group: 'world' },
}
