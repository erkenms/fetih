import type { FeatureRoute } from '../../../app/feature'

export const publicSiteRoutes: FeatureRoute[] = [
  {
    area: 'public',
    path: '',
    page: () => import('./HomePage'),
    bare: true,
  },
  {
    area: 'public',
    path: 'rankings',
    page: () => import('./RankingsPage'),
    bare: true,
    nav: { label: 'publicSite:nav.rankings' },
  },
  {
    area: 'public',
    path: 'rankings/:kind',
    page: () => import('./RankingsPage'),
    bare: true,
  },
  {
    area: 'public',
    path: 'characters/:id',
    page: () => import('./CharacterPage'),
  },
  {
    area: 'public',
    path: 'guilds/:id',
    page: () => import('./GuildPage'),
  },
  {
    area: 'public',
    path: 'news',
    page: () => import('./NewsPage'),
    nav: { label: 'publicSite:nav.news' },
  },
  {
    area: 'public',
    path: 'download',
    page: () => import('./DownloadPage'),
    nav: { label: 'publicSite:nav.download' },
  },
]
