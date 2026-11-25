import {
  ActionIcon,
  Alert,
  Anchor,
  Badge,
  Button,
  Code,
  Group,
  Loader,
  Menu,
  Modal,
  Paper,
  Popover,
  ScrollArea,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
  Tooltip,
} from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import {
  IconAlertTriangle,
  IconExternalLink,
  IconLock,
  IconRefresh,
  IconVolume,
  IconVolumeOff,
} from '@tabler/icons-react'
import { keepPreviousData, useMutation, useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { ApiError, api, unwrap, type Schemas } from '../../api/client'
import { can, useMe } from '../../app/auth'
import { useErrorText } from '../../app/ErrorAlert'
import {
  channelColor,
  chatChannels,
  commandResultColor,
  muteDurationPresets,
  rangeWindow,
  timeRangePresets,
  type TimeRangePreset,
} from './chatMeta'

type ChatEntry = Schemas['ChatLogEntry']

function ModerationActions({
  characterId,
  sender,
}: {
  characterId: number
  sender: string
}) {
  const { t } = useTranslation('chat')
  const commonError = useErrorText()
  const [jailOpen, setJailOpen] = useState(false)

  const formatError = (err: unknown) => {
    if (err instanceof ApiError) {
      const translated = t(`errors.${err.code}`, { defaultValue: '' })
      if (translated) return translated
    }
    return commonError(err)
  }

  const mute = useMutation({
    mutationFn: (minutes: number) =>
      unwrap(
        api.POST('/api/characters/{id}/mute', {
          params: { path: { id: characterId } },
          body: { minutes },
        }),
      ),
    onSuccess: (_data, minutes) => {
      notifications.show({
        color: 'teal',
        message:
          minutes > 0
            ? t('notifications.muted', { name: sender || `#${characterId}`, minutes })
            : t('notifications.unmuted', { name: sender || `#${characterId}` }),
      })
    },
    onError: (err) => {
      notifications.show({ color: 'red', message: formatError(err) })
    },
  })

  const jail = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST('/api/characters/{id}/jail', {
          params: { path: { id: characterId } },
        }),
      ),
    onSuccess: () => {
      setJailOpen(false)
      notifications.show({
        color: 'teal',
        message: t('notifications.jailed', { name: sender || `#${characterId}` }),
      })
    },
    onError: (err) => {
      notifications.show({ color: 'red', message: formatError(err) })
    },
  })

  return (
    <Group gap={4} wrap="nowrap" onClick={(e) => e.stopPropagation()}>
      <Menu shadow="md" width={160} position="bottom-end">
        <Menu.Target>
          <Tooltip label={t('actions.mute')}>
            <ActionIcon
              variant="light"
              color="orange"
              size="sm"
              loading={mute.isPending}
              aria-label={t('actions.mute')}
            >
              <IconVolumeOff size={14} />
            </ActionIcon>
          </Tooltip>
        </Menu.Target>
        <Menu.Dropdown>
          {muteDurationPresets.map((minutes) => (
            <Menu.Item
              key={minutes}
              leftSection={<IconVolumeOff size={14} />}
              onClick={() => mute.mutate(minutes)}
            >
              {t('actions.muteFor', { label: t(`duration.${minutes}`) })}
            </Menu.Item>
          ))}
          <Menu.Divider />
          <Menu.Item leftSection={<IconVolume size={14} />} onClick={() => mute.mutate(0)}>
            {t('actions.unmute')}
          </Menu.Item>
        </Menu.Dropdown>
      </Menu>

      <Popover opened={jailOpen} onChange={setJailOpen} position="bottom-end" withArrow shadow="md">
        <Popover.Target>
          <Tooltip label={t('actions.jail')}>
            <ActionIcon
              variant="light"
              color="red"
              size="sm"
              loading={jail.isPending}
              onClick={() => setJailOpen((o) => !o)}
              aria-label={t('actions.jail')}
            >
              <IconLock size={14} />
            </ActionIcon>
          </Tooltip>
        </Popover.Target>
        <Popover.Dropdown>
          <Stack gap="xs" maw={220}>
            <Text size="xs">{t('actions.jailConfirm', { name: sender || `#${characterId}` })}</Text>
            <Group gap="xs" justify="flex-end">
              <Button size="compact-xs" variant="default" onClick={() => setJailOpen(false)}>
                {t('actions.cancel')}
              </Button>
              <Button size="compact-xs" color="red" loading={jail.isPending} onClick={() => jail.mutate()}>
                {t('actions.confirm')}
              </Button>
            </Group>
          </Stack>
        </Popover.Dropdown>
      </Popover>
    </Group>
  )
}

