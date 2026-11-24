import type { FeatureRoute } from './feature'
import { auditRoute } from '../features/audit'
import { catalogRoutes } from '../features/catalogs'
import { healthRoute } from '../features/health'
import { itemsRoute } from '../features/items'
import { myAccountRoute, registerRoute } from '../features/public/account'
import { publicSiteRoutes } from '../features/public/site'

// Every feature page, one line each. Admin pages show in the sidebar by their nav group.
export const featureRoutes: FeatureRoute[] = [
  healthRoute,
  auditRoute,
  itemsRoute,
  ...catalogRoutes,
  registerRoute,
  myAccountRoute,
  ...publicSiteRoutes,
]
