import {
  Anchor,
  Badge,
  Button,
  Card,
  Group,
  Loader,
  Modal,
  PasswordInput,
  ScrollArea,
  Select,
  SimpleGrid,
  Stack,
  Table,
  Tabs,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import {
  IconArrowLeft,
  IconBan,
  IconCheck,
  IconKey,
  IconShield,
  IconUserCog,
} from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router'
import { api, unwrap } from '../../api/client'
import { can, useMe } from '../../app/auth'
import { ErrorAlert, useErrorText } from '../../app/ErrorAlert'
import { AccountTimelineTab } from '../logs/AccountTimelineTab'
import {
  AUTHORITY_IDS,
  NON_BAN_STATUS_IDS,
  accountStatusColor,
  authorityColor,
  isAccountBanned,
} from './accountMeta'

type ActionModal = 'ban' | 'unban' | 'status' | 'authority' | 'password' | null

export default function AccountDetailPage() {
  const { id } = useParams()
  const accountId = Number(id)
  const { t, i18n } = useTranslation(['accounts', 'common'])
  const formatError = useErrorText('accounts')
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const me = useMe().data

  const [modal, setModal] = useState<ActionModal>(null)
  const [reason, setReason] = useState('')
  const [statusId, setStatusId] = useState<string | null>(null)
  const [authorityId, setAuthorityId] = useState<string | null>(null)
  const [newPassword, setNewPassword] = useState('')
  const [actionError, setActionError] = useState<unknown>(null)

  const detail = useQuery({
    queryKey: ['account', accountId],
    queryFn: () => unwrap(api.GET('/api/accounts/{id}', { params: { path: { id: accountId } } })),
    enabled: Number.isFinite(accountId) && accountId > 0,
  })

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['account', accountId] })
    void queryClient.invalidateQueries({ queryKey: ['accounts'] })
  }

  const banMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST('/api/accounts/{id}/ban', {
          params: { path: { id: accountId } },
          body: { reason: reason.trim() || null },
        }),
      ),
    onSuccess: () => {
      notifications.show({ message: t('accounts:actions.banSuccess'), color: 'green' })
      setModal(null)
      setReason('')
      refresh()
    },
    onError: setActionError,
  })

  const unbanMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST('/api/accounts/{id}/unban', {
          params: { path: { id: accountId } },
          body: { reason: reason.trim() || null },
        }),
      ),
    onSuccess: () => {
      notifications.show({ message: t('accounts:actions.unbanSuccess'), color: 'green' })
      setModal(null)
      setReason('')
      refresh()
    },
    onError: setActionError,
  })

  const statusMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST('/api/accounts/{id}/status', {
          params: { path: { id: accountId } },
          body: { statusId: Number(statusId), reason: reason.trim() || null },
        }),
      ),
    onSuccess: () => {
      notifications.show({ message: t('accounts:actions.statusSuccess'), color: 'green' })
      setModal(null)
      setReason('')
      refresh()
    },
    onError: setActionError,
  })

  const authorityMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST('/api/accounts/{id}/authority', {
          params: { path: { id: accountId } },
          body: { authorityId: Number(authorityId) },
        }),
      ),
    onSuccess: () => {
      notifications.show({ message: t('accounts:actions.authoritySuccess'), color: 'green' })
      setModal(null)
      refresh()
    },
    onError: setActionError,
  })

  const passwordMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST('/api/accounts/{id}/reset-password', {
          params: { path: { id: accountId } },
          body: { newPassword },
        }),
      ),
    onSuccess: () => {
      notifications.show({ message: t('accounts:actions.resetPasswordSuccess'), color: 'green' })
      setModal(null)
      setNewPassword('')
    },
    onError: setActionError,
  })

  if (detail.isLoading) {
    return (
      <Group justify="center" py="xl">
        <Loader />
      </Group>
    )
  }

  if (detail.error || !detail.data) {
    return (
      <Stack>
        <Anchor component={Link} to="/admin/accounts" size="sm">
          <Group gap={4}>
            <IconArrowLeft size={16} />
            {t('accounts:detail.back')}
          </Group>
        </Anchor>
        <ErrorAlert error={detail.error} />
      </Stack>
    )
  }

  const account = detail.data
  const banned = isAccountBanned(account.statusId)
  const canBan = can(me, 'ManageCharacters')
  const canManage = can(me, 'ManageAccounts')
  const canViewLogs = can(me, 'ViewLogs')

  const openModal = (next: ActionModal) => {
    setActionError(null)
    setReason('')
    setNewPassword('')
    if (next === 'status') {
      setStatusId(String(banned ? 2 : account.statusId))
    } else if (next === 'authority') {
      setAuthorityId(String(account.authorityId))
    }
    setModal(next)
  }

  return (
    <Stack>
      <Anchor component={Link} to="/admin/accounts" size="sm">
        <Group gap={4}>
          <IconArrowLeft size={16} />
          {t('accounts:detail.back')}
        </Group>
      </Anchor>

      <Group justify="space-between" align="flex-start" wrap="wrap">
        <Stack gap={4}>
          <Group gap="sm">
            <Title order={2}>{account.username}</Title>
            <Text c="dimmed" size="sm">
              #{account.accountId}
            </Text>
            <Badge color={accountStatusColor(account.statusId)}>
              {t(`accounts:status.${account.statusId}` as never)}
            </Badge>
            <Badge variant="light" color={authorityColor(account.authorityId)}>
              {t(`common:authority.${account.authorityId}` as never)}
            </Badge>
          </Group>
          <Text size="sm" c="dimmed">
            {t('accounts:detail.registered')}: {new Date(account.registered).toLocaleString(i18n.language)}
          </Text>
        </Stack>

        <Group gap="xs" wrap="wrap">
          {canBan && !banned && (
            <Button color="red" variant="light" leftSection={<IconBan size={16} />} onClick={() => openModal('ban')}>
              {t('accounts:actions.ban')}
            </Button>
          )}
          {canBan && banned && (
            <Button color="green" variant="light" leftSection={<IconCheck size={16} />} onClick={() => openModal('unban')}>
              {t('accounts:actions.unban')}
            </Button>
          )}
          {canManage && (
            <>
              <Button variant="light" leftSection={<IconUserCog size={16} />} onClick={() => openModal('status')}>
                {t('accounts:actions.status')}
              </Button>
              <Button variant="light" leftSection={<IconShield size={16} />} onClick={() => openModal('authority')}>
                {t('accounts:actions.authority')}
              </Button>
              <Button variant="light" color="orange" leftSection={<IconKey size={16} />} onClick={() => openModal('password')}>
                {t('accounts:actions.resetPassword')}
              </Button>
            </>
          )}
        </Group>
      </Group>

      <Tabs defaultValue="overview">
        <Tabs.List>
          <Tabs.Tab value="overview">{t('accounts:detail.overview')}</Tabs.Tab>
          <Tabs.Tab value="logins">
            {t('accounts:detail.logins')} ({account.logins.length})
          </Tabs.Tab>
          {canViewLogs && <Tabs.Tab value="timeline">{t('accounts:detail.timeline')}</Tabs.Tab>}
        </Tabs.List>

        <Tabs.Panel value="overview" pt="md">
          <Stack>
            <SimpleGrid cols={{ base: 1, sm: 3 }}>
              <Card withBorder>
                <Text size="xs" c="dimmed">
                  {t('accounts:detail.email')}
                </Text>
                <Text fw={500}>{account.email || '—'}</Text>
              </Card>
              <Card withBorder>
                <Text size="xs" c="dimmed">
                  {t('accounts:detail.name')}
                </Text>
                <Text fw={500}>{account.name || '—'}</Text>
              </Card>
              <Card withBorder>
                <Text size="xs" c="dimmed">
                  {t('accounts:detail.lastIp')}
                </Text>
                {account.ipAddress ? (
                  <Anchor
                    component={Link}
                    to={`/admin/accounts?ip=${encodeURIComponent(account.ipAddress)}`}
                    fw={500}
                    title={t('accounts:detail.searchIp')}
                  >
                    {account.ipAddress}
                  </Anchor>
                ) : (
                  <Text fw={500}>—</Text>
                )}
              </Card>
            </SimpleGrid>

            <Card withBorder>
              <Title order={4} mb="sm">
                {t('accounts:detail.characters')} ({account.characters.length})
              </Title>
              {account.characters.length === 0 ? (
                <Text c="dimmed" size="sm">
                  {t('accounts:detail.noCharacters')}
                </Text>
              ) : (
                <ScrollArea>
                  <Table highlightOnHover striped>
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th>{t('accounts:column.name')}</Table.Th>
                        <Table.Th>{t('accounts:column.level')}</Table.Th>
                        <Table.Th>{t('accounts:column.map')}</Table.Th>
                        <Table.Th>{t('accounts:column.silver')}</Table.Th>
                        <Table.Th>{t('accounts:column.cp')}</Table.Th>
                        <Table.Th>{t('accounts:column.status')}</Table.Th>
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {account.characters.map((ch) => (
                        <Table.Tr
                          key={ch.characterId}
                          style={{ cursor: 'pointer' }}
                          onClick={() => navigate(`/admin/characters/${ch.characterId}`)}
                        >
                          <Table.Td>
                            <Text fw={600} size="sm">
                              {ch.name}
                            </Text>
                            <Text size="xs" c="dimmed">
                              #{ch.characterId}
                            </Text>
                          </Table.Td>
                          <Table.Td>
                            <Text size="sm">
                              {ch.level}
                              {ch.rebirths > 0 ? ` (RB ${ch.rebirths})` : ''}
                            </Text>
                          </Table.Td>
                          <Table.Td>
                            <Text size="sm">{ch.mapName ? `${ch.mapName} (#${ch.mapId})` : `#${ch.mapId}`}</Text>
                          </Table.Td>
                          <Table.Td>{ch.silver.toLocaleString(i18n.language)}</Table.Td>
                          <Table.Td>{ch.conquerPoints.toLocaleString(i18n.language)}</Table.Td>
                          <Table.Td>
                            <Badge size="sm" color={ch.online ? 'green' : 'gray'} variant="light">
                              {t(ch.online ? 'accounts:detail.online' : 'accounts:detail.offline')}
                            </Badge>
                          </Table.Td>
                        </Table.Tr>
                      ))}
                    </Table.Tbody>
                  </Table>
                </ScrollArea>
              )}
            </Card>

            <Card withBorder>
              <Title order={4} mb="sm">
                {t('accounts:detail.sameIp')} ({account.sameIpAccounts.length})
              </Title>
              {account.sameIpAccounts.length === 0 ? (
                <Text c="dimmed" size="sm">
                  {t('accounts:detail.noSameIp')}
                </Text>
              ) : (
                <ScrollArea>
                  <Table highlightOnHover striped>
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th>{t('accounts:column.id')}</Table.Th>
                        <Table.Th>{t('accounts:column.username')}</Table.Th>
                        <Table.Th>{t('accounts:column.status')}</Table.Th>
                        <Table.Th>{t('accounts:column.sharedIps')}</Table.Th>
                        <Table.Th>{t('accounts:column.registered')}</Table.Th>
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {account.sameIpAccounts.map((other) => (
                        <Table.Tr
                          key={other.accountId}
                          style={{ cursor: 'pointer' }}
                          onClick={() => navigate(`/admin/accounts/${other.accountId}`)}
                        >
                          <Table.Td>#{other.accountId}</Table.Td>
                          <Table.Td>
                            <Text fw={600} size="sm">
                              {other.username}
                            </Text>
                          </Table.Td>
                          <Table.Td>
                            <Badge size="sm" variant="light" color={accountStatusColor(other.statusId)}>
                              {t(`accounts:status.${other.statusId}` as never)}
                            </Badge>
                          </Table.Td>
                          <Table.Td>{other.sharedIps.join(', ')}</Table.Td>
                          <Table.Td>{new Date(other.registered).toLocaleDateString(i18n.language)}</Table.Td>
                        </Table.Tr>
                      ))}
                    </Table.Tbody>
                  </Table>
                </ScrollArea>
              )}
            </Card>
          </Stack>
        </Tabs.Panel>

        <Tabs.Panel value="logins" pt="md">
          <Card withBorder>
            {account.logins.length === 0 ? (
              <Text c="dimmed" size="sm">
                {t('accounts:detail.noLogins')}
              </Text>
            ) : (
              <ScrollArea>
                <Table striped>
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>{t('accounts:column.time')}</Table.Th>
                      <Table.Th>{t('accounts:column.ip')}</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {account.logins.map((entry, idx) => (
                      <Table.Tr key={`${entry.timestamp}-${idx}`}>
                        <Table.Td>{new Date(entry.timestamp).toLocaleString(i18n.language)}</Table.Td>
                        <Table.Td>
                          <Anchor
                            component={Link}
                            to={`/admin/accounts?ip=${encodeURIComponent(entry.ipAddress)}`}
                            title={t('accounts:detail.searchIp')}
                          >
                            {entry.ipAddress}
                          </Anchor>
                        </Table.Td>
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              </ScrollArea>
            )}
          </Card>
        </Tabs.Panel>

        {canViewLogs && (
          <Tabs.Panel value="timeline" pt="md">
            <AccountTimelineTab accountId={account.accountId} />
          </Tabs.Panel>
        )}
      </Tabs>

      <Modal
        opened={modal === 'ban' || modal === 'unban'}
        onClose={() => setModal(null)}
        title={t(
          modal === 'ban' ? 'accounts:actions.banTitle' : 'accounts:actions.unbanTitle',
          { username: account.username },
        )}
      >
        <Stack>
          {Boolean(actionError) && (
            <Text c="red" size="sm">
              {formatError(actionError)}
            </Text>
          )}
          <TextInput
            label={t('accounts:actions.reason')}
            value={reason}
            onChange={(e) => setReason(e.currentTarget.value)}
          />
          <Group justify="flex-end">
            <Button variant="subtle" onClick={() => setModal(null)}>
              {t('accounts:actions.cancel')}
            </Button>
            <Button
              color={modal === 'ban' ? 'red' : 'green'}
              loading={banMutation.isPending || unbanMutation.isPending}
              onClick={() => (modal === 'ban' ? banMutation.mutate() : unbanMutation.mutate())}
            >
              {t('accounts:actions.confirm')}
            </Button>
          </Group>
        </Stack>
      </Modal>

      <Modal
        opened={modal === 'status'}
        onClose={() => setModal(null)}
        title={t('accounts:actions.statusTitle', { username: account.username })}
      >
        <Stack>
          {Boolean(actionError) && (
            <Text c="red" size="sm">
              {formatError(actionError)}
            </Text>
          )}
          <Select
            label={t('accounts:actions.newStatus')}
            value={statusId}
            onChange={setStatusId}
            data={NON_BAN_STATUS_IDS.map((sid) => ({
              value: String(sid),
              label: t(`accounts:status.${sid}` as never),
            }))}
          />
          <TextInput
            label={t('accounts:actions.reason')}
            value={reason}
            onChange={(e) => setReason(e.currentTarget.value)}
          />
          <Group justify="flex-end">
            <Button variant="subtle" onClick={() => setModal(null)}>
              {t('accounts:actions.cancel')}
            </Button>
            <Button
              disabled={!statusId}
              loading={statusMutation.isPending}
              onClick={() => statusMutation.mutate()}
            >
              {t('accounts:actions.confirm')}
            </Button>
          </Group>
        </Stack>
      </Modal>

      <Modal
        opened={modal === 'authority'}
        onClose={() => setModal(null)}
        title={t('accounts:actions.authorityTitle', { username: account.username })}
      >
        <Stack>
          {Boolean(actionError) && (
            <Text c="red" size="sm">
              {formatError(actionError)}
            </Text>
          )}
          <Select
            label={t('accounts:actions.newAuthority')}
            value={authorityId}
            onChange={setAuthorityId}
            data={AUTHORITY_IDS.map((aid) => ({
              value: String(aid),
              label: t(`common:authority.${aid}` as never),
            }))}
          />
          <Group justify="flex-end">
            <Button variant="subtle" onClick={() => setModal(null)}>
              {t('accounts:actions.cancel')}
            </Button>
            <Button
              disabled={!authorityId}
              loading={authorityMutation.isPending}
              onClick={() => authorityMutation.mutate()}
            >
              {t('accounts:actions.confirm')}
            </Button>
          </Group>
        </Stack>
      </Modal>

      <Modal
        opened={modal === 'password'}
        onClose={() => setModal(null)}
        title={t('accounts:actions.resetPasswordTitle', { username: account.username })}
      >
        <Stack>
          {Boolean(actionError) && (
            <Text c="red" size="sm">
              {formatError(actionError)}
            </Text>
          )}
          <PasswordInput
            label={t('accounts:actions.newPassword')}
            value={newPassword}
            onChange={(e) => setNewPassword(e.currentTarget.value)}
          />
          <Group justify="flex-end">
            <Button variant="subtle" onClick={() => setModal(null)}>
              {t('accounts:actions.cancel')}
            </Button>
            <Button
              color="orange"
              disabled={!newPassword}
              loading={passwordMutation.isPending}
              onClick={() => passwordMutation.mutate()}
            >
              {t('accounts:actions.confirm')}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  )
}
