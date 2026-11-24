import { Anchor, AppShell, Container, Group } from '@mantine/core'
import { Suspense } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, Outlet } from 'react-router'
import { can, useMe } from './auth'
import { ColorSchemeToggle } from './ColorSchemeToggle'
import { visibleNav } from './feature'
import { PageLoader } from './Guards'
import { LanguageSwitcher } from './LanguageSwitcher'
import { featureRoutes } from './routes'
import { UserMenu } from './UserMenu'

export function PublicLayout() {
  const { t } = useTranslation()
  const me = useMe().data
  const entries = visibleNav(featureRoutes, 'public', (p) => can(me, p))

  return (
    <AppShell header={{ height: 60 }} padding="md">
      <AppShell.Header>
        <Container size="lg" h="100%">
          <Group h="100%" justify="space-between" wrap="nowrap">
            <Group wrap="nowrap" gap="lg">
              <Anchor component={Link} to="/" fw={700} fz="xl" c="inherit" underline="never">
                fetih
              </Anchor>
              <Group gap="md" visibleFrom="sm">
                {entries.map((e) => (
                  <Anchor key={e.path} component={Link} to={`/${e.path}`} c="inherit">
                    {t(e.nav!.label)}
                  </Anchor>
                ))}
              </Group>
            </Group>
            <Group gap="xs" wrap="nowrap">
              <LanguageSwitcher />
              <ColorSchemeToggle />
              <UserMenu />
            </Group>
          </Group>
        </Container>
      </AppShell.Header>
      <AppShell.Main>
        <Container size="lg">
          <Suspense fallback={<PageLoader />}>
            <Outlet />
          </Suspense>
        </Container>
      </AppShell.Main>
    </AppShell>
  )
}
