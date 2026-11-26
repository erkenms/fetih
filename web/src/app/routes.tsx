import type { FeatureRoute } from './feature'
import { auditRoute } from '../features/audit'
import { catalogRoutes } from '../features/catalogs'
import { changesRoute } from '../features/changes'
import { dataRoute } from '../features/data'
import { healthRoute } from '../features/health'
import { economyRoute } from '../features/economy'
import { itemsRoute } from '../features/items'
import { logsRoute } from '../features/logs'
import { itemHistoryRoutes } from '../features/itemhistory'
import { chatRoute } from '../features/chat'
import { socialRoute } from '../features/social'
import { mapsRoutes } from '../features/maps'
import { npcDialogsRoutes } from '../features/npcdialogs'
import { mapEditRoute } from '../features/maps/edit'
import { monstersRoute } from '../features/monsters'
import { integrityRoute } from '../features/integrity'
import { liveRoutes } from '../features/live'
import { looksRoute } from '../features/looks'
import { serverRoute } from '../features/server'

// Every feature page, one line each. Admin pages show in the sidebar by their nav group.
export const featureRoutes: FeatureRoute[] = [
  healthRoute,
  economyRoute,
  auditRoute,
  itemsRoute,
  ...catalogRoutes,
  logsRoute,
  ...itemHistoryRoutes,
  chatRoute,
  changesRoute,
  socialRoute,
  ...mapsRoutes,
  ...npcDialogsRoutes,
  mapEditRoute,
  monstersRoute,
  dataRoute,
  integrityRoute,
  ...liveRoutes,
  looksRoute,
  serverRoute,
]
