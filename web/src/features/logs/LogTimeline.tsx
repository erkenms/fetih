import {
  Alert,
  Badge,
  Button,
  Code,
  Group,
  Loader,
  Modal,
  Paper,
  ScrollArea,
  Select,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
} from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { IconAlertTriangle, IconExternalLink, IconRefresh } from '@tabler/icons-react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { useTranslation } from 'react-i18next'
import { api, unwrap, type Schemas } from '../../api/client'
import { useErrorText } from '../../app/ErrorAlert'
import { PacketDecodeView, packetSourceFromEntry } from '../packets'
import { levelColor, rangeWindow, timeRangePresets, type TimeRangePreset } from './timeline'

type Entry = Schemas['LogEntry']

export interface LogTimelineProps {
  characterId?: number
  accountId?: number
  compact?: boolean
}

export function LogTimeline({ characterId, accountId, compact = false }: LogTimelineProps) {
  const { t, i18n } = useTranslation('logs')
  const [preset, setPreset] = useState<TimeRangePreset>('24h')
  const [server, setServer] = useState<string>('')
  const [level, setLevel] = useState<string>('')
  const [eventPrefix, setEventPrefix] = useState('')
  const [search, setSearch] = useState('')
  const [includePackets, setIncludePackets] = useState(false)
  const [refreshTick, setRefreshTick] = useState(0)
  const [selected, setSelected] = useState<Entry | null>(null)
  const [debounced] = useDebouncedValue({ eventPrefix, search }, 300)

  const window = useMemo(() => rangeWindow(preset), [preset, refreshTick])

  const query = {
    characterId,
    accountId,
    server: server || undefined,
    level: level || undefined,
    eventPrefix: debounced.eventPrefix.trim() || undefined,
    search: debounced.search.trim() || undefined,
    includePackets,
    from: window.from,
    to: window.to,
    limit: compact ? 100 : 200,
  }

  const timeline = useQuery({
    queryKey: ['logs', 'timeline', query],
    queryFn: () => unwrap(api.GET('/api/logs/timeline', { params: { query } })),
    placeholderData: keepPreviousData,
  })

  const errorText = useErrorText('logs')
  const selectedPacket = selected ? packetSourceFromEntry(selected) : null

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
          label={t('filter.server')}
          value={server}
          onChange={(v) => setServer(v ?? '')}
          data={[
            { value: '', label: t('filter.all') },
            { value: 'game', label: 'game' },
            { value: 'account', label: 'account' },
            { value: 'web', label: 'web' },
          ]}
          w={130}
        />
        <Select
          label={t('filter.level')}
          value={level}
          onChange={(v) => setLevel(v ?? '')}
          data={[
            { value: '', label: t('filter.all') },
            { value: 'info', label: 'info' },
            { value: 'warn', label: 'warn' },
            { value: 'error', label: 'error' },
            { value: 'debug', label: 'debug' },
          ]}
          w={120}
        />
        <TextInput
          label={t('filter.eventPrefix')}
          placeholder="item."
          value={eventPrefix}
          onChange={(e) => setEventPrefix(e.currentTarget.value)}
          w={150}
        />
        <TextInput
          label={t('filter.search')}
          value={search}
          onChange={(e) => setSearch(e.currentTarget.value)}
          style={{ flex: 1, minWidth: 160 }}
        />
        <Switch
          label={t('filter.includePackets')}
          checked={includePackets}
          onChange={(e) => setIncludePackets(e.currentTarget.checked)}
          mb={6}
        />
      </Group>

      <Group justify="space-between" wrap="wrap">
        <Group gap="xs">
          <Button
            variant="default"
            size="xs"
            leftSection={<IconRefresh size={14} />}
            onClick={() => setRefreshTick((n) => n + 1)}
            loading={timeline.isFetching}
          >
            {t('actions.refresh')}
          </Button>
          {timeline.data && (
            <Text size="sm" c="dimmed">
              {t('count', { count: timeline.data.entries.length })}
            </Text>
          )}
        </Group>
        {timeline.data && (
          <Group gap="xs">
            <Button
              component="a"
              href={timeline.data.grafanaUrl}
              target="_blank"
              rel="noreferrer"
              variant="light"
              size="xs"
              rightSection={<IconExternalLink size={14} />}
            >
              {t('actions.openGrafana')}
            </Button>
            <Button
              component="a"
              href={timeline.data.grafanaExploreUrl}
              target="_blank"
              rel="noreferrer"
              variant="subtle"
              size="xs"
              rightSection={<IconExternalLink size={14} />}
            >
              {t('actions.exploreGrafana')}
            </Button>
          </Group>
        )}
      </Group>

      {timeline.error && (
        <Alert color="red" icon={<IconAlertTriangle size={18} />}>
          {errorText(timeline.error)}
        </Alert>
      )}

      {timeline.data?.truncated && (
        <Alert color="yellow">{t('truncated', { count: timeline.data.entries.length })}</Alert>
      )}

      <Paper withBorder>
        <ScrollArea>
          <Table highlightOnHover striped miw={720}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('column.time')}</Table.Th>
                <Table.Th>{t('column.server')}</Table.Th>
                <Table.Th>{t('column.level')}</Table.Th>
                <Table.Th>{t('column.event')}</Table.Th>
                <Table.Th>{t('column.message')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {timeline.isPending && (
                <Table.Tr>
                  <Table.Td colSpan={5}>
                    <Group justify="center" py="md">
                      <Loader size="sm" />
                    </Group>
                  </Table.Td>
                </Table.Tr>
              )}
              {timeline.data?.entries.map((entry, idx) => (
                <Table.Tr
                  key={`${entry.timestamp}-${entry.event}-${idx}`}
                  onClick={() => setSelected(entry)}
                  style={{ cursor: 'pointer' }}
                >
                  <Table.Td style={{ whiteSpace: 'nowrap' }}>
                    {new Date(entry.timestamp).toLocaleString(i18n.language)}
                  </Table.Td>
                  <Table.Td>
                    <Badge variant="light" color="gray" size="sm">
                      {entry.server || '—'}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    <Badge variant="light" color={levelColor(entry.level)} size="sm">
                      {entry.level || '—'}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    <Code>{entry.event || '—'}</Code>
                  </Table.Td>
                  <Table.Td>{entry.message}</Table.Td>
                </Table.Tr>
              ))}
              {timeline.data && timeline.data.entries.length === 0 && (
                <Table.Tr>
                  <Table.Td colSpan={5}>
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

      <Modal opened={!!selected} onClose={() => setSelected(null)} title={selected?.event || t('detail.title')} size={selectedPacket ? 'xl' : 'lg'}>
        {selected && (
          <Stack gap="xs">
            <Group gap="xs">
              <Badge variant="light" color="gray">
                {selected.server}
              </Badge>
              <Badge variant="light" color={levelColor(selected.level)}>
                {selected.level}
              </Badge>
              <Text size="sm" c="dimmed">
                {new Date(selected.timestamp).toLocaleString(i18n.language)}
              </Text>
            </Group>
            <Text>{selected.message}</Text>
            {selectedPacket && (
              <>
                <Group justify="space-between" mt="xs">
                  <Text fw={600}>{t('detail.packet')}</Text>
                  <Button component={Link} to="/admin/packets" state={{ packet: selectedPacket }} variant="subtle" size="xs">
                    {t('packets:openDecoder')}
                  </Button>
                </Group>
                <PacketDecodeView source={selectedPacket} />
              </>
            )}
            {selected.characterId != null && (
              <Text size="sm">
                {t('detail.characterId')}: {selected.characterId}
              </Text>
            )}
            {selected.accountId != null && (
              <Text size="sm">
                {t('detail.accountId')}: {selected.accountId}
              </Text>
            )}
            {selected.source && (
              <Text size="sm">
                {t('detail.source')}: <Code>{selected.source}</Code>
              </Text>
            )}
            {Object.keys(selected.fields).length > 0 && (
              <>
                <Text fw={600} mt="xs">
                  {t('detail.fields')}
                </Text>
                <Code block>{JSON.stringify(selected.fields, null, 2)}</Code>
              </>
            )}
            {selected.exception && (
              <>
                <Text fw={600} mt="xs" c="red">
                  {t('detail.exception')}
                </Text>
                <Code block>{selected.exception}</Code>
              </>
            )}
          </Stack>
        )}
      </Modal>
    </Stack>
  )
}
