import {
  Alert,
  Badge,
  Button,
  Checkbox,
  Group,
  Modal,
  NumberInput,
  Paper,
  SegmentedControl,
  Stack,
  Switch,
  Text,
  Textarea,
  TextInput,
  Title,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import {
  IconAlertTriangle,
  IconCheck,
  IconMessage,
  IconPower,
  IconTool,
  IconX,
} from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap, type Schemas } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { formatClock, milestoneLabels, motdLines, remainingSeconds } from './countdown'

type ShutdownMode = NonNullable<Schemas['ShutdownMode']>

const presets = [1, 5, 10, 15, 30]

export default function ServerControlPage() {
  const { t, i18n } = useTranslation('server')
  const queryClient = useQueryClient()

  const status = useQuery({
    queryKey: ['server', 'status'],
    queryFn: () => unwrap(api.GET('/api/server/status')),
    refetchInterval: 5_000,
  })

  const [mode, setMode] = useState<ShutdownMode>('Restart')
  const [minutes, setMinutes] = useState<number>(5)
  const [reason, setReason] = useState('')
  const [confirmOpen, setConfirmOpen] = useState(false)

  const [maintEnabled, setMaintEnabled] = useState(false)
  const [maintMessage, setMaintMessage] = useState('')
  const [kickNonGm, setKickNonGm] = useState(false)
  const [maintDirty, setMaintDirty] = useState(false)

  const [motdText, setMotdText] = useState('')
  const [motdDirty, setMotdDirty] = useState(false)

  const [nowMs, setNowMs] = useState(() => Date.now())

  useEffect(() => {
    if (!status.data) return
    if (!maintDirty) {
      setMaintEnabled(status.data.maintenanceEnabled)
      setMaintMessage(status.data.maintenanceMessage)
    }
    if (!motdDirty) {
      setMotdText(status.data.motd)
    }
  }, [status.data, maintDirty, motdDirty])

  useEffect(() => {
    if (!status.data?.shutdownPending) return
    setNowMs(Date.now())
    const id = window.setInterval(() => setNowMs(Date.now()), 1000)
    return () => window.clearInterval(id)
  }, [status.data?.shutdownPending])

  const startShutdown = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST('/api/server/shutdown', {
          body: {
            mode,
            seconds: Math.max(1, Math.round(minutes)) * 60,
            reason: reason.trim(),
          },
        }),
      ),
    onSuccess: () => {
      setConfirmOpen(false)
      setReason('')
      void queryClient.invalidateQueries({ queryKey: ['server', 'status'] })
      notifications.show({ message: t('shutdown.started'), color: 'orange' })
    },
  })

  const cancelShutdown = useMutation({
    mutationFn: () => unwrap(api.POST('/api/server/shutdown/cancel')),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['server', 'status'] })
      notifications.show({ message: t('shutdown.cancelled'), color: 'green' })
    },
  })

  const saveMaintenance = useMutation({
    mutationFn: () =>
      unwrap(
        api.PUT('/api/server/maintenance', {
          body: {
            enabled: maintEnabled,
            message: maintMessage.trim(),
            kickNonGm: maintEnabled && kickNonGm,
          },
        }),
      ),
    onSuccess: (res) => {
      setMaintDirty(false)
      setKickNonGm(false)
      void queryClient.invalidateQueries({ queryKey: ['server', 'status'] })
      notifications.show({
        message: res.appliedLive ? t('maintenance.savedLive') : t('maintenance.savedOffline'),
        color: 'green',
      })
    },
  })

  const saveMotd = useMutation({
    mutationFn: (text: string) =>
      unwrap(api.PUT('/api/server/motd', { body: { text: text.trim() } })),
    onSuccess: (res) => {
      setMotdDirty(false)
      void queryClient.invalidateQueries({ queryKey: ['server', 'status'] })
      notifications.show({
        message: res.appliedLive ? t('motd.savedLive') : t('motd.savedOffline'),
        color: 'green',
      })
    },
  })

  const data = status.data
  const gameConnected = data?.gameConnected ?? false
  const leftSec = remainingSeconds(data?.shutdownAt, nowMs)
  const previewLines = motdLines(motdText)
  const modeLabel = t(`shutdown.modes.${mode}`)

  return (
    <Stack gap="lg">
      <Group justify="space-between" align="flex-start">
        <div>
          <Title order={2}>{t('title')}</Title>
          <Text c="dimmed">{t('description')}</Text>
        </div>
        {data && (
          <Group gap="xs">
            <Badge color={gameConnected ? 'green' : 'red'} variant="light" size="lg">
              {gameConnected ? t('gameOnline') : t('gameOffline')}
            </Badge>
            {gameConnected && (
              <Badge variant="outline" size="lg">
                {t('onlineCount', { total: data.onlinePlayers, players: data.onlineNonGmPlayers })}
              </Badge>
            )}
          </Group>
        )}
      </Group>

      <ErrorAlert error={status.error} />

      <Paper withBorder p="md">
        <Stack gap="sm">
          <Group gap="xs">
            <IconPower size={18} />
            <Title order={4}>{t('shutdown.title')}</Title>
          </Group>

          <ErrorAlert error={startShutdown.error || cancelShutdown.error} />

          {data && !gameConnected && (
            <Alert color="yellow" variant="light">
              {t('shutdown.offlineNote')}
            </Alert>
          )}

          {data?.shutdownPending ? (
            <Alert
              color="red"
              variant="light"
              icon={<IconAlertTriangle size={18} />}
              title={t('shutdown.activeTitle', {
                mode: t(`shutdown.modes.${data.shutdownMode ?? 'Shutdown'}`),
              })}
            >
              <Stack gap="xs">
                <Text fw={700} size="lg">
                  {t('shutdown.activeCountdown', { clock: formatClock(leftSec) })}
                </Text>
                {data.shutdownAt && (
                  <Text size="sm">
                    {t('shutdown.activeAt', {
                      time: new Date(data.shutdownAt).toLocaleString(i18n.language),
                    })}
                  </Text>
                )}
                {data.shutdownReason && (
                  <Text size="sm">{t('shutdown.activeReason', { reason: data.shutdownReason })}</Text>
                )}
                <div>
                  <Button
                    color="red"
                    variant="outline"
                    leftSection={<IconX size={16} />}
                    loading={cancelShutdown.isPending}
                    onClick={() => cancelShutdown.mutate()}
                  >
                    {t('shutdown.cancel')}
                  </Button>
                </div>
              </Stack>
            </Alert>
          ) : (
            <Stack gap="sm">
              <Group align="center">
                <Text size="sm" fw={500}>
                  {t('shutdown.mode')}
                </Text>
                <SegmentedControl
                  value={mode}
                  onChange={(v) => setMode(v as ShutdownMode)}
                  data={[
                    { value: 'Restart', label: t('shutdown.modes.Restart') },
                    { value: 'Shutdown', label: t('shutdown.modes.Shutdown') },
                  ]}
                />
              </Group>

              <Group align="flex-end" gap="sm" wrap="wrap">
                <NumberInput
                  label={t('shutdown.duration')}
                  value={minutes}
                  min={1}
                  max={120}
                  w={180}
                  onChange={(v) => setMinutes(Number(v) || 1)}
                />
                <Group gap={6}>
                  {presets.map((p) => (
                    <Button
                      key={p}
                      size="xs"
                      variant={minutes === p ? 'filled' : 'default'}
                      onClick={() => setMinutes(p)}
                    >
                      {t('shutdown.preset', { minutes: p })}
                    </Button>
                  ))}
                </Group>
              </Group>

              <TextInput
                label={t('shutdown.reason')}
                placeholder={t('shutdown.reasonPlaceholder')}
                value={reason}
                maxLength={200}
                onChange={(e) => setReason(e.currentTarget.value)}
              />

              <Text size="xs" c="dimmed">
                {t('shutdown.milestones', {
                  list: milestoneLabels(Math.max(1, Math.round(minutes)) * 60).join(', '),
                })}
              </Text>

              <Group justify="flex-end">
                <Button
                  color="red"
                  leftSection={<IconPower size={16} />}
                  disabled={!gameConnected}
                  onClick={() => {
                    startShutdown.reset()
                    setConfirmOpen(true)
                  }}
                >
                  {t('shutdown.schedule')}
                </Button>
              </Group>
            </Stack>
          )}
        </Stack>
      </Paper>

      <Paper withBorder p="md">
        <Stack gap="sm">
          <Group justify="space-between">
            <Group gap="xs">
              <IconTool size={18} />
              <Title order={4}>{t('maintenance.title')}</Title>
            </Group>
            {data && (
              <Badge color={data.maintenanceEnabled ? 'orange' : 'gray'} variant="light">
                {data.maintenanceEnabled ? t('maintenance.active') : t('maintenance.inactive')}
              </Badge>
            )}
          </Group>
          <Text size="sm" c="dimmed">
            {t('maintenance.description')}
          </Text>
          <ErrorAlert error={saveMaintenance.error} />
          <Switch
            label={t('maintenance.toggle')}
            checked={maintEnabled}
            onChange={(e) => {
              setMaintEnabled(e.currentTarget.checked)
              setMaintDirty(true)
            }}
          />
          <TextInput
            label={t('maintenance.message')}
            placeholder={t('maintenance.messagePlaceholder')}
            value={maintMessage}
            maxLength={255}
            onChange={(e) => {
              setMaintMessage(e.currentTarget.value)
              setMaintDirty(true)
            }}
          />
          {maintEnabled && gameConnected && (data?.onlineNonGmPlayers ?? 0) > 0 && (
            <Checkbox
              label={t('maintenance.kickNonGm', { count: data?.onlineNonGmPlayers ?? 0 })}
              checked={kickNonGm}
              onChange={(e) => setKickNonGm(e.currentTarget.checked)}
            />
          )}
          <Group justify="flex-end">
            <Button
              leftSection={<IconCheck size={16} />}
              loading={saveMaintenance.isPending}
              onClick={() => saveMaintenance.mutate()}
            >
              {t('maintenance.save')}
            </Button>
          </Group>
        </Stack>
      </Paper>

      <Paper withBorder p="md">
        <Stack gap="sm">
          <Group gap="xs">
            <IconMessage size={18} />
            <Title order={4}>{t('motd.title')}</Title>
          </Group>
          <Text size="sm" c="dimmed">
            {t('motd.description')}
          </Text>
          <ErrorAlert error={saveMotd.error} />
          <Textarea
            label={t('motd.label')}
            placeholder={t('motd.placeholder')}
            value={motdText}
            maxLength={1000}
            minRows={3}
            autosize
            onChange={(e) => {
              setMotdText(e.currentTarget.value)
              setMotdDirty(true)
            }}
          />
          {previewLines.length > 0 && (
            <Paper withBorder p="xs">
              <Text size="xs" fw={600} c="dimmed" mb={4}>
                {t('motd.preview')}
              </Text>
              <Stack gap={2}>
                {previewLines.map((line, i) => (
                  <Text key={i} size="xs" ff="monospace">
                    [SYSTEM] {line}
                  </Text>
                ))}
              </Stack>
            </Paper>
          )}
          <Group justify="space-between">
            <Text size="xs" c="dimmed">
              {motdText.trim().length} / 1000
            </Text>
            <Group gap="xs">
              <Button
                variant="default"
                disabled={motdText.trim().length === 0}
                loading={saveMotd.isPending}
                onClick={() => {
                  setMotdText('')
                  setMotdDirty(false)
                  saveMotd.mutate('')
                }}
              >
                {t('motd.clear')}
              </Button>
              <Button
                leftSection={<IconCheck size={16} />}
                loading={saveMotd.isPending}
                onClick={() => saveMotd.mutate(motdText)}
              >
                {t('motd.save')}
              </Button>
            </Group>
          </Group>
        </Stack>
      </Paper>

      <Modal
        opened={confirmOpen}
        onClose={() => setConfirmOpen(false)}
        title={t('shutdown.confirm.title', { mode: modeLabel.toLowerCase() })}
        size="sm"
      >
        <Stack gap="md">
          <ErrorAlert error={startShutdown.error} />
          <Text>
            {t('shutdown.confirm.body', {
              mode: modeLabel.toLowerCase(),
              minutes: Math.max(1, Math.round(minutes)),
              online: data?.onlinePlayers ?? 0,
            })}
          </Text>
          {reason.trim() && (
            <Text size="sm" c="dimmed">
              {t('shutdown.confirm.reason', { reason: reason.trim() })}
            </Text>
          )}
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setConfirmOpen(false)}>
              {t('shutdown.confirm.cancel')}
            </Button>
            <Button
              color="red"
              loading={startShutdown.isPending}
              onClick={() => startShutdown.mutate()}
            >
              {t('shutdown.confirm.submit')}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  )
}
