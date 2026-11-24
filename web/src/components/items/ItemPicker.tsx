import {
  ActionIcon,
  Button,
  Group,
  Loader,
  Modal,
  Pagination,
  Paper,
  ScrollArea,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
} from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { IconSearch, IconX } from '@tabler/icons-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap } from '../../api/client'
import { ItemCard } from './ItemCard'
import { ItemIcon } from './ItemIcon'
import { ItemTooltip } from './ItemTooltip'
import { qualityColor, type CatalogItem, type ItemCategory, type ItemQuality } from './itemMeta'

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

export interface ItemPickerProps {
  value?: number | null
  onChange: (item: CatalogItem | null) => void
  label?: string
  placeholder?: string
  category?: ItemCategory
  clearable?: boolean
  disabled?: boolean
}

export function ItemPicker({
  value,
  onChange,
  label,
  placeholder,
  category: fixedCategory,
  clearable = true,
  disabled = false,
}: ItemPickerProps) {
  const { t } = useTranslation('items')
  const [opened, setOpened] = useState(false)
  const [search, setSearch] = useState('')
  const [category, setCategory] = useState<ItemCategory | null>(fixedCategory ?? null)
  const [quality, setQuality] = useState<ItemQuality | null>(null)
  const [page, setPage] = useState(1)
  const [debouncedSearch] = useDebouncedValue(search, 250)

  const selectedItem = useQuery({
    queryKey: ['item', value],
    queryFn: () => unwrap(api.GET('/api/items/{id}', { params: { path: { id: value! } } })),
    enabled: value != null && value > 0,
    retry: false,
  })

  const searchQuery = {
    q: debouncedSearch.trim() || undefined,
    category: fixedCategory ?? category ?? undefined,
    quality: quality ?? undefined,
    page,
    pageSize: 20,
  }

  const results = useQuery({
    queryKey: ['items', 'picker', searchQuery],
    queryFn: () => unwrap(api.GET('/api/items', { params: { query: searchQuery } })),
    enabled: opened,
    placeholderData: keepPreviousData,
  })

  const total = results.data?.total ?? 0

  return (
    <Stack gap={4}>
      {label && (
        <Text size="sm" fw={500}>
          {label}
        </Text>
      )}
      {selectedItem.data ? (
        <ItemCard
          item={selectedItem.data}
          onClick={disabled ? undefined : () => setOpened(true)}
          rightSection={
            <Group gap={4}>
              <Button
                size="xs"
                variant="light"
                disabled={disabled}
                onClick={(e) => {
                  e.stopPropagation()
                  setOpened(true)
                }}
              >
                {t('change')}
              </Button>
              {clearable && !disabled && (
                <ActionIcon
                  variant="subtle"
                  color="gray"
                  aria-label={t('clear')}
                  onClick={(e) => {
                    e.stopPropagation()
                    onChange(null)
                  }}
                >
                  <IconX size={16} />
                </ActionIcon>
              )}
            </Group>
          }
        />
      ) : (
        <Paper
          withBorder
          p="xs"
          onClick={disabled ? undefined : () => setOpened(true)}
          style={{ cursor: disabled ? undefined : 'pointer' }}
        >
          <Group justify="space-between">
            <Text size="sm" c="dimmed">
              {value ? `#${value}` : placeholder || t('noneSelected')}
            </Text>
            <Button size="xs" variant="light" disabled={disabled}>
              {t('pick')}
            </Button>
          </Group>
        </Paper>
      )}

      <Modal opened={opened} onClose={() => setOpened(false)} title={label || t('pick')} size="xl">
        <Stack>
          <Group grow>
            <TextInput
              placeholder={t('searchPlaceholder')}
              leftSection={<IconSearch size={16} />}
              value={search}
              onChange={(e) => {
                setSearch(e.currentTarget.value)
                setPage(1)
              }}
              data-autofocus
            />
            {!fixedCategory && (
              <Select
                placeholder={t('filter.category')}
                clearable
                value={category}
                onChange={(v) => {
                  setCategory((v as ItemCategory | null) ?? null)
                  setPage(1)
                }}
                data={categories.map((c) => ({ value: c, label: t(`category.${c}`) }))}
              />
            )}
            <Select
              placeholder={t('filter.quality')}
              clearable
              value={quality}
              onChange={(v) => {
                setQuality((v as ItemQuality | null) ?? null)
                setPage(1)
              }}
              data={qualities.map((q) => ({ value: q, label: t(`quality.${q}`) }))}
            />
          </Group>

          <ScrollArea h={380}>
            <Table highlightOnHover striped>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('column.item')}</Table.Th>
                  <Table.Th>{t('column.id')}</Table.Th>
                  <Table.Th>{t('column.category')}</Table.Th>
                  <Table.Th>{t('column.level')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {results.data?.items.map((item) => (
                  <Table.Tr
                    key={item.id}
                    style={{ cursor: 'pointer' }}
                    onClick={() => {
                      onChange(item)
                      setOpened(false)
                    }}
                  >
                    <Table.Td>
                      <ItemTooltip item={item}>
                        <Group gap="xs" wrap="nowrap">
                          <ItemIcon item={item} size={32} />
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
                        </Group>
                      </ItemTooltip>
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs" c="dimmed">
                        {item.id}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs">{item.type || t(`category.${item.category ?? 'Other'}`)}</Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs">{item.requiredLevel || '—'}</Text>
                    </Table.Td>
                  </Table.Tr>
                ))}
                {results.data && results.data.items.length === 0 && (
                  <Table.Tr>
                    <Table.Td colSpan={4}>
                      <Text c="dimmed" ta="center" py="md">
                        {t('empty')}
                      </Text>
                    </Table.Td>
                  </Table.Tr>
                )}
              </Table.Tbody>
            </Table>
          </ScrollArea>

          <Group justify="space-between">
            <Group gap="xs">
              <Text size="sm" c="dimmed">
                {t('total', { count: total })}
              </Text>
              {results.isFetching && <Loader size="xs" />}
            </Group>
            <Pagination total={Math.max(1, Math.ceil(total / 20))} value={page} onChange={setPage} />
          </Group>
        </Stack>
      </Modal>
    </Stack>
  )
}
