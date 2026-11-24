import { Badge, Code, Grid, Group, Loader, NavLink, Paper, ScrollArea, Select, Stack, Table, Text, TextInput, Title } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { IconShoppingBag } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { AssetImage } from '../../components/pickers'

export default function ShopsCatalogPage() {
  const { t } = useTranslation('catalogs')
  const [search, setSearch] = useState('')
  const [source, setSource] = useState<string | null>(null)
  const [selectedKey, setSelectedKey] = useState<string | null>(null)
  const [filters] = useDebouncedValue({ search, source }, 250)

  const shopsQuery = useQuery({
    queryKey: ['catalogs', 'shops', filters],
    queryFn: () =>
      unwrap(
        api.GET('/api/catalogs/shops', {
          params: {
            query: {
              search: filters.search.trim() || undefined,
              source: filters.source || undefined,
            },
          },
        }),
      ),
  })

  const shops = shopsQuery.data?.shops ?? []
  const sources = (shopsQuery.data?.sources ?? []).map((s) => ({ value: s, label: s }))

  useEffect(() => {
    if (shops.length > 0 && !shops.some((s) => s.key === selectedKey)) {
      setSelectedKey(shops[0].key)
    }
  }, [shops, selectedKey])

  const selected = shops.find((s) => s.key === selectedKey) ?? null

  const formatCurrency = (currency: string | null | undefined) => {
    if (!currency) return '—'
    const key = `shops.currency.${currency}`
    const translated = t(key)
    return translated === key ? currency : translated
  }

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={2}>{t('shops.title')}</Title>
        {shopsQuery.isFetching && <Loader size="sm" />}
      </Group>
      <Text c="dimmed">{t('shops.description')}</Text>

      <Group align="flex-end" wrap="wrap">
        <TextInput
          style={{ flex: 1, minWidth: 220 }}
          placeholder={t('shops.searchPlaceholder')}
          value={search}
          onChange={(e) => setSearch(e.currentTarget.value)}
        />
        <Select
          w={220}
          clearable
          placeholder={t('shops.allSources')}
          data={sources}
          value={source}
          onChange={setSource}
        />
      </Group>

      <ErrorAlert error={shopsQuery.error} />

      <Grid>
        <Grid.Col span={{ base: 12, md: 4 }}>
          <Paper withBorder>
            <ScrollArea h={560}>
              {shops.map((shop) => (
                <NavLink
                  key={shop.key}
                  active={shop.key === selected?.key}
                  onClick={() => setSelectedKey(shop.key)}
                  label={
                    <Group justify="space-between" wrap="nowrap">
                      <Text size="sm" fw={500} truncate>{shop.name}</Text>
                      <Badge size="xs" variant="light">{t('shops.itemCount', { count: shop.itemCount })}</Badge>
                    </Group>
                  }
                  description={
                    <Group gap={6}>
                      <Text size="xs" c="dimmed">{shop.source}</Text>
                      {shop.npcId != null && <Text size="xs" c="dimmed">• {t('shops.npcId', { id: shop.npcId })}</Text>}
                    </Group>
                  }
                />
              ))}
              {shopsQuery.data && shops.length === 0 && (
                <Text c="dimmed" ta="center" py="lg">{t('shops.empty')}</Text>
              )}
            </ScrollArea>
          </Paper>
          <Text size="xs" c="dimmed" mt="xs">{t('shops.total', { count: shops.length })}</Text>
        </Grid.Col>

        <Grid.Col span={{ base: 12, md: 8 }}>
          <Paper withBorder p="md" mih={560}>
            {selected ? (
              <Stack gap="sm">
                <Group justify="space-between" wrap="wrap">
                  <div>
                    <Title order={4}>{selected.name}</Title>
                    <Group gap="xs">
                      <Badge variant="outline">{selected.source}</Badge>
                      <Badge variant="light">{formatCurrency(selected.currency)}</Badge>
                      {selected.npcId != null && <Badge variant="light">{t('shops.npcId', { id: selected.npcId })}</Badge>}
                      {selected.mapId != null && (
                        <Text size="xs" c="dimmed">
                          {t('shops.location', { map: selected.mapId, x: selected.x, y: selected.y })}
                        </Text>
                      )}
                    </Group>
                  </div>
                </Group>

                <ScrollArea>
                  <Table striped highlightOnHover miw={600}>
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th w={48}>{t('shops.column.icon')}</Table.Th>
                        <Table.Th>{t('shops.column.id')}</Table.Th>
                        <Table.Th>{t('shops.column.name')}</Table.Th>
                        <Table.Th>{t('shops.column.price')}</Table.Th>
                        <Table.Th>{t('shops.column.category')}</Table.Th>
                        <Table.Th>{t('shops.column.tags')}</Table.Th>
                        <Table.Th>{t('shops.column.description')}</Table.Th>
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {selected.items.map((item, index) => (
                        <Table.Tr key={`${item.itemId}-${index}`}>
                          <Table.Td>
                            <AssetImage
                              path={item.itemIcon}
                              alt={item.itemName ?? String(item.itemId)}
                              size={32}
                              fallback={<IconShoppingBag size={16} />}
                            />
                          </Table.Td>
                          <Table.Td><Code>{item.itemId}</Code></Table.Td>
                          <Table.Td><Text fw={500}>{item.itemName ?? '—'}</Text></Table.Td>
                          <Table.Td>
                            {item.price != null ? `${item.price.toLocaleString()} ${formatCurrency(item.currency)}` : '—'}
                          </Table.Td>
                          <Table.Td>{item.category ?? '—'}</Table.Td>
                          <Table.Td>
                            <Group gap={4}>
                              {item.tags.map((tag) => (
                                <Badge key={tag} size="xs" color={tag === 'hot' ? 'red' : tag === 'new' ? 'teal' : 'blue'} variant="light">
                                  {tag}
                                </Badge>
                              ))}
                            </Group>
                          </Table.Td>
                          <Table.Td>
                            <Text size="xs" c="dimmed">{item.description ?? '—'}</Text>
                          </Table.Td>
                        </Table.Tr>
                      ))}
                    </Table.Tbody>
                  </Table>
                </ScrollArea>
              </Stack>
            ) : (
              <Text c="dimmed" ta="center" py="xl">{t('shops.selectPrompt')}</Text>
            )}
          </Paper>
        </Grid.Col>
      </Grid>
    </Stack>
  )
}
