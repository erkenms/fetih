import { Button, Center, Loader, Stack, Text, Title } from '@mantine/core'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, Navigate, useLocation } from 'react-router'
import type { Permission } from '../api/client'
import { can, useMe } from './auth'
import { ErrorAlert } from './ErrorAlert'

// Hides a page from accounts without the permission. The backend checks it again on every
// call; this only keeps people from pages that would fail.
export function RequirePermission({ permission, children }: { permission?: Permission; children: ReactNode }) {
  const me = useMe()
  const location = useLocation()

  if (me.isPending)
    return (
      <Center h="50vh">
        <Loader />
      </Center>
    )
  if (me.error) return <ErrorAlert error={me.error} />
  if (!me.data) return <Navigate to={`/login?next=${encodeURIComponent(location.pathname + location.search)}`} replace />
  if (permission && !can(me.data, permission)) return <Forbidden />
  return children
}

export function Forbidden() {
  const { t } = useTranslation()
  return (
    <Center h="50vh">
      <Stack align="center">
        <Title order={2}>{t('forbidden.title')}</Title>
        <Text c="dimmed">{t('forbidden.text')}</Text>
        <Button component={Link} to="/" variant="default">
          {t('shell.home')}
        </Button>
      </Stack>
    </Center>
  )
}

export function NotFound() {
  const { t } = useTranslation()
  return (
    <Center h="50vh">
      <Stack align="center">
        <Title order={2}>{t('notFound.title')}</Title>
        <Button component={Link} to="/" variant="default">
          {t('shell.home')}
        </Button>
      </Stack>
    </Center>
  )
}

export function PageLoader() {
  return (
    <Center h="30vh">
      <Loader />
    </Center>
  )
}
