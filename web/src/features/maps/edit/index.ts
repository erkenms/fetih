import type { FeatureRoute } from '../../../app/feature'

export const mapEditRoute: FeatureRoute = {
  area: 'admin',
  path: 'maps/:id/edit',
  page: () => import('./MapEditorPage'),
  permission: 'Studio',
}
