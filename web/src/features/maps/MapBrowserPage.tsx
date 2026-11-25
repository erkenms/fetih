import {
  Badge,
  Box,
  Card,
  Code,
  Group,
  Loader,
  Pagination,
  Paper,
  ScrollArea,
  SegmentedControl,
  SimpleGrid,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { IconLayoutGrid, IconList, IconMap, IconMap2 } from '@tabler/icons-react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { api, unwrap } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { AssetImage, assetUrl } from '../../components/pickers'

const pageSize = 36

export default function MapBrowserPage() {
  const { t } = useTranslation('maps')
  const navigate = useNavigate()
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [hasMinimap, setHasMinimap] = useState(false)
  const [hasTiles, setHasTiles] = useState(false)
  const [hasEntities, setHasEntities] = useState(false)
  const [viewMode, setViewMode] = useState<'grid' | 'table'>('grid')
  const [filters] = useDebouncedValue({ search, hasMinimap, hasTiles, hasEntities }, 250)

  const query = {
    page,
    pageSize,
    search: filters.search.trim() || undefined,
    hasMinimap: filters.hasMinimap ? true : undefined,
    hasTiles: filters.hasTiles ? true : undefined,
    hasEntities: filters.hasEntities ? true : undefined,
  }

  const maps = useQuery({
    queryKey: ['maps', 'list', query],
    queryFn: () => unwrap(api.GET('/api/maps', { params: { query } })),
    placeholderData: keepPreviousData,
  })

  const total = maps.data?.total ?? 0
  const openMap = (id: number) => navigate(`/admin/maps/${id}`)

  return (
    <Stack>
      <Group justify="space-between" wrap="wrap">
        <Group gap="xs">
          <IconMap2 size={24} />
          <Title order={2}>{t('browser.title')}</Title>
          {maps.isFetching && <Loader size="sm" />}
        </Group>
        <SegmentedControl
          size="xs"
          value={viewMode}
          onChange={(v) => setViewMode(v as 'grid' | 'table')}
          data={[
            {
              value: 'grid',
              label: (
                <Group gap={4} wrap="nowrap">
                  <IconLayoutGrid size={14} />
                  <span>{t('browser.viewGrid')}</span>
                </Group>
              ),
            },
            {
              value: 'table',
              label: (
                <Group gap={4} wrap="nowrap">
                  <IconList size={14} />
                  <span>{t('browser.viewTable')}</span>
                </Group>
              ),
            },
          ]}
        />
      </Group>

      <Text c="dimmed">{t('browser.description')}</Text>

      <Group align="center" wrap="wrap">
        <TextInput
          style={{ flex: 1, minWidth: 240 }}
          placeholder={t('browser.searchPlaceholder')}
          value={search}
          onChange={(e) => {
            setSearch(e.currentTarget.value)
            setPage(1)
          }}
        />
        <Switch
          label={t('browser.filterMinimap')}
          checked={hasMinimap}
          onChange={(e) => {
            setHasMinimap(e.currentTarget.checked)
            setPage(1)
          }}
        />
        <Switch
          label={t('browser.filterTiles')}
          checked={hasTiles}
          onChange={(e) => {
            setHasTiles(e.currentTarget.checked)
            setPage(1)
          }}
        />
        <Switch
          label={t('browser.filterEntities')}
          checked={hasEntities}
          onChange={(e) => {
            setHasEntities(e.currentTarget.checked)
            setPage(1)
          }}
        />
      </Group>

      <ErrorAlert error={maps.error} />

      {viewMode === 'grid' ? (
        <>
          <SimpleGrid cols={{ base: 1, sm: 2, md: 3, lg: 4 }} spacing="md">
            {maps.data?.items.map((m) => {
              const title = m.name ?? m.serverName ?? `#${m.id}`
              const thumb = assetUrl(m.minimap)
              return (
                <Card
                  key={m.id}
                  withBorder
                  padding="sm"
                  radius="md"
                  style={{ cursor: 'pointer' }}
                  onClick={() => openMap(m.id)}
                >
                  <Card.Section>
                    <Box
                      h={130}
                      bg="dark.7"
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        position: 'relative',
                        overflow: 'hidden',
                      }}
                    >
                      {thumb ? (
                        <img
                          src={thumb}
                          alt={title}
                          style={{ width: '100%', height: '100%', objectFit: 'cover' }}
                        />
                      ) : (
                        <IconMap size={36} opacity={0.35} />
                      )}
                      <Group pos="absolute" top={6} left={6} gap={4}>
                        <Badge size="xs" variant="filled" color="dark">
                          #{m.id}
                        </Badge>
                        {m.hasTiles && (
                          <Badge size="xs" variant="filled" color="teal">
                            {t('browser.tilesBadge')}
                          </Badge>
                        )}
                      </Group>
                    </Box>
                  </Card.Section>

                  <Stack gap={6} mt="sm">
                    <Group justify="space-between" gap="xs" wrap="nowrap">
                      <Text fw={600} lineClamp={1}>
                        {title}
                      </Text>
                      {m.serverName && m.name && (
                        <Code fz="xs">{m.serverName}</Code>
                      )}
                    </Group>

                    {m.aliases.length > 0 && (
                      <Group gap={4}>
                        {m.aliases.map((a) => (
                          <Badge key={a} size="xs" variant="light">
                            {a}
                          </Badge>
                        ))}
                      </Group>
                    )}

                    <Group gap="xs" c="dimmed" fz="xs">
                      {m.npcCount > 0 && <span>{t('browser.stats.npcs', { count: m.npcCount })}</span>}
                      {m.spawnCount > 0 && <span>{t('browser.stats.spawns', { count: m.spawnCount })}</span>}
                      {m.portalCount > 0 && <span>{t('browser.stats.portals', { count: m.portalCount })}</span>}
                    </Group>
                  </Stack>
                </Card>
              )
            })}
          </SimpleGrid>

          {maps.data && maps.data.items.length === 0 && (
            <Paper withBorder p="xl">
              <Text c="dimmed" ta="center">
                {t('browser.empty')}
              </Text>
            </Paper>
          )}
        </>
      ) : (
        <Paper withBorder>
          <ScrollArea>
            <Table highlightOnHover striped miw={820}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th w={64}>{t('browser.column.minimap')}</Table.Th>
                  <Table.Th>{t('browser.column.id')}</Table.Th>
                  <Table.Th>{t('browser.column.name')}</Table.Th>
                  <Table.Th>{t('browser.column.aliases')}</Table.Th>
                  <Table.Th>{t('browser.column.entities')}</Table.Th>
                  <Table.Th>{t('browser.column.flags')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {maps.data?.items.map((m) => (
                  <Table.Tr key={m.id} onClick={() => openMap(m.id)} style={{ cursor: 'pointer' }}>
                    <Table.Td>
                      <AssetImage path={m.minimap} alt={m.name ?? String(m.id)} size={40} fallback={<IconMap size={18} />} />
                    </Table.Td>
                    <Table.Td>
                      <Group gap={4}>
                        <Code>{m.id}</Code>
                        {m.hasTiles && (
                          <Badge size="xs" color="teal" variant="light">
                            {t('browser.tilesBadge')}
                          </Badge>
                        )}
                      </Group>
                    </Table.Td>
                    <Table.Td>
                      <Stack gap={2}>
                        <Text fw={500}>{m.name ?? m.serverName ?? '—'}</Text>
                        {m.serverName && m.name && <Code fz="xs">{m.serverName}</Code>}
                      </Stack>
                    </Table.Td>
                    <Table.Td>
                      <Group gap={4}>
                        {m.aliases.map((a) => (
                          <Badge key={a} size="xs" variant="light">
                            {a}
                          </Badge>
                        ))}
                      </Group>
                    </Table.Td>
                    <Table.Td>
                      <Group gap="xs" fz="xs" c="dimmed">
                        {m.npcCount > 0 && <span>{t('browser.stats.npcs', { count: m.npcCount })}</span>}
                        {m.spawnCount > 0 && <span>{t('browser.stats.spawns', { count: m.spawnCount })}</span>}
                        {m.portalCount > 0 && <span>{t('browser.stats.portals', { count: m.portalCount })}</span>}
                      </Group>
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs" c="dimmed" lineClamp={1}>
                        {m.flags.join(', ') || '—'}
                      </Text>
                    </Table.Td>
                  </Table.Tr>
                ))}
                {maps.data && maps.data.items.length === 0 && (
                  <Table.Tr>
                    <Table.Td colSpan={6}>
                      <Text c="dimmed" ta="center" py="md">
                        {t('browser.empty')}
                      </Text>
                    </Table.Td>
                  </Table.Tr>
                )}
              </Table.Tbody>
            </Table>
          </ScrollArea>
        </Paper>
      )}

      <Group justify="space-between">
        <Text size="sm" c="dimmed">
          {t('browser.total', { count: total })}
        </Text>
        <Pagination total={Math.max(1, Math.ceil(total / pageSize))} value={page} onChange={setPage} />
      </Group>
    </Stack>
  )
}