export function ChatLogTab() {
  const { t, i18n } = useTranslation('chat')
  const me = useMe().data
  const canModerate = can(me, 'Moderate')

  const [preset, setPreset] = useState<TimeRangePreset>('24h')
  const [channel, setChannel] = useState<string>('')
  const [character, setCharacter] = useState('')
  const [account, setAccount] = useState('')
  const [words, setWords] = useState('')
  const [refreshTick, setRefreshTick] = useState(0)
  const [selected, setSelected] = useState<ChatEntry | null>(null)
  const [debounced] = useDebouncedValue({ character, account, words }, 300)

  const window = useMemo(() => rangeWindow(preset), [preset, refreshTick])

  const query = {
    channel: channel || undefined,
    character: debounced.character.trim() || undefined,
    account: debounced.account.trim() || undefined,
    words: debounced.words.trim() || undefined,
    from: window.from,
    to: window.to,
    limit: 200,
  }

  const chat = useQuery({
    queryKey: ['chat', 'messages', query],
    queryFn: () => unwrap(api.GET('/api/chat/messages', { params: { query } })),
    placeholderData: keepPreviousData,
  })

  const errorText = (error: unknown) => {
    const code = error instanceof ApiError ? error.code : 'server_error'
    return t(`errors.${code}`, {
      defaultValue: t(`common:errors.${code}`, { defaultValue: t('common:errors.unknown', { code }) }),
    })
  }

  return (
    <Stack gap="sm">
      <Group align="flex-end" wrap="wrap">
        <Select
          label={t('filter.range')}
          value={preset}
          onChange={(v) => v && setPreset(v as TimeRangePreset)}
          data={timeRangePresets.map((p) => ({ value: p, label: t(`range.${p}`) }))}
          w={150}
        />
        <Select
          label={t('filter.channel')}
          value={channel}
          onChange={(v) => setChannel(v ?? '')}
          data={[
            { value: '', label: t('filter.allChannels') },
            ...chatChannels.map((c) => ({ value: c, label: t(`channel.${c}`) })),
          ]}
          w={150}
        />
        <TextInput
          label={t('filter.character')}
          placeholder={t('filter.characterPlaceholder')}
          value={character}
          onChange={(e) => setCharacter(e.currentTarget.value)}
          w={170}
        />
        <TextInput
          label={t('filter.account')}
          placeholder={t('filter.accountPlaceholder')}
          value={account}
          onChange={(e) => setAccount(e.currentTarget.value)}
          w={170}
        />
        <TextInput
          label={t('filter.words')}
          placeholder={t('filter.wordsPlaceholder')}
          value={words}
          onChange={(e) => setWords(e.currentTarget.value)}
          style={{ flex: 1, minWidth: 180 }}
        />
      </Group>

      <Group justify="space-between" wrap="wrap">
        <Group gap="xs">
          <Button
            variant="default"
            size="xs"
            leftSection={<IconRefresh size={14} />}
            onClick={() => setRefreshTick((n) => n + 1)}
            loading={chat.isFetching}
          >
            {t('actions.refresh')}
          </Button>
          {chat.data && (
            <Text size="sm" c="dimmed">
              {t('count', { count: chat.data.entries.length })}
            </Text>
          )}
        </Group>
        {chat.data && (
          <Button
            component="a"
            href={chat.data.grafanaExploreUrl}
            target="_blank"
            rel="noreferrer"
            variant="subtle"
            size="xs"
            rightSection={<IconExternalLink size={14} />}
          >
            {t('actions.exploreGrafana')}
          </Button>
        )}
      </Group>

      {chat.error && (
        <Alert color="red" icon={<IconAlertTriangle size={18} />}>
          {errorText(chat.error)}
        </Alert>
      )}

      {chat.data?.truncated && (
        <Alert color="yellow">{t('truncated', { count: chat.data.entries.length })}</Alert>
      )}

      <Paper withBorder>
        <ScrollArea>
          <Table highlightOnHover striped miw={820}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('column.time')}</Table.Th>
                <Table.Th>{t('column.channel')}</Table.Th>
                <Table.Th>{t('column.sender')}</Table.Th>
                <Table.Th>{t('column.recipient')}</Table.Th>
                <Table.Th>{t('column.message')}</Table.Th>
                {canModerate && <Table.Th w={80}>{t('column.actions')}</Table.Th>}
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {chat.isPending && (
                <Table.Tr>
                  <Table.Td colSpan={canModerate ? 6 : 5}>
                    <Group justify="center" py="md">
                      <Loader size="sm" />
                    </Group>
                  </Table.Td>
                </Table.Tr>
              )}
              {chat.data?.entries.map((entry, idx) => (
                <Table.Tr
                  key={`${entry.timestamp}-${entry.event}-${idx}`}
                  onClick={() => setSelected(entry)}
                  style={{ cursor: 'pointer' }}
                >
                  <Table.Td style={{ whiteSpace: 'nowrap' }}>
                    {new Date(entry.timestamp).toLocaleString(i18n.language)}
                  </Table.Td>
                  <Table.Td>
                    <Badge variant="light" color={channelColor(entry.channel)} size="sm">
                      {t(`channel.${entry.channel}`, { defaultValue: entry.channel })}
                    </Badge>
                  </Table.Td>
                  <Table.Td style={{ whiteSpace: 'nowrap' }} onClick={(e) => e.stopPropagation()}>
                    <Group gap={6} wrap="nowrap">
                      {entry.characterId != null ? (
                        <Anchor component={Link} to={`/admin/characters/${entry.characterId}`} size="sm" fw={500}>
                          {entry.sender || `#${entry.characterId}`}
                        </Anchor>
                      ) : (
                        <Text size="sm" fw={500}>
                          {entry.sender || '—'}
                        </Text>
                      )}
                      {entry.accountId != null && (
                        <Anchor component={Link} to={`/admin/accounts/${entry.accountId}`} size="xs" c="dimmed">
                          #{entry.accountId}
                        </Anchor>
                      )}
                    </Group>
                  </Table.Td>
                  <Table.Td style={{ whiteSpace: 'nowrap' }}>
                    {entry.recipient ? <Text size="sm">{entry.recipient}</Text> : <Text size="sm" c="dimmed">—</Text>}
                  </Table.Td>
                  <Table.Td>
                    {entry.command != null ? (
                      <Group gap="xs" wrap="nowrap">
                        <Code>{entry.text}</Code>
                        {entry.result && (
                          <Badge variant="light" size="xs" color={commandResultColor(entry.result)}>
                            {entry.result}
                          </Badge>
                        )}
                      </Group>
                    ) : (
                      <Text size="sm">{entry.text}</Text>
                    )}
                  </Table.Td>
                  {canModerate && (
                    <Table.Td>
                      {entry.characterId != null && (
                        <ModerationActions characterId={entry.characterId} sender={entry.sender} />
                      )}
                    </Table.Td>
                  )}
                </Table.Tr>
              ))}
              {chat.data && chat.data.entries.length === 0 && (
                <Table.Tr>
                  <Table.Td colSpan={canModerate ? 6 : 5}>
                    <Text c="dimmed" ta="center" py="md">
                      {t('empty')}
                    </Text>
                  </Table.Td>
                </Table.Tr>
              )}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      </Paper>

      <Modal opened={!!selected} onClose={() => setSelected(null)} title={t('detail.title')} size="lg">
        {selected && (
          <Stack gap="xs">
            <Group gap="xs">
              <Badge variant="light" color={channelColor(selected.channel)}>
                {t(`channel.${selected.channel}`, { defaultValue: selected.channel })}
              </Badge>
              <Code>{selected.event}</Code>
              <Text size="sm" c="dimmed">
                {new Date(selected.timestamp).toLocaleString(i18n.language)}
              </Text>
            </Group>
            <Text fw={500}>{selected.text}</Text>
            {selected.characterId != null && (
              <Text size="sm">
                {t('detail.characterId')}:{' '}
                <Anchor component={Link} to={`/admin/characters/${selected.characterId}`}>
                  {selected.sender} (#{selected.characterId})
                </Anchor>
              </Text>
            )}
            {selected.accountId != null && (
              <Text size="sm">
                {t('detail.accountId')}:{' '}
                <Anchor component={Link} to={`/admin/accounts/${selected.accountId}`}>
                  #{selected.accountId}
                </Anchor>
              </Text>
            )}
            {selected.command && (
              <Text size="sm">
                {t('detail.command')}: <Code>/{selected.command}</Code>
                {selected.result && (
                  <>
                    {' '}
                    <Badge variant="light" size="xs" color={commandResultColor(selected.result)}>
                      {selected.result}
                    </Badge>
                  </>
                )}
              </Text>
            )}
            <Text fw={600} mt="xs" size="sm">
              {t('detail.rawMessage')}
            </Text>
            <Code block>{selected.rawMessage}</Code>
          </Stack>
        )}
      </Modal>
    </Stack>
  )
}
