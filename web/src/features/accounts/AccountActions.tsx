import { Button, Group, Modal, PasswordInput, Select, Stack, Text, TextInput } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { IconBan, IconCheck, IconKey, IconShield, IconUserCog } from '@tabler/icons-react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap } from '../../api/client'
import { can, useMe } from '../../app/auth'
import { useErrorText } from '../../app/ErrorAlert'
import { AUTHORITY_IDS, NON_BAN_STATUS_IDS, isAccountBanned } from './accountMeta'

type ActionModal = 'ban' | 'unban' | 'status' | 'authority' | 'password' | null

interface AccountActionsProps {
  account: { accountId: number; username: string; statusId: number; authorityId: number }
}

// Ban, status, authority and password buttons of an account, with their dialogs
export function AccountActions({ account }: AccountActionsProps) {
  const accountId = account.accountId
  const { t } = useTranslation(['accounts', 'common'])
  const formatError = useErrorText('accounts')
  const queryClient = useQueryClient()
  const me = useMe().data

  const [modal, setModal] = useState<ActionModal>(null)
  const [reason, setReason] = useState('')
  const [statusId, setStatusId] = useState<string | null>(null)
  const [authorityId, setAuthorityId] = useState<string | null>(null)
  const [newPassword, setNewPassword] = useState('')
  const [actionError, setActionError] = useState<unknown>(null)

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['account', accountId] })
    void queryClient.invalidateQueries({ queryKey: ['players'] })
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

  const banned = isAccountBanned(account.statusId)
  const canBan = can(me, 'ManageCharacters')
  const canManage = can(me, 'ManageAccounts')
  if (!canBan && !canManage) return null

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
    <>
      <Group gap="xs" wrap="wrap">
        {canBan && !banned && (
          <Button size="xs" color="red" variant="light" leftSection={<IconBan size={15} />} onClick={() => openModal('ban')}>
            {t('accounts:actions.ban')}
          </Button>
        )}
        {canBan && banned && (
          <Button size="xs" color="green" variant="light" leftSection={<IconCheck size={15} />} onClick={() => openModal('unban')}>
            {t('accounts:actions.unban')}
          </Button>
        )}
        {canManage && (
          <>
            <Button size="xs" variant="light" leftSection={<IconUserCog size={15} />} onClick={() => openModal('status')}>
              {t('accounts:actions.status')}
            </Button>
            <Button size="xs" variant="light" leftSection={<IconShield size={15} />} onClick={() => openModal('authority')}>
              {t('accounts:actions.authority')}
            </Button>
            <Button size="xs" variant="light" color="orange" leftSection={<IconKey size={15} />} onClick={() => openModal('password')}>
              {t('accounts:actions.resetPassword')}
            </Button>
          </>
        )}
      </Group>

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
    </>
  )
}
