import { Stack, Text, Title } from '@mantine/core'
import { lazy } from 'react'
import { useTranslation } from 'react-i18next'

// Placeholder until the public home page task replaces it
export function PublicHome() {
  const { t } = useTranslation()
  return (
    <Stack py="xl">
      <Title>fetih</Title>
      <Text c="dimmed">{t('home.tagline')}</Text>
    </Stack>
  )
}

// The admin home is the dashboard
export const AdminHome = lazy(() => import('../features/dashboard/DashboardPage'))
