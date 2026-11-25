import {
  ActionIcon,
  Badge,
  Button,
  Checkbox,
  Group,
  Loader,
  Modal,
  NumberInput,
  Paper,
  ScrollArea,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
  Title,
  Tooltip,
} from '@mantine/core'
import { notifications as toast } from '@mantine/notifications'
import { IconEdit, IconPlus, IconRefresh, IconSend, IconTrash } from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap, type Schemas } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { formatCooldown, isValidDiscordWebhookUrl } from './webhookUtils'

type Webhook = Schemas['NotificationWebhookDto']

interface FormState {
  id: number | null
  name: string
  webhookUrl: string
  enabled: boolean
  notifyBans: boolean
  notifyErrors: boolean
  notifyCheatReports: boolean
  minIntervalSeconds: number
}

const emptyForm: FormState = {
  id: null,
  name: '',
  webhookUrl: '',
  enabled: true,
  notifyBans: true,
  notifyErrors: true,
  notifyCheatReports: true,
  minIntervalSeconds: 0,
}

const categoryColor: Record<string, string> = {
  ban: 'red',
  error: 'orange',
  cheat_report: 'yellow',
  test: 'blue',
}

export default function NotificationsPage() {
  const { t, i18n } = useTranslation('notifications')
  const queryClient = useQueryClient()

  const [modalOpen, setModalOpen] = useState(false)
  const [form, setForm] = useState<FormState>(emptyForm)
  const [deleteTarget, setDeleteTarget] = useState<Webhook | null>(null)

  const webhooks = useQuery({
    queryKey: ['notifications', 'webhooks'],
    queryFn: () => unwrap(api.GET('/api/notifications/webhooks')),
  })

  const deliveries = useQuery({
    queryKey: ['notifications', 'deliveries'],
    queryFn: () => unwrap(api.GET('/api/notifications/deliveries')),
    refetchInterval: 15_000,
  })

  const saveMutation = useMutation({
    mutationFn: (state: FormState) => {
      const body: Schemas['SaveWebhookRequest'] = {
        name: state.name.trim(),
        webhookUrl: state.webhookUrl.trim() || null,
        enabled: state.enabled,
        notifyBans: state.notifyBans,
        notifyErrors: state.notifyErrors,
        notifyCheatReports: state.notifyCheatReports,
        minIntervalSeconds: Math.max(0, Math.round(state.minIntervalSeconds)),
      }
      if (state.id !== null) {
        return unwrap(
          api.PUT('/api/notifications/webhooks/{id}', {
            params: { path: { id: state.id } },
            body,
          }),
        )
      }
      return unwrap(api.POST('/api/notifications/webhooks', { body }))
    },
    onSuccess: () => {
      setModalOpen(false)
      void queryClient.invalidateQueries({ queryKey: ['notifications', 'webhooks'] })
      toast.show({ message: t('webhooks.saved'), color: 'green' })
    },
  })

  const toggleMutation = useMutation({
    mutationFn: ({ item, enabled }: { item: Webhook; enabled: boolean }) =>
      unwrap(
        api.PUT('/api/notifications/webhooks/{id}', {
          params: { path: { id: item.id } },
          body: {
            name: item.name,
            webhookUrl: null,
            enabled,
            notifyBans: item.notifyBans,
            notifyErrors: item.notifyErrors,
            notifyCheatReports: item.notifyCheatReports,
            minIntervalSeconds: item.minIntervalSeconds,
          },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['notifications', 'webhooks'] })
    },
  })

  const deleteMutation = useMutation({
    mutationFn: (id: number) =>
      unwrap(api.DELETE('/api/notifications/webhooks/{id}', { params: { path: { id } } })),
    onSuccess: () => {
      setDeleteTarget(null)
      void queryClient.invalidateQueries({ queryKey: ['notifications', 'webhooks'] })
      toast.show({ message: t('webhooks.deleted'), color: 'green' })
    },
  })

  const testMutation = useMutation({
    mutationFn: (id: number) =>
      unwrap(api.POST('/api/notifications/webhooks/{id}/test', { params: { path: { id } } })),
    onSuccess: (res) => {
      void queryClient.invalidateQueries({ queryKey: ['notifications', 'webhooks'] })
      void queryClient.invalidateQueries({ queryKey: ['notifications', 'deliveries'] })
      if (res.ok) {
        toast.show({ message: t('webhooks.testOk'), color: 'green' })
      } else {
        toast.show({
          message: t('webhooks.testFailed', { error: res.error ?? String(res.statusCode ?? 'error') }),
          color: 'red',
        })
      }
    },
  })

  const openCreate = () => {
    saveMutation.reset()
    setForm(emptyForm)
    setModalOpen(true)
  }

  const openEdit = (item: Webhook) => {
    saveMutation.reset()
    setForm({
      id: item.id,
      name: item.name,
      webhookUrl: '',
      enabled: item.enabled,
      notifyBans: item.notifyBans,
      notifyErrors: item.notifyErrors,
      notifyCheatReports: item.notifyCheatReports,
      minIntervalSeconds: item.minIntervalSeconds,
    })
    setModalOpen(true)
  }

  const urlProvided = form.webhookUrl.trim().length > 0
  const urlValid = form.id !== null ? !urlProvided || isValidDiscordWebhookUrl(form.webhookUrl) : isValidDiscordWebhookUrl(form.webhookUrl)
  const canSave = form.name.trim().length > 0 && urlValid

  return (
    <Stack gap="lg">
      <div>
        <Title order={2}>{t('title')}</Title>
        <Text c="dimmed">{t('description')}</Text>
      </div>

      <Stack gap="sm">
        <Group justify="space-between">
          <Group gap="xs">
            <Title order={4}>{t('webhooks.title')}</Title>
            {webhooks.isFetching && <Loader size="xs" />}
          </Group>
          <Button leftSection={<IconPlus size={16} />} size="sm" onClick={openCreate}>
            {t('webhooks.new')}
          </Button>
        </Group>

        <ErrorAlert error={webhooks.error || toggleMutation.error || testMutation.error} />

        <Paper withBorder>
          <ScrollArea>
            <Table highlightOnHover striped miw={780}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('webhooks.columns.name')}</Table.Th>
                  <Table.Th>{t('webhooks.columns.events')}</Table.Th>
                  <Table.Th>{t('webhooks.columns.cooldown')}</Table.Th>
                  <Table.Th>{t('webhooks.columns.lastSent')}</Table.Th>
                  <Table.Th>{t('webhooks.columns.enabled')}</Table.Th>
                  <Table.Th>{t('webhooks.columns.actions')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {webhooks.data?.map((item) => (
                  <Table.Tr key={item.id}>
                    <Table.Td>
                      <Text fw={500} size="sm">
                        {item.name}
                      </Text>
                      <Text size="xs" c="dimmed" ff="monospace">
                        {item.maskedUrl}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Group gap={4}>
                        {item.notifyBans && (
                          <Badge color="red" variant="light" size="sm">
                            {t('categories.ban')}
                          </Badge>
                        )}
                        {item.notifyErrors && (
                          <Badge color="orange" variant="light" size="sm">
                            {t('categories.error')}
                          </Badge>
                        )}
                        {item.notifyCheatReports && (
                          <Badge color="yellow" variant="light" size="sm">
                            {t('categories.cheat_report')}
                          </Badge>
                        )}
                      </Group>
                    </Table.Td>
                    <Table.Td>{formatCooldown(item.minIntervalSeconds)}</Table.Td>
                    <Table.Td>
                      <Text size="sm">
                        {item.lastSentAt
                          ? new Date(item.lastSentAt).toLocaleString(i18n.language)
                          : t('webhooks.never')}
                      </Text>
                      {item.lastError && (
                        <Text size="xs" c="red" lineClamp={1} maw={220}>
                          {item.lastError}
                        </Text>
                      )}
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
                        <Tooltip label={t('webhooks.actions.test')}>
                          <ActionIcon
                            variant="subtle"
                            color="blue"
                            loading={testMutation.isPending && testMutation.variables === item.id}
                            onClick={() => testMutation.mutate(item.id)}
                            aria-label={t('webhooks.actions.test')}
                          >
                            <IconSend size={16} />
                          </ActionIcon>
                        </Tooltip>
                        <Tooltip label={t('webhooks.actions.edit')}>
                          <ActionIcon
                            variant="subtle"
                            onClick={() => openEdit(item)}
                            aria-label={t('webhooks.actions.edit')}
                          >
                            <IconEdit size={16} />
                          </ActionIcon>
                        </Tooltip>
                        <Tooltip label={t('webhooks.actions.delete')}>
                          <ActionIcon
                            variant="subtle"
                            color="red"
                            onClick={() => setDeleteTarget(item)}
                            aria-label={t('webhooks.actions.delete')}
                          >
                            <IconTrash size={16} />
                          </ActionIcon>
                        </Tooltip>
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                ))}
                {webhooks.data && webhooks.data.length === 0 && (
                  <Table.Tr>
                    <Table.Td colSpan={6}>
                      <Text c="dimmed" ta="center" py="md">
                        {t('webhooks.empty')}
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
            <Title order={4}>{t('deliveries.title')}</Title>
            {deliveries.isFetching && <Loader size="xs" />}
          </Group>
          <Button
            variant="default"
            size="xs"
            leftSection={<IconRefresh size={14} />}
            onClick={() => void deliveries.refetch()}
          >
            {t('deliveries.refresh')}
          </Button>
        </Group>

        <ErrorAlert error={deliveries.error} />

        <Paper withBorder>
          <ScrollArea>
            <Table highlightOnHover striped miw={720}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('deliveries.columns.time')}</Table.Th>
                  <Table.Th>{t('deliveries.columns.webhook')}</Table.Th>
                  <Table.Th>{t('deliveries.columns.category')}</Table.Th>
                  <Table.Th>{t('deliveries.columns.summary')}</Table.Th>
                  <Table.Th>{t('deliveries.columns.result')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {deliveries.data?.map((row) => (
                  <Table.Tr key={row.id}>
                    <Table.Td>
                      <Text size="xs" style={{ whiteSpace: 'nowrap' }}>
                        {new Date(row.sentAt).toLocaleString(i18n.language)}
                      </Text>
                    </Table.Td>
                    <Table.Td>{row.webhookName}</Table.Td>
                    <Table.Td>
                      <Badge color={categoryColor[row.category] ?? 'gray'} variant="light" size="sm">
                        {t(`categories.${row.category}`, { defaultValue: row.category })}
                      </Badge>
                    </Table.Td>
                    <Table.Td>
                      <Text fw={500} size="sm">
                        {row.title}
                      </Text>
                      <Text size="xs" c="dimmed" lineClamp={2} maw={360}>
                        {row.summary}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Badge color={row.success ? 'green' : 'red'} variant="light" size="sm">
                        {row.success
                          ? `${t('deliveries.ok')}${row.statusCode ? ` (${row.statusCode})` : ''}`
                          : t('deliveries.failed')}
                      </Badge>
                      {row.error && (
                        <Text size="xs" c="red" lineClamp={1} maw={200}>
                          {row.error}
                        </Text>
                      )}
                    </Table.Td>
                  </Table.Tr>
                ))}
                {deliveries.data && deliveries.data.length === 0 && (
                  <Table.Tr>
                    <Table.Td colSpan={5}>
                      <Text c="dimmed" ta="center" py="md">
                        {t('deliveries.empty')}
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
        title={form.id !== null ? t('webhooks.edit') : t('webhooks.new')}
        size="md"
      >
        <Stack gap="sm">
          <ErrorAlert error={saveMutation.error} />
          <TextInput
            label={t('webhooks.form.name')}
            placeholder={t('webhooks.form.namePlaceholder')}
            value={form.name}
            maxLength={127}
            required
            onChange={(e) => setForm({ ...form, name: e.currentTarget.value })}
          />
          <TextInput
            label={t('webhooks.form.url')}
            placeholder={t('webhooks.form.urlPlaceholder')}
            description={form.id !== null ? t('webhooks.form.urlKeepNote') : undefined}
            error={urlProvided && !urlValid ? t('webhooks.form.urlInvalid') : undefined}
            value={form.webhookUrl}
            maxLength={512}
            required={form.id === null}
            onChange={(e) => setForm({ ...form, webhookUrl: e.currentTarget.value })}
          />
          <Stack gap={6}>
            <Text size="sm" fw={500}>
              {t('webhooks.form.events')}
            </Text>
            <Checkbox
              label={t('webhooks.form.notifyBans')}
              checked={form.notifyBans}
              onChange={(e) => setForm({ ...form, notifyBans: e.currentTarget.checked })}
            />
            <Checkbox
              label={t('webhooks.form.notifyErrors')}
              checked={form.notifyErrors}
              onChange={(e) => setForm({ ...form, notifyErrors: e.currentTarget.checked })}
            />
            <Checkbox
              label={t('webhooks.form.notifyCheatReports')}
              checked={form.notifyCheatReports}
              onChange={(e) => setForm({ ...form, notifyCheatReports: e.currentTarget.checked })}
            />
          </Stack>
          <NumberInput
            label={t('webhooks.form.cooldown')}
            value={form.minIntervalSeconds}
            min={0}
            max={86400}
            onChange={(v) => setForm({ ...form, minIntervalSeconds: Number(v) || 0 })}
          />
          <Switch
            label={t('webhooks.form.enabled')}
            checked={form.enabled}
            onChange={(e) => setForm({ ...form, enabled: e.currentTarget.checked })}
          />
          <Group justify="flex-end" mt="xs">
            <Button variant="default" onClick={() => setModalOpen(false)}>
              {t('webhooks.form.cancel')}
            </Button>
            <Button
              disabled={!canSave}
              loading={saveMutation.isPending}
              onClick={() => saveMutation.mutate(form)}
            >
              {t('webhooks.form.save')}
            </Button>
          </Group>
        </Stack>
      </Modal>

      <Modal
        opened={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        title={t('webhooks.deleteConfirm.title')}
        size="sm"
      >
        {deleteTarget && (
          <Stack gap="md">
            <ErrorAlert error={deleteMutation.error} />
            <Text>{t('webhooks.deleteConfirm.body', { name: deleteTarget.name })}</Text>
            <Group justify="flex-end">
              <Button variant="default" onClick={() => setDeleteTarget(null)}>
                {t('webhooks.deleteConfirm.cancel')}
              </Button>
              <Button
                color="red"
                loading={deleteMutation.isPending}
                onClick={() => deleteMutation.mutate(deleteTarget.id)}
              >
                {t('webhooks.deleteConfirm.confirm')}
              </Button>
            </Group>
          </Stack>
        )}
      </Modal>
    </Stack>
  )
}
