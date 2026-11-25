import {
  Alert,
  Anchor,
  Badge,
  Button,
  Code,
  Group,
  Loader,
  Modal,
  Paper,
  ScrollArea,
  Select,
  SimpleGrid,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { IconAlertTriangle, IconExternalLink, IconInfoCircle, IconRefresh } from '@tabler/icons-react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { ApiError, api, unwrap, type Schemas } from '../../api/client'
import {
  commandResultColor,
  prettyJson,
  rangeWindow,
  timeRangePresets,
  type TimeRangePreset,
} from './chatMeta'

type AuditEntry = Schemas['AuditEntry']

export function GmCommandsTab() {
  const { t, i18n } = useTranslation('chat')
  const [preset, setPreset] = useState<TimeRangePreset>('24h')
  const [character, setCharacter] = useState('')
  const [account, setAccount] = useState('')
  const [command, setCommand] = useState('')
  const [words, setWords] = useState('')
  const [refreshTick, setRefreshTick] = useState(0)
  const [selectedAudit, setSelectedAudit] = useState<AuditEntry | null>(null)
  const [debounced] = useDebouncedValue({ character, account, command, words }, 300)

  const window = useMemo(() => rangeWindow(preset), [preset, refreshTick])

  const query = {
    character: debounced.character.trim() || undefined,
    account: debounced.account.trim() || undefined,
    command: debounced.command.trim() || undefined,
    words: debounced.words.trim() || undefined,
    from: window.from,
    to: window.to,
    limit: 200,
  }

  const gm = useQuery({
    queryKey: ['chat', 'gm-commands', query],
    queryFn: () => unwrap(api.GET('/api/chat/gm-commands', { params: { query } })),
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
        <TextInput
          label={t('filter.character')}
          placeholder={t('filter.characterPlaceholder')}
          value={character}
          onChange={(e) => setCharacter(e.currentTarget.value)}
          w={160}
        />
        <TextInput
          label={t('filter.account')}
          placeholder={t('filter.accountPlaceholder')}
          value={account}
          onChange={(e) => setAccount(e.currentTarget.value)}
          w={160}
        />
        <TextInput
          label={t('filter.command')}
          placeholder={t('filter.commandPlaceholder')}
          value={command}
          onChange={(e) => setCommand(e.currentTarget.value)}
          w={150}
        />
        <TextInput
          label={t('filter.words')}
          placeholder={t('filter.wordsPlaceholder')}
          value={words}
          onChange={(e) => setWords(e.currentTarget.value)}
          style={{ flex: 1, minWidth: 160 }}
        />
      </Group>

      <Group justify="space-between" wrap="wrap">
        <Button
          variant="default"
          size="xs"
          leftSection={<IconRefresh size={14} />}
          onClick={() => setRefreshTick((n) => n + 1)}
          loading={gm.isFetching}
        >
          {t('actions.refresh')}
        </Button>
        {gm.data && (
          <Button
            component="a"
            href={gm.data.grafanaExploreUrl}
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

      {gm.error && (
        <Alert color="red" icon={<IconAlertTriangle size={18} />}>
          {errorText(gm.error)}
        </Alert>
      )}

      {gm.data && !gm.data.lokiAvailable && (
        <Alert color="yellow" icon={<IconAlertTriangle size={18} />}>
          {t('gm.lokiUnavailable')}
        </Alert>
      )}

      {gm.data?.truncated && (
        <Alert color="yellow">{t('truncated', { count: gm.data.commands.length })}</Alert>
      )}

      <SimpleGrid cols={{ base: 1, xl: 2 }} spacing="md">
        <Paper withBorder p="sm">
          <Group justify="space-between" mb="xs">
            <Title order={4}>{t('gm.inGameTitle')}</Title>
            {gm.data && (
              <Text size="xs" c="dimmed">
                {t('count', { count: gm.data.commands.length })}
              </Text>
            )}
          </Group>
          <ScrollArea>
            <Table highlightOnHover striped miw={480}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('column.time')}</Table.Th>
                  <Table.Th>{t('column.gm')}</Table.Th>
                  <Table.Th>{t('column.command')}</Table.Th>
                  <Table.Th>{t('column.result')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {gm.isPending && (
                  <Table.Tr>
                    <Table.Td colSpan={4}>
                      <Group justify="center" py="md">
                        <Loader size="sm" />
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                )}
                {gm.data?.commands.map((entry, idx) => (
                  <Table.Tr key={`${entry.timestamp}-${entry.command}-${idx}`}>
                    <Table.Td style={{ whiteSpace: 'nowrap' }}>
                      {new Date(entry.timestamp).toLocaleString(i18n.language)}
                    </Table.Td>
                    <Table.Td style={{ whiteSpace: 'nowrap' }}>
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
                          <Anchor component={Link} to={`/admin/players/${entry.accountId}`} size="xs" c="dimmed">
                            #{entry.accountId}
                          </Anchor>
                        )}
                      </Group>
                    </Table.Td>
                    <Table.Td>
                      <Code>{entry.text}</Code>
                    </Table.Td>
                    <Table.Td>
                      <Badge variant="light" size="sm" color={commandResultColor(entry.result)}>
                        {entry.result ?? '—'}
                      </Badge>
                    </Table.Td>
                  </Table.Tr>
                ))}
                {gm.data && gm.data.commands.length === 0 && (
                  <Table.Tr>
                    <Table.Td colSpan={4}>
                      <Text c="dimmed" ta="center" py="md">
                        {t('emptyCommands')}
                      </Text>
                    </Table.Td>
                  </Table.Tr>
                )}
              </Table.Tbody>
            </Table>
          </ScrollArea>
        </Paper>

        <Paper withBorder p="sm">
          <Group justify="space-between" mb="xs">
            <Title order={4}>{t('gm.webAuditTitle')}</Title>
            {gm.data?.canViewAudit && (
              <Text size="xs" c="dimmed">
                {t('count', { count: gm.data.audit.length })}
              </Text>
            )}
          </Group>
          {gm.data && !gm.data.canViewAudit ? (
            <Alert color="gray" icon={<IconInfoCircle size={18} />}>
              {t('gm.auditPermissionNote')}
            </Alert>
          ) : (
            <ScrollArea>
              <Table highlightOnHover striped miw={480}>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>{t('column.time')}</Table.Th>
                    <Table.Th>{t('column.who')}</Table.Th>
                    <Table.Th>{t('column.action')}</Table.Th>
                    <Table.Th>{t('column.summary')}</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {gm.isPending && (
                    <Table.Tr>
                      <Table.Td colSpan={4}>
                        <Group justify="center" py="md">
                          <Loader size="sm" />
                        </Group>
                      </Table.Td>
                    </Table.Tr>
                  )}
                  {gm.data?.audit.map((entry) => (
                    <Table.Tr
                      key={entry.id}
                      onClick={() => setSelectedAudit(entry)}
                      style={{ cursor: 'pointer' }}
                    >
                      <Table.Td style={{ whiteSpace: 'nowrap' }}>
                        {new Date(entry.timestamp).toLocaleString(i18n.language)}
                      </Table.Td>
                      <Table.Td style={{ whiteSpace: 'nowrap' }} onClick={(e) => e.stopPropagation()}>
                        <Anchor component={Link} to={`/admin/players/${entry.accountId}`} size="sm">
                          {entry.username}
                        </Anchor>{' '}
                        <Text span c="dimmed" size="xs">
                          #{entry.accountId}
                        </Text>
                      </Table.Td>
                      <Table.Td>
                        <Code>{entry.action}</Code>
                      </Table.Td>
                      <Table.Td>{entry.summary ?? (entry.targetType ? `${entry.targetType} ${entry.targetId ?? ''}` : '—')}</Table.Td>
                    </Table.Tr>
                  ))}
                  {gm.data && gm.data.canViewAudit && gm.data.audit.length === 0 && (
                    <Table.Tr>
                      <Table.Td colSpan={4}>
                        <Text c="dimmed" ta="center" py="md">
                          {t('emptyAudit')}
                        </Text>
                      </Table.Td>
                    </Table.Tr>
                  )}
                </Table.Tbody>
              </Table>
            </ScrollArea>
          )}
        </Paper>
      </SimpleGrid>

      <Modal
        opened={!!selectedAudit}
        onClose={() => setSelectedAudit(null)}
        title={selectedAudit?.action}
        size="lg"
      >
        {selectedAudit && (
          <Stack gap="xs">
            <Text size="sm">
              {t('column.who')}: {selectedAudit.username} #{selectedAudit.accountId} ({selectedAudit.ipAddress ?? '?'})
            </Text>
            <Text size="sm">{new Date(selectedAudit.timestamp).toLocaleString(i18n.language)}</Text>
            {selectedAudit.summary && <Text>{selectedAudit.summary}</Text>}
            <Text fw={600} mt="sm">
              {t('detail.before')}
            </Text>
            <Code block>{prettyJson(selectedAudit.before)}</Code>
            <Text fw={600}>{t('detail.after')}</Text>
            <Code block>{prettyJson(selectedAudit.after)}</Code>
          </Stack>
        )}
      </Modal>
    </Stack>
  )
}
