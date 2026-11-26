import { Badge, Code, Divider, Group, Loader, Modal, NumberInput, Pagination, Paper, ScrollArea, SegmentedControl, SimpleGrid, Stack, Switch, Table, Text, TextInput, Title } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap, type Schemas } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { LookSprite, monsterLook, useLooks } from '../../components/looks'

const pageSize = 50
type Monster = Schemas['MonsterCatalogEntry']

export default function MonstersCatalogPage() {
  const { t } = useTranslation('catalogs')
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [source, setSource] = useState('all')
  const [bossOnly, setBossOnly] = useState(false)
  const [minLevel, setMinLevel] = useState<number | string>('')
  const [maxLevel, setMaxLevel] = useState<number | string>('')
  const [selected, setSelected] = useState<Monster | null>(null)
  const looks = useLooks()
  const [filters] = useDebouncedValue({ search, source, bossOnly, minLevel, maxLevel }, 250)

  const query = {
    page,
    pageSize,
    search: filters.search.trim() || undefined,
    source: filters.source === 'all' ? undefined : filters.source,
    boss: filters.bossOnly ? true : undefined,
    minLevel: typeof filters.minLevel === 'number' ? filters.minLevel : undefined,
    maxLevel: typeof filters.maxLevel === 'number' ? filters.maxLevel : undefined,
  }

  const monsters = useQuery({
    queryKey: ['catalogs', 'monsters', query],
    queryFn: () => unwrap(api.GET('/api/catalogs/monsters', { params: { query } })),
    placeholderData: keepPreviousData,
  })

  const total = monsters.data?.total ?? 0

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={2}>{t('monsters.title')}</Title>
        {monsters.isFetching && <Loader size="sm" />}
      </Group>
      <Text c="dimmed">{t('monsters.description')}</Text>

      <Group align="flex-end" wrap="wrap">
        <TextInput
          style={{ flex: 1, minWidth: 220 }}
          placeholder={t('monsters.searchPlaceholder')}
          value={search}
          onChange={(e) => {
            setSearch(e.currentTarget.value)
            setPage(1)
          }}
        />
        <NumberInput
          w={110}
          placeholder={t('monsters.minLevel')}
          min={0}
          value={minLevel}
          onChange={(v) => {
            setMinLevel(v)
            setPage(1)
          }}
        />
        <NumberInput
          w={110}
          placeholder={t('monsters.maxLevel')}
          min={0}
          value={maxLevel}
          onChange={(v) => {
            setMaxLevel(v)
            setPage(1)
          }}
        />
        <SegmentedControl
          value={source}
          onChange={(v) => {
            setSource(v)
            setPage(1)
          }}
          data={[
            { label: t('monsters.source.all'), value: 'all' },
            { label: t('monsters.source.server'), value: 'server' },
            { label: t('monsters.source.client'), value: 'client' },
          ]}
        />
        <Switch
          label={t('monsters.bossOnly')}
          checked={bossOnly}
          onChange={(e) => {
            setBossOnly(e.currentTarget.checked)
            setPage(1)
          }}
        />
      </Group>

      <ErrorAlert error={monsters.error} />

      <Paper withBorder>
        <ScrollArea>
          <Table highlightOnHover striped miw={760}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('monsters.column.id')}</Table.Th>
                <Table.Th>{t('monsters.column.name')}</Table.Th>
                <Table.Th>{t('monsters.column.level')}</Table.Th>
                <Table.Th>{t('monsters.column.life')}</Table.Th>
                <Table.Th>{t('monsters.column.attack')}</Table.Th>
                <Table.Th>{t('monsters.column.lookface')}</Table.Th>
                <Table.Th>{t('monsters.column.source')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {monsters.data?.items.map((m) => (
                <Table.Tr key={m.id} onClick={() => setSelected(m)} style={{ cursor: 'pointer' }}>
                  <Table.Td><Code>{m.id}</Code></Table.Td>
                  <Table.Td>
                    <Group gap="xs" wrap="nowrap">
                      <LookSprite look={monsterLook(looks.index, m.lookface)} alt={m.name} size={32} />
                      <Text fw={500}>{m.name}</Text>
                      {m.boss && <Badge size="xs" color="red" variant="light">{t('monsters.boss')}</Badge>}
                    </Group>
                  </Table.Td>
                  <Table.Td>{m.level ?? m.clientLevel ?? '—'}</Table.Td>
                  <Table.Td>{(m.life ?? m.clientMaxLife)?.toLocaleString() ?? '—'}</Table.Td>
                  <Table.Td>
                    {m.attackMin != null && m.attackMax != null ? `${m.attackMin}–${m.attackMax}` : '—'}
                  </Table.Td>
                  <Table.Td>{m.lookface ?? '—'}</Table.Td>
                  <Table.Td>
                    <Group gap={4}>
                      {m.inServer && <Badge size="xs" variant="light">{t('monsters.source.server')}</Badge>}
                      {m.inClient && <Badge size="xs" variant="outline">{t('monsters.source.client')}</Badge>}
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
              {monsters.data && monsters.data.items.length === 0 && (
                <Table.Tr>
                  <Table.Td colSpan={7}>
                    <Text c="dimmed" ta="center" py="md">{t('monsters.empty')}</Text>
                  </Table.Td>
                </Table.Tr>
              )}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      </Paper>

      <Group justify="space-between">
        <Text size="sm" c="dimmed">{t('monsters.total', { count: total })}</Text>
        <Pagination total={Math.max(1, Math.ceil(total / pageSize))} value={page} onChange={setPage} />
      </Group>

      <Modal opened={!!selected} onClose={() => setSelected(null)} title={selected ? `${selected.name} (#${selected.id})` : ''} size="lg">
        {selected && (
          <Stack gap="sm">
            {monsterLook(looks.index, selected.lookface) && (
              <Group justify="center">
                <LookSprite look={monsterLook(looks.index, selected.lookface)} alt={selected.name} size={180} />
              </Group>
            )}
            <Text fw={600}>{t('monsters.detail.serverStats')}</Text>
            <SimpleGrid cols={{ base: 2, sm: 3 }} spacing="xs">
              <Stat label={t('monsters.column.level')} value={selected.level} />
              <Stat label={t('monsters.column.life')} value={selected.life?.toLocaleString()} />
              <Stat
                label={t('monsters.column.attack')}
                value={selected.attackMin != null ? `${selected.attackMin}–${selected.attackMax}` : null}
              />
              <Stat label={t('monsters.detail.defense')} value={selected.defense} />
              <Stat label={t('monsters.detail.dexterity')} value={selected.dexterity} />
              <Stat label={t('monsters.detail.dodge')} value={selected.dodge} />
              <Stat label={t('monsters.detail.attackRange')} value={selected.attackRange} />
              <Stat label={t('monsters.detail.viewRange')} value={selected.viewRange} />
              <Stat label={t('monsters.detail.attackSpeed')} value={selected.attackSpeed} />
              <Stat label={t('monsters.detail.moveSpeed')} value={selected.moveSpeed} />
              <Stat label={t('monsters.detail.dropMoney')} value={selected.dropMoney} />
              <Stat label={t('monsters.detail.magicType')} value={selected.magicType} />
              <Stat label={t('monsters.detail.magicHitRate')} value={selected.magicHitRate} />
            </SimpleGrid>

            {selected.inClient && (
              <>
                <Divider />
                <Text fw={600}>{t('monsters.detail.clientStats')}</Text>
                <SimpleGrid cols={{ base: 2, sm: 3 }} spacing="xs">
                  <Stat label={t('monsters.detail.clientLevel')} value={selected.clientLevel} />
                  <Stat label={t('monsters.detail.clientMaxLife')} value={selected.clientMaxLife?.toLocaleString()} />
                  <Stat label={t('monsters.detail.zoomPercent')} value={selected.zoomPercent} />
                </SimpleGrid>
              </>
            )}
          </Stack>
        )}
      </Modal>
    </Stack>
  )
}

function Stat({ label, value }: { label: string; value: string | number | null | undefined }) {
  return (
    <Paper withBorder p="xs">
      <Text size="xs" c="dimmed">{label}</Text>
      <Text fw={600} size="sm">{value ?? '—'}</Text>
    </Paper>
  )
}
