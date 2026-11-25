import { useQuery } from '@tanstack/react-query'
import { Fragment, Suspense } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, NavLink, Outlet } from 'react-router'
import { api, unwrap } from '../api/client'
import { can, useMe } from './auth'
import { useBarePage, visibleNav } from './feature'
import { PageLoader } from './Guards'
import { languages, setLanguage } from './i18n'
import classes from './PublicLayout.module.css'
import { featureRoutes } from './routes'
import { UserMenu } from './UserMenu'

export function PublicLayout() {
  const { t, i18n } = useTranslation()
  const me = useMe().data
  const entries = visibleNav(featureRoutes, 'public', (p) => can(me, p))
  // The same query as the home page, so the two share one request
  const home = useQuery({ queryKey: ['public', 'home'], queryFn: () => unwrap(api.GET('/api/public/home')) }).data
  const current = i18n.resolvedLanguage ?? i18n.language
  const bare = useBarePage()

  return (
    <div className={classes.page}>
      <div className={classes.wrap}>
        <div className={classes.topline}>
          <span>
            {home &&
              (home.gameOnline ? (
                <>
                  <span className={classes.on}>{t('shell.serverOnline')}</span>
                  {home.onlinePlayers != null && <> · {t('shell.playersOnline', { count: home.onlinePlayers })}</>}
                </>
              ) : (
                <span className={classes.off}>{t('shell.serverOffline')}</span>
              ))}
          </span>
          <span style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            {languages.map((l, i) => (
              <Fragment key={l}>
                {i > 0 && '|'}
                <button type="button" onClick={() => setLanguage(l)} aria-pressed={l === current} style={l === current ? { color: '#fff0c0' } : undefined}>
                  {l === 'tr' ? 'Türkçe' : 'English'}
                </button>
              </Fragment>
            ))}
            <UserMenu />
          </span>
        </div>

        <header className={classes.header}>
          <Link to="/" className={classes.logo}>
            <span className={classes.logoName}>Fetih</span>
            <span className={classes.logoSub}>Online</span>
          </Link>
          <div className={classes.tagline}>{t('publicSite:home.subtitle')}</div>
        </header>

        <nav className={classes.nav} aria-label={t('shell.menu')}>
          <NavLink to="/" end>
            {t('shell.home')}
          </NavLink>
          {entries.map((e) => (
            <NavLink key={e.path} to={`/${e.path}`}>
              {t(e.nav!.label)}
            </NavLink>
          ))}
        </nav>

        <main className={bare ? undefined : classes.content}>
          <Suspense fallback={<PageLoader />}>
            <Outlet />
          </Suspense>
        </main>

        <footer className={classes.footer}>
          <Link to="/">{t('shell.home')}</Link>
          {entries.map((e) => (
            <Fragment key={e.path}>
              {' | '}
              <Link to={`/${e.path}`}>{t(e.nav!.label)}</Link>
            </Fragment>
          ))}
          <br />
          {t('shell.copyright', { year: new Date().getFullYear() })}
        </footer>
      </div>
    </div>
  )
}
