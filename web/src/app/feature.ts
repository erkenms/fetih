import type { ComponentType } from 'react'
import { useMatches } from 'react-router'
import type { Permission } from '../api/client'

// Groups of the admin sidebar, in display order. Labels are common:nav.<group>.
export const navGroups = [
  'overview',
  'world',
  'players',
  'social',
  'studio',
  'catalogs',
  'logs',
  'system',
] as const
export type NavGroup = (typeof navGroups)[number]

// What a feature registers in routes.tsx: one page, where it lives, and who may open it.
export interface FeatureRoute {
  // 'admin' pages live under /admin with the sidebar; 'public' pages under / with the site header
  area: 'admin' | 'public'
  // Relative to the area, like 'audit' or 'characters/:id'
  path: string
  page: () => Promise<{ default: ComponentType }>
  permission?: Permission
  // The page lays out its own Panels on the dark page; others get the layout's parchment frame
  bare?: boolean
  // Pages with a nav entry show in the sidebar (admin) or header (public)
  nav?: {
    // An i18n key with its namespace, like 'audit:title'
    label: string
    icon?: ComponentType<{ size?: number; stroke?: number }>
    group?: NavGroup
  }
}

// Whether the open page is bare (see FeatureRoute.bare)
export function useBarePage() {
  return useMatches().some((m) => (m.handle as { bare?: boolean } | undefined)?.bare)
}

export function visibleNav(routes: FeatureRoute[], area: FeatureRoute['area'], has: (p: Permission) => boolean) {
  return routes.filter((r) => r.area === area && r.nav && (!r.permission || has(r.permission)))
}
