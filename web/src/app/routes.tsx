import type { FeatureRoute } from './feature'
import { auditRoute } from '../features/audit'
import { catalogRoutes } from '../features/catalogs'
import { broadcastsRoute } from '../features/broadcasts'
import { changesRoute } from '../features/changes'
import { dataRoute } from '../features/data'
import { healthRoute } from '../features/health'
import { itemsRoute } from '../features/items'
import { myAccountRoute, registerRoute } from '../features/public/account'
import { publicSiteRoutes } from '../features/public/site'
import { publicHeatmapRoute } from '../features/public/heatmap'
import { logsRoute } from '../features/logs'
import { itemHistoryRoutes } from '../features/itemhistory'
import { chatRoute } from '../features/chat'
import { socialRoute } from '../features/social'
import { pkRoute } from '../features/pk'
import { mapsRoutes } from '../features/maps'
import { mapEditRoute } from '../features/maps/edit'
import { playersRoutes } from '../features/players'
import { monstersRoute } from '../features/monsters'
import { integrityRoute } from '../features/integrity'
import { liveRoutes } from '../features/live'
import { looksRoute } from '../features/looks'
import { packetsRoute } from '../features/packets'
import { notificationsRoute } from '../features/notifications'
import { serverRoute } from '../features/server'

// Every feature page, one line each. Admin pages show in the sidebar by their nav group.
export const featureRoutes: FeatureRoute[] = [
  healthRoute,
  broadcastsRoute,
  auditRoute,
  itemsRoute,
  ...catalogRoutes,
  registerRoute,
  myAccountRoute,
  ...publicSiteRoutes,
  publicHeatmapRoute,
  logsRoute,
  ...itemHistoryRoutes,
  chatRoute,
  changesRoute,
  socialRoute,
  pkRoute,
  ...mapsRoutes,
  mapEditRoute,
  ...playersRoutes,
  monstersRoute,
  dataRoute,
  integrityRoute,
  ...liveRoutes,
  looksRoute,
  packetsRoute,
  notificationsRoute,
  serverRoute,
]
