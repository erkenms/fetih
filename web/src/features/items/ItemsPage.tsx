import {
  Alert,
  Badge,
  Button,
  Code,
  CopyButton,
  Group,
  Loader,
  Modal,
  NumberInput,
  Pagination,
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
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { IconAlertTriangle, IconCheck, IconCopy, IconSearch } from '@tabler/icons-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import {
  ItemIcon,
  ItemTooltip,
  ItemTooltipContent,
  qualityBadgeColor,
  qualityColor,
  type CatalogItem,
  type ItemCategory,
  type ItemQuality,
} from '../../components/items'

const pageSize = 50

const categories: ItemCategory[] = [
  'Weapon',
  'Armor',
  'Headwear',
  'Necklace',
  'Ring',
  'Boots',
  'Shield',
  'Garment',
  'Steed',
  'Crop',
  'Bottle',
  'Arrow',
  'Gem',
  'Consumable',
  'Other',
]

const qualities: ItemQuality[] = ['Normal', 'Refined', 'Unique', 'Elite', 'Super', 'None']

const professionLines = [
  { value: '10', key: 'profession.line.1' },
  { value: '20', key: 'profession.line.2' },
  { value: '40', key: 'profession.line.4' },
  { value: '50', key: 'profession.line.5' },
  { value: '60', key: 'profession.line.6' },
  { value: '70', key: 'profession.line.7' },
  { value: '100', key: 'profession.line.10' },
]

function statsSummary(item: CatalogItem): string {
  const parts: string[] = []
  if ((item.minAttack ?? 0) > 0 || (item.maxAttack ?? 0) > 0) {
    parts.push(`Atk ${item.minAttack ?? 0}–${item.maxAttack ?? 0}`)
  }
  if ((item.magicAttack ?? 0) > 0) parts.push(`MAtk +${item.magicAttack}`)
  if ((item.defense ?? 0) > 0) parts.push(`Def +${item.defense}`)
  if ((item.magicDefense ?? 0) > 0) parts.push(`MDef +${item.magicDefense}`)
  if ((item.life ?? 0) > 0) parts.push(`HP +${item.life}`)
  if ((item.mana ?? 0) > 0) parts.push(`MP +${item.mana}`)
  return parts.join(', ') || '—'
}

