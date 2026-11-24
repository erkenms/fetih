import type { FeatureRoute } from '../../../app/feature'

export const registerRoute: FeatureRoute = {
  area: 'public',
  path: 'register',
  page: () => import('./RegisterPage'),
  nav: { label: 'publicAccount:register.nav' },
}

export const myAccountRoute: FeatureRoute = {
  area: 'public',
  path: 'account',
  page: () => import('./MyAccountPage'),
  nav: { label: 'publicAccount:account.nav' },
}
