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
import { logsRoute } from '../features/logs'
import { socialRoute } from '../features/social'
import { mapsRoutes } from '../features/maps'
import { accountsRoutes } from '../features/accounts'
import { charactersRoutes } from '../features/characters'
import { monstersRoute } from '../features/monsters'
import { integrityRoute } from '../features/integrity'

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
  logsRoute,
  changesRoute,
  socialRoute,
  ...mapsRoutes,
  ...accountsRoutes,
  ...charactersRoutes,
  monstersRoute,
  dataRoute,
  integrityRoute,
]
