import type { FeatureRoute } from './feature'
import { auditRoute } from '../features/audit'
import { healthRoute } from '../features/health'

// Every feature page, one line each. Admin pages show in the sidebar by their nav group.
export const featureRoutes: FeatureRoute[] = [
  healthRoute,
  auditRoute,
]
