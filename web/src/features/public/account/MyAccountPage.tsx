import { Badge, Button, Group, Paper, PasswordInput, SimpleGrid, Stack, Table, Text, Title } from '@mantine/core'
import { useForm } from '@mantine/form'
import { notifications } from '@mantine/notifications'
import { useTranslation } from 'react-i18next'
import { ErrorAlert } from '../../../app/ErrorAlert'
import { PageLoader, RequirePermission } from '../../../app/Guards'
import { AccountErrorAlert } from './AccountErrorAlert'
import { useChangePassword, useMyAccount, usePublicAccountConfig } from './api'
import { classKey, isValidPassword } from './validation'

function formatDateTime(value: string, locale: string) {
  return new Date(value).toLocaleString(locale)
}

function MyAccountContent() {
  const { t, i18n } = useTranslation(['publicAccount', 'common'])
  const profile = useMyAccount()
  const config = usePublicAccountConfig()
  const changePassword = useChangePassword()

  const minPassword = config.data?.minPasswordLength ?? 6
  const maxPassword = config.data?.maxPasswordLength ?? 16

  const form = useForm({
    initialValues: { currentPassword: '', newPassword: '', confirmPassword: '' },
    validate: {
      currentPassword: (v) => (v ? null : t('publicAccount:validation.required')),
      newPassword: (v) =>
        !v
          ? t('publicAccount:validation.required')
          : isValidPassword(v, minPassword, maxPassword)
            ? null
            : t('publicAccount:validation.invalidPassword', { min: minPassword, max: maxPassword }),
      confirmPassword: (v, values) =>
        !v
          ? t('publicAccount:validation.required')
          : v === values.newPassword
            ? null
            : t('publicAccount:validation.passwordMismatch'),
    },
  })

  if (profile.isPending) return <PageLoader />
  if (profile.error) return <ErrorAlert error={profile.error} />

  const data = profile.data

  return (
    <Stack gap="lg" py="md">
      <Title order={2}>{t('publicAccount:account.title')}</Title>

      <SimpleGrid cols={{ base: 1, md: 2 }} spacing="lg">
        <Paper withBorder p="md">
          <Stack gap="xs">
            <Title order={4}>{t('publicAccount:account.overview')}</Title>
            <Group justify="space-between">
              <Text c="dimmed" size="sm">
                {t('publicAccount:account.email')}
              </Text>
              <Text fw={500} size="sm">
                {data.username}
              </Text>
            </Group>
            <Group justify="space-between">
              <Text c="dimmed" size="sm">
                {t('publicAccount:account.role')}
              </Text>
              <Badge variant="light">{t(`common:authority.${data.authority}`)}</Badge>
            </Group>
            <Group justify="space-between">
              <Text c="dimmed" size="sm">
                {t('publicAccount:account.registered')}
              </Text>
              <Text size="sm">{formatDateTime(data.registered, i18n.language)}</Text>
            </Group>
          </Stack>
        </Paper>

        <Paper withBorder p="md">
          <form
            onSubmit={form.onSubmit((values) =>
              changePassword.mutate(
                { currentPassword: values.currentPassword, newPassword: values.newPassword },
                {
                  onSuccess: () => {
                    form.reset()
                    notifications.show({ color: 'green', message: t('publicAccount:account.password.updated') })
                  },
                },
              ),
            )}
          >
            <Stack gap="sm">
              <Title order={4}>{t('publicAccount:account.password.title')}</Title>
              <AccountErrorAlert error={changePassword.error} />
              <PasswordInput
                label={t('publicAccount:account.password.current')}
                autoComplete="current-password"
                maxLength={maxPassword}
                {...form.getInputProps('currentPassword')}
              />
              <PasswordInput
                label={t('publicAccount:account.password.next')}
                autoComplete="new-password"
                maxLength={maxPassword}
                {...form.getInputProps('newPassword')}
              />
              <PasswordInput
                label={t('publicAccount:account.password.confirm')}
                autoComplete="new-password"
                maxLength={maxPassword}
                {...form.getInputProps('confirmPassword')}
              />
              <Button type="submit" loading={changePassword.isPending} w="fit-content">
                {t('publicAccount:account.password.submit')}
              </Button>
            </Stack>
          </form>
        </Paper>
      </SimpleGrid>

      <Paper withBorder p="md">
        <Stack gap="sm">
          <Title order={4}>{t('publicAccount:account.characters.title')}</Title>
          {data.characters.length === 0 ? (
            <Text size="sm" c="dimmed">
              {t('publicAccount:account.characters.empty')}
            </Text>
          ) : (
            <Table.ScrollContainer minWidth={640}>
              <Table striped highlightOnHover>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>{t('publicAccount:account.characters.name')}</Table.Th>
                    <Table.Th>{t('publicAccount:account.characters.class')}</Table.Th>
                    <Table.Th>{t('publicAccount:account.characters.level')}</Table.Th>
                    <Table.Th>{t('publicAccount:account.characters.rebirths')}</Table.Th>
                    <Table.Th>{t('publicAccount:account.characters.map')}</Table.Th>
                    <Table.Th>{t('publicAccount:account.characters.silver')}</Table.Th>
                    <Table.Th>{t('publicAccount:account.characters.cps')}</Table.Th>
                    <Table.Th>{t('publicAccount:account.characters.vip')}</Table.Th>
                    <Table.Th>{t('publicAccount:account.characters.spouse')}</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {data.characters.map((ch) => (
                    <Table.Tr key={ch.characterId}>
                      <Table.Td fw={500}>{ch.name}</Table.Td>
                      <Table.Td>{t(`publicAccount:classes.${classKey(ch.currentClass)}`, { id: ch.currentClass })}</Table.Td>
                      <Table.Td>{ch.level}</Table.Td>
                      <Table.Td>{ch.rebirths}</Table.Td>
                      <Table.Td>{ch.mapId}</Table.Td>
                      <Table.Td>{ch.silver.toLocaleString()}</Table.Td>
                      <Table.Td>{ch.jewels.toLocaleString()}</Table.Td>
                      <Table.Td>{ch.vipLevel}</Table.Td>
                      <Table.Td>{ch.spouse}</Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </Table.ScrollContainer>
          )}
        </Stack>
      </Paper>

      <Paper withBorder p="md">
        <Stack gap="sm">
          <Title order={4}>{t('publicAccount:account.logins.title')}</Title>
          {data.logins.length === 0 ? (
            <Text size="sm" c="dimmed">
              {t('publicAccount:account.logins.empty')}
            </Text>
          ) : (
            <Table striped highlightOnHover>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('publicAccount:account.logins.time')}</Table.Th>
                  <Table.Th>{t('publicAccount:account.logins.ip')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {data.logins.map((login, index) => (
                  <Table.Tr key={`${login.timestamp}-${index}`}>
                    <Table.Td>{formatDateTime(login.timestamp, i18n.language)}</Table.Td>
                    <Table.Td>{login.ipAddress}</Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          )}
        </Stack>
      </Paper>
    </Stack>
  )
}

export default function MyAccountPage() {
  return (
    <RequirePermission>
      <MyAccountContent />
    </RequirePermission>
  )
}
