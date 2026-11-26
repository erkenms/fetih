import type { FeatureRoute } from './feature'
import { auditRoute } from '../features/audit'
import { catalogRoutes } from '../features/catalogs'
import { economyRoute } from '../features/economy'
import { itemsRoute } from '../features/items'
import { logsRoute } from '../features/logs'
import { itemHistoryRoutes } from '../features/itemhistory'
import { chatRoute } from '../features/chat'
import { mapsRoutes } from '../features/maps'
import { npcDialogsRoutes } from '../features/npcdialogs'
import { mapEditRoute } from '../features/maps/edit'
import { liveRoutes } from '../features/live'

// Every feature page, one line each. Admin pages show in the sidebar by their nav group.
export const featureRoutes: FeatureRoute[] = [
  economyRoute,
  auditRoute,
  itemsRoute,
  ...catalogRoutes.filter((r) => r.path !== 'catalogs/monsters' && r.path !== 'catalogs/faces'),
  logsRoute,
  ...itemHistoryRoutes,
  chatRoute,
  ...mapsRoutes,
  ...npcDialogsRoutes,
  mapEditRoute,
  ...liveRoutes,
]
