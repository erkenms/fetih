import { IconFlame, IconMap, IconMoodSmile, IconShoppingBag, IconSkull } from '@tabler/icons-react'
import type { FeatureRoute } from '../../app/feature'

export const catalogRoutes: FeatureRoute[] = [
  {
    area: 'admin',
    path: 'catalogs/monsters',
    page: () => import('./MonstersCatalogPage'),
    permission: 'ViewCatalogs',
    nav: { label: 'catalogs:nav.monsters', icon: IconSkull, group: 'catalogs' },
  },
  {
    area: 'admin',
    path: 'catalogs/spells',
    page: () => import('./SpellsCatalogPage'),
    permission: 'ViewCatalogs',
    nav: { label: 'catalogs:nav.spells', icon: IconFlame, group: 'catalogs' },
  },
  {
    area: 'admin',
    path: 'catalogs/maps',
    page: () => import('./MapsCatalogPage'),
    permission: 'ViewCatalogs',
    nav: { label: 'catalogs:nav.maps', icon: IconMap, group: 'catalogs' },
  },
  {
    area: 'admin',
    path: 'catalogs/faces',
    page: () => import('./FacesCatalogPage'),
    permission: 'ViewCatalogs',
    nav: { label: 'catalogs:nav.faces', icon: IconMoodSmile, group: 'catalogs' },
  },
  {
    area: 'admin',
    path: 'catalogs/shops',
    page: () => import('./ShopsCatalogPage'),
    permission: 'ViewCatalogs',
    nav: { label: 'catalogs:nav.shops', icon: IconShoppingBag, group: 'catalogs' },
  },
]
