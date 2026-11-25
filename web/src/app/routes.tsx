import type { FeatureRoute } from './feature'
import { auditRoute } from '../features/audit'
import { catalogRoutes } from '../features/catalogs'
import { broadcastsRoute } from '../features/broadcasts'
import { changesRoute } from '../features/changes'
import { healthRoute } from '../features/health'
import { itemsRoute } from '../features/items'
import { myAccountRoute, registerRoute } from '../features/public/account'
import { publicSiteRoutes } from '../features/public/site'
import { logsRoute } from '../features/logs'
import { socialRoute } from '../features/social'

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
]
