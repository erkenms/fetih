import { Badge, Code, Group, Loader, Modal, Pagination, Paper, ScrollArea, Select, Stack, Switch, Table, Text, TextInput, Title } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { IconSparkles } from '@tabler/icons-react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap, type Schemas } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { AssetImage, formatSpellPower } from '../../components/pickers'

const pageSize = 50
type Spell = Schemas['SpellCatalogEntry']

export default function SpellsCatalogPage() {
  const { t } = useTranslation('catalogs')
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [sort, setSort] = useState<string | null>(null)
  const [xpOnly, setXpOnly] = useState(false)
  const [selected, setSelected] = useState<Spell | null>(null)
  const [filters] = useDebouncedValue({ search, sort, xpOnly }, 250)

  const query = {
    page,
    pageSize,
    search: filters.search.trim() || undefined,
    sort: filters.sort || undefined,
    xpOnly: filters.xpOnly ? true : undefined,
  }

  const spells = useQuery({
    queryKey: ['catalogs', 'spells', query],
    queryFn: () => unwrap(api.GET('/api/catalogs/spells', { params: { query } })),
    placeholderData: keepPreviousData,
  })

  const total = spells.data?.total ?? 0
  const sortOptions = (spells.data?.sorts ?? []).map((s) => ({ value: s, label: s }))

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={2}>{t('spells.title')}</Title>
        {spells.isFetching && <Loader size="sm" />}
      </Group>
      <Text c="dimmed">{t('spells.description')}</Text>

      <Group align="flex-end" wrap="wrap">
        <TextInput
          style={{ flex: 1, minWidth: 220 }}
          placeholder={t('spells.searchPlaceholder')}
          value={search}
          onChange={(e) => {
            setSearch(e.currentTarget.value)
            setPage(1)
          }}
        />
        <Select
          w={200}
          clearable
          searchable
          placeholder={t('spells.allSorts')}
          data={sortOptions}
          value={sort}
          onChange={(v) => {
            setSort(v)
            setPage(1)
          }}
        />
        <Switch
          label={t('spells.xpOnly')}
          checked={xpOnly}
          onChange={(e) => {
            setXpOnly(e.currentTarget.checked)
            setPage(1)
          }}
        />
      </Group>

      <ErrorAlert error={spells.error} />

      <Paper withBorder>
        <ScrollArea>
          <Table highlightOnHover striped miw={720}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th w={52}>{t('spells.column.icon')}</Table.Th>
                <Table.Th>{t('spells.column.type')}</Table.Th>
                <Table.Th>{t('spells.column.name')}</Table.Th>
                <Table.Th>{t('spells.column.sort')}</Table.Th>
                <Table.Th>{t('spells.column.levels')}</Table.Th>
                <Table.Th>{t('spells.column.flags')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {spells.data?.items.map((s) => (
                <Table.Tr key={s.type} onClick={() => setSelected(s)} style={{ cursor: 'pointer' }}>
                  <Table.Td>
                    <AssetImage path={s.icon} alt={s.name} size={32} fallback={<IconSparkles size={16} />} />
                  </Table.Td>
                  <Table.Td><Code>{s.type}</Code></Table.Td>
                  <Table.Td><Text fw={500}>{s.name}</Text></Table.Td>
                  <Table.Td><Badge variant="light">{s.sort}</Badge></Table.Td>
                  <Table.Td>{s.levels.length}</Table.Td>
                  <Table.Td>
                    <Group gap={4}>
                      {s.useXp === 1 && <Badge size="xs" color="violet">{t('spells.xp')}</Badge>}
                      {s.ground && <Badge size="xs" variant="outline">{t('spells.flags.ground')}</Badge>}
                      {s.multi && <Badge size="xs" variant="outline">{t('spells.flags.multi')}</Badge>}
                      {s.crime && <Badge size="xs" color="orange" variant="light">{t('spells.flags.crime')}</Badge>}
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
              {spells.data && spells.data.items.length === 0 && (
                <Table.Tr>
                  <Table.Td colSpan={6}>
                    <Text c="dimmed" ta="center" py="md">{t('spells.empty')}</Text>
                  </Table.Td>
                </Table.Tr>
              )}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      </Paper>

      <Group justify="space-between">
        <Text size="sm" c="dimmed">{t('spells.total', { count: total })}</Text>
        <Pagination total={Math.max(1, Math.ceil(total / pageSize))} value={page} onChange={setPage} />
      </Group>

      <Modal
        opened={!!selected}
        onClose={() => setSelected(null)}
        title={
          selected && (
            <Group gap="xs">
              <AssetImage path={selected.icon} alt={selected.name} size={28} fallback={<IconSparkles size={16} />} />
              <Text fw={600}>{selected.name} (#{selected.type})</Text>
            </Group>
          )
        }
        size="xl"
      >
        {selected && (
          <Stack gap="sm">
            <Group gap="xs">
              <Badge>{selected.sort}</Badge>
              {selected.useXp === 1 && <Badge color="violet">{t('spells.flags.xp')}</Badge>}
              {selected.ground && <Badge variant="outline">{t('spells.flags.ground')}</Badge>}
              {selected.multi && <Badge variant="outline">{t('spells.flags.multi')}</Badge>}
              {selected.crime && <Badge color="orange" variant="light">{t('spells.flags.crime')}</Badge>}
            </Group>
            <Text fw={600}>{t('spells.levels.title')}</Text>
            <ScrollArea>
              <Table striped withTableBorder miw={640}>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>{t('spells.levels.level')}</Table.Th>
                    <Table.Th>{t('spells.levels.needLevel')}</Table.Th>
                    <Table.Th>{t('spells.levels.mana')}</Table.Th>
                    <Table.Th>{t('spells.levels.stamina')}</Table.Th>
                    <Table.Th>{t('spells.levels.power')}</Table.Th>
                    <Table.Th>{t('spells.levels.range')}</Table.Th>
                    <Table.Th>{t('spells.levels.distance')}</Table.Th>
                    <Table.Th>{t('spells.levels.delay')}</Table.Th>
                    <Table.Th>{t('spells.levels.needExp')}</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {selected.levels.map((l) => (
                    <Table.Tr key={l.level}>
                      <Table.Td>{l.level}</Table.Td>
                      <Table.Td>{l.needLevel}</Table.Td>
                      <Table.Td>{l.useMana}</Table.Td>
                      <Table.Td>{l.useStamina}</Table.Td>
                      <Table.Td>{formatSpellPower(l.power)}</Table.Td>
                      <Table.Td>{l.range}</Table.Td>
                      <Table.Td>{l.distance}</Table.Td>
                      <Table.Td>{l.delayMilliseconds}</Table.Td>
                      <Table.Td>{l.needExperience.toLocaleString()}</Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </ScrollArea>
          </Stack>
        )}
      </Modal>
    </Stack>
  )
}
