import { Badge, Box, Code, Group, Loader, Modal, Pagination, Paper, ScrollArea, Stack, Switch, Table, Text, TextInput, Title } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { IconMap } from '@tabler/icons-react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap, type Schemas } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { AssetImage, assetUrl } from '../../components/pickers'

const pageSize = 50
type MapEntry = Schemas['MapCatalogEntry']

export default function MapsCatalogPage() {
  const { t } = useTranslation('catalogs')
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [hasMinimap, setHasMinimap] = useState(false)
  const [selected, setSelected] = useState<MapEntry | null>(null)
  const [filters] = useDebouncedValue({ search, hasMinimap }, 250)

  const query = {
    page,
    pageSize,
    search: filters.search.trim() || undefined,
    hasMinimap: filters.hasMinimap ? true : undefined,
  }

  const maps = useQuery({
    queryKey: ['catalogs', 'maps', query],
    queryFn: () => unwrap(api.GET('/api/catalogs/maps', { params: { query } })),
    placeholderData: keepPreviousData,
  })

  const total = maps.data?.total ?? 0

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={2}>{t('maps.title')}</Title>
        {maps.isFetching && <Loader size="sm" />}
      </Group>
      <Text c="dimmed">{t('maps.description')}</Text>

      <Group align="center" wrap="wrap">
        <TextInput
          style={{ flex: 1, minWidth: 220 }}
          placeholder={t('maps.searchPlaceholder')}
          value={search}
          onChange={(e) => {
            setSearch(e.currentTarget.value)
            setPage(1)
          }}
        />
        <Switch
          label={t('maps.hasMinimapOnly')}
          checked={hasMinimap}
          onChange={(e) => {
            setHasMinimap(e.currentTarget.checked)
            setPage(1)
          }}
        />
      </Group>

      <ErrorAlert error={maps.error} />

      <Paper withBorder>
        <ScrollArea>
          <Table highlightOnHover striped miw={760}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th w={64}>{t('maps.column.minimap')}</Table.Th>
                <Table.Th>{t('maps.column.id')}</Table.Th>
                <Table.Th>{t('maps.column.name')}</Table.Th>
                <Table.Th>{t('maps.column.serverName')}</Table.Th>
                <Table.Th>{t('maps.column.aliases')}</Table.Th>
                <Table.Th>{t('maps.column.flags')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {maps.data?.items.map((m) => (
                <Table.Tr key={m.id} onClick={() => setSelected(m)} style={{ cursor: 'pointer' }}>
                  <Table.Td>
                    <AssetImage path={m.minimap} alt={m.name ?? String(m.id)} size={40} fallback={<IconMap size={18} />} />
                  </Table.Td>
                  <Table.Td><Code>{m.id}</Code></Table.Td>
                  <Table.Td><Text fw={500}>{m.name ?? '—'}</Text></Table.Td>
                  <Table.Td>{m.serverName ? <Code>{m.serverName}</Code> : '—'}</Table.Td>
                  <Table.Td>
                    <Group gap={4}>
                      {m.aliases.map((a) => (
                        <Badge key={a} size="xs" variant="light">{a}</Badge>
                      ))}
                    </Group>
                  </Table.Td>
                  <Table.Td>
                    <Text size="xs" c="dimmed" lineClamp={1}>{m.flags ?? '—'}</Text>
                  </Table.Td>
                </Table.Tr>
              ))}
              {maps.data && maps.data.items.length === 0 && (
                <Table.Tr>
                  <Table.Td colSpan={6}>
                    <Text c="dimmed" ta="center" py="md">{t('maps.empty')}</Text>
                  </Table.Td>
                </Table.Tr>
              )}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      </Paper>

      <Group justify="space-between">
        <Text size="sm" c="dimmed">{t('maps.total', { count: total })}</Text>
        <Pagination total={Math.max(1, Math.ceil(total / pageSize))} value={page} onChange={setPage} />
      </Group>

      <Modal
        opened={!!selected}
        onClose={() => setSelected(null)}
        title={selected ? `${selected.name ?? selected.serverName ?? selected.id} (#${selected.id})` : ''}
        size="lg"
      >
        {selected && (
          <Stack gap="sm">
            {assetUrl(selected.minimap) ? (
              <Box
                component="img"
                src={assetUrl(selected.minimap)!}
                alt={selected.name ?? String(selected.id)}
                mah={320}
                style={{ objectFit: 'contain', width: '100%', borderRadius: 'var(--mantine-radius-sm)' }}
              />
            ) : (
              <Text c="dimmed" size="sm">{t('maps.detail.noMinimap')}</Text>
            )}
            <Group gap="xs">
              {selected.aliases.map((a) => <Badge key={a} variant="light">{a}</Badge>)}
            </Group>
            <Text size="sm">
              <Text span fw={600}>{t('maps.detail.file')}: </Text>
              <Code>{selected.file ?? '—'}</Code>
            </Text>
            <Text size="sm">
              <Text span fw={600}>{t('maps.detail.puzzleSize')}: </Text>
              {selected.puzzleSize ?? '—'}
            </Text>
            <Text size="sm">
              <Text span fw={600}>{t('maps.detail.rebornMap')}: </Text>
              {selected.rebornMap ?? '—'}
            </Text>
            <Text size="sm">
              <Text span fw={600}>{t('maps.detail.flags')}: </Text>
              {selected.flags ?? '—'}
            </Text>
          </Stack>
        )}
      </Modal>
    </Stack>
  )
}
