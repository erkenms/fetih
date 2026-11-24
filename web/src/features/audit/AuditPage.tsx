import { Code, Group, Loader, Modal, Pagination, Paper, ScrollArea, Stack, Table, Text, TextInput, Title } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap, type Schemas } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'

const pageSize = 50
type Entry = Schemas['AuditEntry']

function pretty(json: string | null) {
  if (!json) return '—'
  try {
    return JSON.stringify(JSON.parse(json), null, 2)
  } catch {
    return json
  }
}

export default function AuditPage() {
  const { t, i18n } = useTranslation('audit')
  const [page, setPage] = useState(1)
  const [action, setAction] = useState('')
  const [target, setTarget] = useState('')
  const [account, setAccount] = useState('')
  const [selected, setSelected] = useState<Entry | null>(null)
  const [filters] = useDebouncedValue({ action, target, account }, 300)

  const accountId = /^\d+$/.test(filters.account.trim()) ? Number(filters.account.trim()) : undefined
  const query = {
    page,
    pageSize,
    action: filters.action.trim() || undefined,
    targetId: filters.target.trim() || undefined,
    accountId,
  }
  const audit = useQuery({
    queryKey: ['audit', query],
    queryFn: () => unwrap(api.GET('/api/audit', { params: { query } })),
    placeholderData: keepPreviousData,
  })

  const total = audit.data?.total ?? 0
  const reset = (set: (v: string) => void) => (v: string) => {
    set(v)
    setPage(1)
  }

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={2}>{t('title')}</Title>
        {audit.isFetching && <Loader size="sm" />}
      </Group>
      <Text c="dimmed">{t('description')}</Text>
      <Group grow>
        <TextInput label={t('filter.action')} placeholder="character." value={action} onChange={(e) => reset(setAction)(e.currentTarget.value)} />
        <TextInput label={t('filter.target')} value={target} onChange={(e) => reset(setTarget)(e.currentTarget.value)} />
        <TextInput label={t('filter.account')} value={account} onChange={(e) => reset(setAccount)(e.currentTarget.value)} />
      </Group>
      <ErrorAlert error={audit.error} />
      <Paper withBorder>
        <ScrollArea>
          <Table highlightOnHover striped miw={800}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('column.time')}</Table.Th>
                <Table.Th>{t('column.who')}</Table.Th>
                <Table.Th>{t('column.action')}</Table.Th>
                <Table.Th>{t('column.target')}</Table.Th>
                <Table.Th>{t('column.summary')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {audit.data?.items.map((entry) => (
                <Table.Tr key={entry.id} onClick={() => setSelected(entry)} style={{ cursor: 'pointer' }}>
                  <Table.Td>{new Date(entry.timestamp).toLocaleString(i18n.language)}</Table.Td>
                  <Table.Td>
                    {entry.username} <Text span c="dimmed" size="xs">#{entry.accountId}</Text>
                  </Table.Td>
                  <Table.Td>
                    <Code>{entry.action}</Code>
                  </Table.Td>
                  <Table.Td>{entry.targetType ? `${entry.targetType} ${entry.targetId ?? ''}` : '—'}</Table.Td>
                  <Table.Td>{entry.summary ?? ''}</Table.Td>
                </Table.Tr>
              ))}
              {audit.data && audit.data.items.length === 0 && (
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
      <Group justify="space-between">
        <Text size="sm" c="dimmed">
          {t('total', { count: total })}
        </Text>
        <Pagination total={Math.max(1, Math.ceil(total / pageSize))} value={page} onChange={setPage} />
      </Group>

      <Modal opened={!!selected} onClose={() => setSelected(null)} title={selected?.action} size="lg">
        {selected && (
          <Stack gap="xs">
            <Text size="sm">
              {t('column.who')}: {selected.username} #{selected.accountId} ({selected.ipAddress ?? '?'})
            </Text>
            <Text size="sm">{new Date(selected.timestamp).toLocaleString(i18n.language)}</Text>
            {selected.summary && <Text>{selected.summary}</Text>}
            <Text fw={600} mt="sm">{t('before')}</Text>
            <Code block>{pretty(selected.before)}</Code>
            <Text fw={600}>{t('after')}</Text>
            <Code block>{pretty(selected.after)}</Code>
          </Stack>
        )}
      </Modal>
    </Stack>
  )
}
