import {
  ActionIcon,
  Alert,
  Badge,
  Button,
  Group,
  Loader,
  Modal,
  NumberInput,
  Paper,
  ScrollArea,
  SegmentedControl,
  Stack,
  Switch,
  Table,
  Text,
  Textarea,
  TextInput,
  Title,
  Tooltip,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { IconEdit, IconPlayerPlay, IconPlus, IconRefresh, IconSend, IconTrash } from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap, type Schemas } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { announcementState, nextRunAt } from './schedule'

type Announcement = Schemas['AnnouncementDto']
type Channel = Schemas['BroadcastChannel']

const channels: Channel[] = ['Center', 'System', 'TopLeft']

const stateColor: Record<ReturnType<typeof announcementState>, string> = {
  active: 'green',
  scheduled: 'blue',
  expired: 'gray',
  disabled: 'gray',
}

function toDatetimeLocal(iso: string | null | undefined): string {
  if (!iso) return ''
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ''
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

function fromDatetimeLocal(value: string): string | null {
  if (!value.trim()) return null
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? null : d.toISOString()
}

interface FormState {
  id: number | null
  title: string
  channel: Channel
  message: string
  intervalMinutes: number
  enabled: boolean
  startAt: string
  endAt: string
}

const emptyForm: FormState = {
  id: null,
  title: '',
  channel: 'Center',
  message: '',
  intervalMinutes: 30,
  enabled: true,
  startAt: '',
  endAt: '',
}

export default function BroadcastsPage() {
  const { t, i18n } = useTranslation('broadcasts')
  const queryClient = useQueryClient()

  const [sendChannel, setSendChannel] = useState<Channel>('Center')
  const [sendText, setSendText] = useState('')
  const [modalOpen, setModalOpen] = useState(false)
  const [form, setForm] = useState<FormState>(emptyForm)
  const [deleteTarget, setDeleteTarget] = useState<Announcement | null>(null)

  const queue = useQuery({
    queryKey: ['broadcasts', 'queue'],
    queryFn: () => unwrap(api.GET('/api/broadcasts/queue')),
    refetchInterval: 10_000,
  })

  const announcements = useQuery({
    queryKey: ['broadcasts', 'announcements'],
    queryFn: () => unwrap(api.GET('/api/broadcasts/announcements')),
  })

  const gameConnected = queue.data?.gameConnected ?? false

  const sendMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST('/api/broadcasts/send', {
          body: { channel: sendChannel, text: sendText.trim() },
        }),
      ),
    onSuccess: () => {
      setSendText('')
      notifications.show({ message: t('send.sent'), color: 'green' })
    },
  })

  const saveMutation = useMutation({
    mutationFn: (state: FormState) => {
      const body: Schemas['SaveAnnouncementRequest'] = {
        title: state.title.trim(),
        channel: state.channel,
        message: state.message.trim(),
        intervalMinutes: state.intervalMinutes,
        enabled: state.enabled,
        startAt: fromDatetimeLocal(state.startAt),
        endAt: fromDatetimeLocal(state.endAt),
      }
      if (state.id !== null) {
        return unwrap(api.PUT('/api/broadcasts/announcements/{id}', { params: { path: { id: state.id } }, body }))
      }
      return unwrap(api.POST('/api/broadcasts/announcements', { body }))
    },
    onSuccess: () => {
      setModalOpen(false)
      void queryClient.invalidateQueries({ queryKey: ['broadcasts', 'announcements'] })
      notifications.show({ message: t('scheduled.saved'), color: 'green' })
    },
  })

  const toggleMutation = useMutation({
    mutationFn: ({ item, enabled }: { item: Announcement; enabled: boolean }) =>
      unwrap(
        api.PUT('/api/broadcasts/announcements/{id}', {
          params: { path: { id: item.id } },
          body: {
            title: item.title,
            channel: item.channel,
            message: item.message,
            intervalMinutes: item.intervalMinutes,
            enabled,
            startAt: item.startAt,
            endAt: item.endAt,
          },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['broadcasts', 'announcements'] })
    },
  })

  const deleteMutation = useMutation({
    mutationFn: (id: number) =>
      unwrap(api.DELETE('/api/broadcasts/announcements/{id}', { params: { path: { id } } })),
    onSuccess: () => {
      setDeleteTarget(null)
      void queryClient.invalidateQueries({ queryKey: ['broadcasts', 'announcements'] })
      notifications.show({ message: t('scheduled.deleted'), color: 'green' })
    },
  })

  const sendNowMutation = useMutation({
    mutationFn: (id: number) =>
      unwrap(api.POST('/api/broadcasts/announcements/{id}/send', { params: { path: { id } } })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['broadcasts', 'announcements'] })
      notifications.show({ message: t('scheduled.sentNow'), color: 'green' })
    },
  })

  const openCreate = () => {
    saveMutation.reset()
    setForm(emptyForm)
    setModalOpen(true)
  }

  const openEdit = (item: Announcement) => {
    saveMutation.reset()
    setForm({
      id: item.id,
      title: item.title,
      channel: item.channel,
      message: item.message,
      intervalMinutes: item.intervalMinutes,
      enabled: item.enabled,
      startAt: toDatetimeLocal(item.startAt),
      endAt: toDatetimeLocal(item.endAt),
    })
    setModalOpen(true)
  }

  const now = new Date()

  return (
    <Stack gap="lg">
      <Group justify="space-between">
        <div>
          <Title order={2}>{t('title')}</Title>
          <Text c="dimmed">{t('description')}</Text>
        </div>
        {queue.data && (
          <Badge color={gameConnected ? 'green' : 'red'} variant="light" size="lg">
            {gameConnected ? t('gameOnline') : t('gameOffline')}
          </Badge>
        )}
      </Group>

      <Paper withBorder p="md">
        <Stack gap="sm">
          <Title order={4}>{t('send.title')}</Title>
          <ErrorAlert error={sendMutation.error} />
          {queue.data && !gameConnected && (
            <Alert color="yellow" variant="light">
              {t('send.offlineNote')}
            </Alert>
          )}
          <Group align="center">
            <Text size="sm" fw={500}>
              {t('send.channel')}
            </Text>
            <SegmentedControl
              value={sendChannel}
              onChange={(v) => setSendChannel(v as Channel)}
              data={channels.map((c) => ({ value: c, label: t(`send.channels.${c}`) }))}
            />
          </Group>
          <Textarea
            label={t('send.message')}
            placeholder={t('send.placeholder')}
            value={sendText}
            onChange={(e) => setSendText(e.currentTarget.value)}
            maxLength={512}
            minRows={2}
            autosize
          />
          <Group justify="space-between">
            <Text size="xs" c="dimmed">
              {sendText.trim().length} / 512
            </Text>
            <Button
              leftSection={<IconSend size={16} />}
              disabled={!gameConnected || sendText.trim().length === 0}
              loading={sendMutation.isPending}
              onClick={() => sendMutation.mutate()}
            >
              {t('send.submit')}
            </Button>
          </Group>
        </Stack>
      </Paper>

      <Stack gap="sm">
        <Group justify="space-between">
          <Group gap="xs">
            <Title order={4}>{t('scheduled.title')}</Title>
            {announcements.isFetching && <Loader size="xs" />}
          </Group>
          <Button leftSection={<IconPlus size={16} />} size="sm" onClick={openCreate}>
            {t('scheduled.new')}
          </Button>
        </Group>
        <ErrorAlert error={announcements.error || toggleMutation.error || sendNowMutation.error} />
        <Paper withBorder>
          <ScrollArea>
            <Table highlightOnHover striped miw={820}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('scheduled.columns.title')}</Table.Th>
                  <Table.Th>{t('scheduled.columns.channel')}</Table.Th>
                  <Table.Th>{t('scheduled.columns.interval')}</Table.Th>
                  <Table.Th>{t('scheduled.columns.status')}</Table.Th>
                  <Table.Th>{t('scheduled.columns.lastSent')}</Table.Th>
                  <Table.Th>{t('scheduled.columns.nextRun')}</Table.Th>
                  <Table.Th>{t('scheduled.columns.enabled')}</Table.Th>
                  <Table.Th>{t('scheduled.columns.actions')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {announcements.data?.map((item) => {
                  const state = announcementState(item, now)
                  const next = nextRunAt(item, now)
                  return (
                    <Table.Tr key={item.id}>
                      <Table.Td>
                        <Text fw={500} size="sm">
                          {item.title}
                        </Text>
                        <Text size="xs" c="dimmed" lineClamp={1} maw={280}>
                          {item.message}
                        </Text>
                      </Table.Td>
                      <Table.Td>
                        <Badge variant="outline">{t(`send.channels.${item.channel}`)}</Badge>
                      </Table.Td>
                      <Table.Td>{t('scheduled.interval', { minutes: item.intervalMinutes })}</Table.Td>
                      <Table.Td>
                        <Badge color={stateColor[state]} variant="light">
                          {t(`scheduled.state.${state}`)}
                        </Badge>
                      </Table.Td>
                      <Table.Td>
                        {item.lastSentAt
                          ? new Date(item.lastSentAt).toLocaleString(i18n.language)
                          : t('scheduled.never')}
                      </Table.Td>
                      <Table.Td>
                        {next
                          ? next.getTime() <= now.getTime()
                            ? t('scheduled.dueNow')
                            : next.toLocaleString(i18n.language)
                          : '—'}
                      </Table.Td>
                      <Table.Td>
                        <Switch
                          checked={item.enabled}
                          disabled={toggleMutation.isPending}
                          onChange={(e) =>
                            toggleMutation.mutate({ item, enabled: e.currentTarget.checked })
                          }
                        />
                      </Table.Td>
                      <Table.Td>
                        <Group gap={4} wrap="nowrap">
                          <Tooltip label={t('scheduled.actions.sendNow')}>
                            <ActionIcon
                              variant="subtle"
                              color="blue"
                              disabled={!gameConnected}
                              loading={sendNowMutation.isPending && sendNowMutation.variables === item.id}
                              onClick={() => sendNowMutation.mutate(item.id)}
                              aria-label={t('scheduled.actions.sendNow')}
                            >
                              <IconPlayerPlay size={16} />
                            </ActionIcon>
                          </Tooltip>
                          <Tooltip label={t('scheduled.actions.edit')}>
                            <ActionIcon
                              variant="subtle"
                              onClick={() => openEdit(item)}
                              aria-label={t('scheduled.actions.edit')}
                            >
                              <IconEdit size={16} />
                            </ActionIcon>
                          </Tooltip>
                          <Tooltip label={t('scheduled.actions.delete')}>
                            <ActionIcon
                              variant="subtle"
                              color="red"
                              onClick={() => setDeleteTarget(item)}
                              aria-label={t('scheduled.actions.delete')}
                            >
                              <IconTrash size={16} />
                            </ActionIcon>
                          </Tooltip>
                        </Group>
                      </Table.Td>
                    </Table.Tr>
                  )
                })}
                {announcements.data && announcements.data.length === 0 && (
                  <Table.Tr>
                    <Table.Td colSpan={8}>
                      <Text c="dimmed" ta="center" py="md">
                        {t('scheduled.empty')}
                      </Text>
                    </Table.Td>
                  </Table.Tr>
                )}
              </Table.Tbody>
            </Table>
          </ScrollArea>
        </Paper>
      </Stack>

      <Stack gap="sm">
        <Group justify="space-between">
          <Group gap="xs">
            <Title order={4}>{t('queue.title')}</Title>
            {queue.data && <Badge variant="light">{queue.data.items.length}</Badge>}
            {queue.isFetching && <Loader size="xs" />}
          </Group>
          <Button
            variant="default"
            size="xs"
            leftSection={<IconRefresh size={14} />}
            onClick={() => void queue.refetch()}
          >
            {t('queue.refresh')}
          </Button>
        </Group>
        <ErrorAlert error={queue.error} />
        <Paper withBorder>
          <ScrollArea>
            <Table highlightOnHover striped miw={640}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('queue.columns.position')}</Table.Th>
                  <Table.Th>{t('queue.columns.sender')}</Table.Th>
                  <Table.Th>{t('queue.columns.message')}</Table.Th>
                  <Table.Th>{t('queue.columns.addition')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {queue.data?.items.map((item) => (
                  <Table.Tr key={item.id}>
                    <Table.Td>{item.position + 1}</Table.Td>
                    <Table.Td>
                      {item.senderName}{' '}
                      <Text span c="dimmed" size="xs">
                        #{item.characterId}
                      </Text>
                    </Table.Td>
                    <Table.Td>{item.message}</Table.Td>
                    <Table.Td>{item.addition}</Table.Td>
                  </Table.Tr>
                ))}
                {queue.data && queue.data.items.length === 0 && (
                  <Table.Tr>
                    <Table.Td colSpan={4}>
                      <Text c="dimmed" ta="center" py="md">
                        {t('queue.empty')}
                      </Text>
                    </Table.Td>
                  </Table.Tr>
                )}
              </Table.Tbody>
            </Table>
          </ScrollArea>
        </Paper>
      </Stack>

      <Modal
        opened={modalOpen}
        onClose={() => setModalOpen(false)}
        title={form.id !== null ? t('scheduled.edit') : t('scheduled.new')}
        size="md"
      >
        <Stack gap="sm">
          <ErrorAlert error={saveMutation.error} />
          <TextInput
            label={t('scheduled.form.title')}
            placeholder={t('scheduled.form.titlePlaceholder')}
            value={form.title}
            maxLength={127}
            required
            onChange={(e) => setForm({ ...form, title: e.currentTarget.value })}
          />
          <div>
            <Text size="sm" fw={500} mb={4}>
              {t('scheduled.form.channel')}
            </Text>
            <SegmentedControl
              fullWidth
              value={form.channel}
              onChange={(v) => setForm({ ...form, channel: v as Channel })}
              data={channels.map((c) => ({ value: c, label: t(`send.channels.${c}`) }))}
            />
          </div>
          <Textarea
            label={t('scheduled.form.message')}
            placeholder={t('scheduled.form.messagePlaceholder')}
            value={form.message}
            maxLength={512}
            minRows={2}
            autosize
            required
            onChange={(e) => setForm({ ...form, message: e.currentTarget.value })}
          />
          <NumberInput
            label={t('scheduled.form.interval')}
            value={form.intervalMinutes}
            min={1}
            max={10080}
            onChange={(v) => setForm({ ...form, intervalMinutes: Number(v) || 1 })}
          />
          <Group grow>
            <TextInput
              type="datetime-local"
              label={t('scheduled.form.startAt')}
              value={form.startAt}
              onChange={(e) => setForm({ ...form, startAt: e.currentTarget.value })}
            />
            <TextInput
              type="datetime-local"
              label={t('scheduled.form.endAt')}
              value={form.endAt}
              onChange={(e) => setForm({ ...form, endAt: e.currentTarget.value })}
            />
          </Group>
          <Switch
            label={t('scheduled.form.enabled')}
            checked={form.enabled}
            onChange={(e) => setForm({ ...form, enabled: e.currentTarget.checked })}
          />
          <Group justify="flex-end" mt="xs">
            <Button variant="default" onClick={() => setModalOpen(false)}>
              {t('scheduled.form.cancel')}
            </Button>
            <Button
              disabled={form.title.trim().length === 0 || form.message.trim().length === 0}
              loading={saveMutation.isPending}
              onClick={() => saveMutation.mutate(form)}
            >
              {t('scheduled.form.save')}
            </Button>
          </Group>
        </Stack>
      </Modal>

      <Modal
        opened={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        title={t('scheduled.deleteConfirm.title')}
        size="sm"
      >
        {deleteTarget && (
          <Stack gap="md">
            <ErrorAlert error={deleteMutation.error} />
            <Text>{t('scheduled.deleteConfirm.body', { title: deleteTarget.title })}</Text>
            <Group justify="flex-end">
              <Button variant="default" onClick={() => setDeleteTarget(null)}>
                {t('scheduled.deleteConfirm.cancel')}
              </Button>
              <Button
                color="red"
                loading={deleteMutation.isPending}
                onClick={() => deleteMutation.mutate(deleteTarget.id)}
              >
                {t('scheduled.deleteConfirm.confirm')}
              </Button>
            </Group>
          </Stack>
        )}
      </Modal>
    </Stack>
  )
}
