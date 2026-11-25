import { Box, Card, Group, Stack, Text, Title } from '@mantine/core'
import { useTranslation } from 'react-i18next'
import type { Schemas } from '../../api/client'
import { ItemIcon, ItemTooltip } from '../../components/items'
import { toCatalogItem, toItemInstance, type CharacterItemEntry } from './characterMeta'

// Equipment the 5619 status window has no slot for
const EXTRA_SLOTS = [12, 18]

interface CharacterItemsViewProps {
  storedSilver: number
  equipment: CharacterItemEntry[]
  warehouses: Schemas['CharacterWarehouseGroup'][]
  detained: CharacterItemEntry[]
}

function ItemTile({ entry }: { entry: CharacterItemEntry }) {
  const catalog = toCatalogItem(entry)
  const instance = toItemInstance(entry)
  return (
    <ItemTooltip item={catalog} instance={instance}>
      <Box style={{ display: 'inline-flex' }}>
        <ItemIcon item={catalog} instance={instance} size={48} />
      </Box>
    </ItemTooltip>
  )
}

export function CharacterItemsView({ storedSilver, equipment, warehouses, detained }: CharacterItemsViewProps) {
  const { t, i18n } = useTranslation('characters')
  const extra = equipment.filter((entry) => EXTRA_SLOTS.includes(entry.position))

  return (
    <Stack>
      {extra.length > 0 && (
        <Card withBorder>
          <Title order={4} mb="sm">
            {t('detail.items.otherEquipment')}
          </Title>
          <Group gap="md" wrap="wrap">
            {extra.map((entry) => (
              <Group key={entry.itemId} gap="xs" wrap="nowrap">
                <ItemTile entry={entry} />
                <Text size="sm">{t(`slot.${entry.position}` as never)}</Text>
              </Group>
            ))}
          </Group>
        </Card>
      )}

      <Card withBorder>
        <Title order={4} mb="sm">
          {t('detail.items.warehouses')}
        </Title>
        <Text size="sm" mb="sm">
          {t('detail.items.storedSilver', { value: storedSilver.toLocaleString(i18n.language) })}
        </Text>
        {warehouses.length === 0 ? (
          <Text c="dimmed" size="sm">
            {t('detail.items.emptyWarehouses')}
          </Text>
        ) : (
          <Stack gap="md">
            {warehouses.map((wh) => (
              <Stack key={wh.packageId} gap="xs">
                <Text size="sm" fw={600}>
                  {t('detail.items.warehouseNpc', { id: wh.packageId, count: wh.items.length })}
                </Text>
                <Group gap="sm" wrap="wrap">
                  {wh.items.map((entry) => (
                    <ItemTile key={entry.itemId} entry={entry} />
                  ))}
                </Group>
              </Stack>
            ))}
          </Stack>
        )}
      </Card>

      {detained.length > 0 && (
        <Card withBorder>
          <Title order={4} mb="sm">
            {t('detail.items.detained', { count: detained.length })}
          </Title>
          <Group gap="sm" wrap="wrap">
            {detained.map((entry) => (
              <ItemTile key={entry.itemId} entry={entry} />
            ))}
          </Group>
        </Card>
      )}
    </Stack>
  )
}