export default function ItemsPage() {
  const { t } = useTranslation('items')
  const [page, setPage] = useState(1)
  const [q, setQ] = useState('')
  const [category, setCategory] = useState<ItemCategory | null>(null)
  const [quality, setQuality] = useState<ItemQuality | null>(null)
  const [profession, setProfession] = useState<string | null>(null)
  const [minLevel, setMinLevel] = useState<number | string>('')
  const [maxLevel, setMaxLevel] = useState<number | string>('')
  const [selected, setSelected] = useState<CatalogItem | null>(null)

  const [debouncedQ] = useDebouncedValue(q, 250)

  const query = {
    q: debouncedQ.trim() || undefined,
    category: category ?? undefined,
    quality: quality ?? undefined,
    profession: profession ? Number(profession) : undefined,
    minLevel: typeof minLevel === 'number' ? minLevel : undefined,
    maxLevel: typeof maxLevel === 'number' ? maxLevel : undefined,
    page,
    pageSize,
  }

  const items = useQuery({
    queryKey: ['items', query],
    queryFn: () => unwrap(api.GET('/api/items', { params: { query } })),
    placeholderData: keepPreviousData,
  })

  const total = items.data?.total ?? 0
  const hasFilters = Boolean(q || category || quality || profession || minLevel !== '' || maxLevel !== '')

  const clearFilters = () => {
    setQ('')
    setCategory(null)
    setQuality(null)
    setProfession(null)
    setMinLevel('')
    setMaxLevel('')
    setPage(1)
  }

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={2}>{t('title')}</Title>
        {items.isFetching && <Loader size="sm" />}
      </Group>
      <Text c="dimmed">{t('description')}</Text>

      {items.data && !items.data.assetsAvailable && (
        <Alert color="yellow" icon={<IconAlertTriangle size={16} />}>
          {t('assetsUnavailable')}
        </Alert>
      )}

      <SimpleGrid cols={{ base: 1, sm: 2, md: 3, lg: 6 }} spacing="sm">
        <TextInput
          label={t('column.item')}
          placeholder={t('searchPlaceholder')}
          leftSection={<IconSearch size={16} />}
          value={q}
          onChange={(e) => {
            setQ(e.currentTarget.value)
            setPage(1)
          }}
        />
        <Select
          label={t('filter.category')}
          placeholder={t('filter.all')}
          clearable
          value={category}
          onChange={(v) => {
            setCategory((v as ItemCategory | null) ?? null)
            setPage(1)
          }}
          data={categories.map((c) => ({ value: c, label: t(`category.${c}`) }))}
        />
        <Select
          label={t('filter.quality')}
          placeholder={t('filter.all')}
          clearable
          value={quality}
          onChange={(v) => {
            setQuality((v as ItemQuality | null) ?? null)
            setPage(1)
          }}
          data={qualities.map((qual) => ({ value: qual, label: t(`quality.${qual}`) }))}
        />
        <Select
          label={t('filter.profession')}
          placeholder={t('filter.all')}
          clearable
          value={profession}
          onChange={(v) => {
            setProfession(v)
            setPage(1)
          }}
          data={professionLines.map((p) => ({ value: p.value, label: t(p.key) }))}
        />
        <NumberInput
          label={t('filter.minLevel')}
          min={0}
          max={255}
          value={minLevel}
          onChange={(v) => {
            setMinLevel(v)
            setPage(1)
          }}
        />
        <NumberInput
          label={t('filter.maxLevel')}
          min={0}
          max={255}
          value={maxLevel}
          onChange={(v) => {
            setMaxLevel(v)
            setPage(1)
          }}
        />
      </SimpleGrid>

      {hasFilters && (
        <Group>
          <Button size="xs" variant="subtle" onClick={clearFilters}>
            {t('clearFilters')}
          </Button>
        </Group>
      )}

      <ErrorAlert error={items.error} />

      <Paper withBorder>
        <ScrollArea>
          <Table highlightOnHover striped miw={820}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('column.item')}</Table.Th>
                <Table.Th>{t('column.id')}</Table.Th>
                <Table.Th>{t('column.category')}</Table.Th>
                <Table.Th>{t('column.quality')}</Table.Th>
                <Table.Th>{t('column.level')}</Table.Th>
                <Table.Th>{t('column.stats')}</Table.Th>
                <Table.Th>{t('column.price')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {items.data?.items.map((item) => (
                <Table.Tr key={item.id} onClick={() => setSelected(item)} style={{ cursor: 'pointer' }}>
                  <Table.Td>
                    <ItemTooltip item={item}>
                      <Group gap="sm" wrap="nowrap">
                        <ItemIcon item={item} size={36} />
                        <Stack gap={0}>
                          <Text
                            size="sm"
                            fw={600}
                            style={{
                              color:
                                item.quality && item.quality !== 'None' && item.quality !== 'Normal'
                                  ? qualityColor(item.quality)
                                  : undefined,
                            }}
                          >
                            {item.name}
                          </Text>
                          {item.type && (
                            <Text size="xs" c="dimmed">
                              {item.type}
                            </Text>
                          )}
                        </Stack>
                      </Group>
                    </ItemTooltip>
                  </Table.Td>
                  <Table.Td>
                    <Code>{item.id}</Code>
                  </Table.Td>
                  <Table.Td>{t(`category.${item.category ?? 'Other'}`)}</Table.Td>
                  <Table.Td>
                    {item.quality && item.quality !== 'None' ? (
                      <Badge size="sm" variant="light" color={qualityBadgeColor(item.quality)}>
                        {t(`quality.${item.quality}`)}
                      </Badge>
                    ) : (
                      '—'
                    )}
                  </Table.Td>
                  <Table.Td>{item.requiredLevel || '—'}</Table.Td>
                  <Table.Td>
                    <Text size="xs">{statsSummary(item)}</Text>
                  </Table.Td>
                  <Table.Td>
                    <Stack gap={0}>
                      {(item.price ?? 0) > 0 && <Text size="xs">{item.price!.toLocaleString()} S</Text>}
                      {(item.emoneyPrice ?? 0) > 0 && (
                        <Text size="xs" c="yellow">
                          {item.emoneyPrice!.toLocaleString()} CP
                        </Text>
                      )}
                      {!(item.price ?? 0) && !(item.emoneyPrice ?? 0) && <Text size="xs">—</Text>}
                    </Stack>
                  </Table.Td>
                </Table.Tr>
              ))}
              {items.data && items.data.items.length === 0 && (
                <Table.Tr>
                  <Table.Td colSpan={7}>
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

      <Modal opened={!!selected} onClose={() => setSelected(null)} title={t('detail.title')} size="md">
        {selected && (
          <Stack>
            <Paper
              p="sm"
              radius="md"
              style={{
                background: 'rgba(18, 20, 26, 0.96)',
                color: '#f1f3f5',
              }}
            >
              <ItemTooltipContent item={selected} />
            </Paper>
            <SimpleGrid cols={2} spacing="xs">
              <Text size="xs" c="dimmed">
                {t('detail.actionId')}: <Code>{selected.actionId ?? 0}</Code>
              </Text>
              <Text size="xs" c="dimmed">
                {t('detail.monopoly')}: <Code>{selected.monopoly ?? 0}</Code>
              </Text>
              <Text size="xs" c="dimmed">
                {t('detail.stackLimit')}: <Code>{selected.stackLimit ?? 0}</Code>
              </Text>
              <Text size="xs" c="dimmed">
                {t('detail.groundIcon')}: <Code>{selected.groundIcon ?? '—'}</Code>
              </Text>
            </SimpleGrid>
            <Group justify="flex-end">
              <CopyButton value={String(selected.id ?? '')}>
                {({ copied, copy }) => (
                  <Button
                    size="xs"
                    variant="light"
                    color={copied ? 'teal' : 'blue'}
                    leftSection={copied ? <IconCheck size={14} /> : <IconCopy size={14} />}
                    onClick={copy}
                  >
                    {copied ? t('copied') : t('copyId')}
                  </Button>
                )}
              </CopyButton>
            </Group>
          </Stack>
        )}
      </Modal>
    </Stack>
  )
}
