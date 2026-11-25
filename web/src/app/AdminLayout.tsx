import { AppShell, Burger, Group, NavLink, ScrollArea } from '@mantine/core'
import { useDisclosure } from '@mantine/hooks'
import { Fragment, Suspense } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, NavLink as RouterNavLink, Outlet, useLocation } from 'react-router'
import { Panel } from '../theme/Panel'
import classes from './AdminLayout.module.css'
import { can, useMe } from './auth'
import { navGroups, visibleNav } from './feature'
import { PageLoader } from './Guards'
import { languages, setLanguage } from './i18n'
import { featureRoutes } from './routes'
import { UserMenu } from './UserMenu'

export function AdminLayout() {
  const { t, i18n } = useTranslation()
  const [opened, { toggle, close }] = useDisclosure()
  const me = useMe().data
  const location = useLocation()
  const entries = visibleNav(featureRoutes, 'admin', (p) => can(me, p))
  const current = i18n.resolvedLanguage ?? i18n.language

  return (
    <AppShell
      header={{ height: 50 }}
      navbar={{ width: 230, breakpoint: 'sm', collapsed: { mobile: !opened } }}
      padding={14}
    >
      <AppShell.Header className={classes.header}>
        <Group h="100%" px="md" justify="space-between" wrap="nowrap">
          <Group wrap="nowrap" gap="sm" align="center">
            <Burger opened={opened} onClick={toggle} hiddenFrom="sm" size="sm" className={classes.burger} />
            <Link to="/admin" className={classes.logo}>
              Fetih
            </Link>
            <span className={classes.subtitle}>{t('shell.adminPanel')}</span>
          </Group>
          <Group gap={10} wrap="nowrap" className={classes.links}>
            <Group gap={10} wrap="nowrap" visibleFrom="sm">
              <Link to="/">{t('shell.backToSite')}</Link>
              {'|'}
              {languages.map((l, i) => (
                <Fragment key={l}>
                  {i > 0 && '/'}
                  <button type="button" onClick={() => setLanguage(l)} aria-pressed={l === current} style={l === current ? { color: '#fff', fontWeight: 700 } : undefined}>
                    {l.toUpperCase()}
                  </button>
                </Fragment>
              ))}
            </Group>
            <UserMenu />
          </Group>
        </Group>
      </AppShell.Header>

      <AppShell.Navbar className={classes.navbar}>
        <ScrollArea>
          <Panel title={t('shell.menu')} icon="icons/items/711394.png" p={0} py={4}>
            <NavLink
              component={RouterNavLink}
              to="/admin"
              end
              label={`» ${t('nav.home')}`}
              active={location.pathname === '/admin'}
              onClick={close}
            />
            {navGroups.map((group) => {
              const items = entries.filter((e) => (e.nav?.group ?? 'system') === group)
              if (items.length === 0) return null
              return (
                <div key={group}>
                  <div className={classes.group}>{t(`nav.${group}`)}</div>
                  {items.map((item) => {
                    const to = `/admin/${item.path}`
                    return (
                      <NavLink
                        key={item.path}
                        component={RouterNavLink}
                        to={to}
                        label={`» ${t(item.nav!.label)}`}
                        active={location.pathname.startsWith(to)}
                        onClick={close}
                      />
                    )
                  })}
                </div>
              )
            })}
          </Panel>
        </ScrollArea>
      </AppShell.Navbar>

      <AppShell.Main>
        <div className={classes.content}>
          <Suspense fallback={<PageLoader />}>
            <Outlet />
          </Suspense>
        </div>
      </AppShell.Main>
    </AppShell>
  )
}
