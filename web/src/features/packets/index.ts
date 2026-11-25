import { IconBinary } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export { PacketDecodeView } from './PacketDecodeView'
export { packetSourceFromEntry } from './packet'

export const packetsRoute: FeatureRoute = {
  area: 'admin',
  path: 'packets',
  page: () => import('./PacketsPage'),
  permission: 'ViewLogs',
  nav: { label: 'packets:title', icon: IconBinary, group: 'logs' },
}
