import { IconMessageDots } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const npcDialogsRoutes: FeatureRoute[] = [
  {
    area: 'admin',
    path: 'npc-dialogs',
    page: () => import('./NpcDialogsPage'),
    permission: 'ViewCatalogs',
    bare: true,
    nav: { label: 'npcDialogs:title', icon: IconMessageDots, group: 'catalogs' },
  },
  {
    area: 'admin',
    path: 'npc-dialogs/:npcId',
    page: () => import('./NpcDialogsPage'),
    permission: 'ViewCatalogs',
    bare: true,
  },
]
