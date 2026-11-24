import { Anchor, AppShell, Burger, Group, NavLink, ScrollArea, Text } from '@mantine/core'
import { useDisclosure } from '@mantine/hooks'
import { IconHome } from '@tabler/icons-react'
import { Suspense } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, NavLink as RouterNavLink, Outlet, useLocation } from 'react-router'
import { can, useMe } from './auth'
import { ColorSchemeToggle } from './ColorSchemeToggle'
import { navGroups, visibleNav } from './feature'
import { PageLoader } from './Guards'
import { LanguageSwitcher } from './LanguageSwitcher'
import { featureRoutes } from './routes'
import { UserMenu } from './UserMenu'

export function AdminLayout() {
  const { t } = useTranslation()
  const [opened, { toggle, close }] = useDisclosure()
  const me = useMe().data
  const location = useLocation()
  const entries = visibleNav(featureRoutes, 'admin', (p) => can(me, p))

  return (
    <AppShell
      header={{ height: 56 }}
      navbar={{ width: 240, breakpoint: 'sm', collapsed: { mobile: !opened } }}
      padding="md"
    >
      <AppShell.Header>
        <Group h="100%" px="md" justify="space-between" wrap="nowrap">
          <Group wrap="nowrap">
            <Burger opened={opened} onClick={toggle} hiddenFrom="sm" size="sm" />
            <Anchor component={Link} to="/admin" fw={700} fz="lg" c="inherit" underline="never">
              fetih
            </Anchor>
            <Text c="dimmed" size="sm" visibleFrom="xs">
              {t('shell.adminPanel')}
            </Text>
          </Group>
          <Group gap="xs" wrap="nowrap">
            <LanguageSwitcher />
            <ColorSchemeToggle />
            <UserMenu />
          </Group>
        </Group>
      </AppShell.Header>

      <AppShell.Navbar p="xs">
        <ScrollArea>
          <NavLink
            component={RouterNavLink}
            to="/admin"
            end
            label={t('nav.home')}
            leftSection={<IconHome size={18} stroke={1.5} />}
            active={location.pathname === '/admin'}
            onClick={close}
          />
          {navGroups.map((group) => {
            const items = entries.filter((e) => (e.nav?.group ?? 'system') === group)
            if (items.length === 0) return null
            return (
              <div key={group}>
                <Text size="xs" fw={600} c="dimmed" tt="uppercase" mt="md" mb={4} px="xs">
                  {t(`nav.${group}`)}
                </Text>
                {items.map((item) => {
                  const Icon = item.nav?.icon
                  const to = `/admin/${item.path}`
                  return (
                    <NavLink
                      key={item.path}
                      component={RouterNavLink}
                      to={to}
                      label={t(item.nav!.label)}
                      leftSection={Icon ? <Icon size={18} stroke={1.5} /> : undefined}
                      active={location.pathname.startsWith(to)}
                      onClick={close}
                    />
                  )
                })}
              </div>
            )
          })}
        </ScrollArea>
      </AppShell.Navbar>

      <AppShell.Main>
        <Suspense fallback={<PageLoader />}>
          <Outlet />
        </Suspense>
      </AppShell.Main>
    </AppShell>
  )
}
