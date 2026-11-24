import { Badge, Group, Stack, Text, Title } from '@mantine/core'
import { useTranslation } from 'react-i18next'
import { useMe } from './auth'

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

// Placeholder until the dashboard task replaces it
export function AdminHome() {
  const { t } = useTranslation()
  const me = useMe().data
  return (
    <Stack>
      <Title order={2}>{t('home.welcome', { name: me?.username ?? '' })}</Title>
      <Text c="dimmed">{t(`authority.${me?.authority ?? 1}`)}</Text>
      <Group gap="xs">
        {me?.permissions.map((p) => (
          <Badge key={p} variant="light">
            {t(`permission.${p}`)}
          </Badge>
        ))}
      </Group>
    </Stack>
  )
}
