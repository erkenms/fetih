import {
  ActionIcon,
  Box,
  Button,
  Card,
  Group,
  Modal,
  SimpleGrid,
  Stack,
  Text,
  Title,
  Tooltip,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { IconTrash } from '@tabler/icons-react'
import { useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap, type Schemas } from '../../api/client'
import { useErrorText } from '../../app/ErrorAlert'
import { ItemIcon, ItemTooltip } from '../../components/items'
import {
  EQUIPMENT_SLOTS,
  mapEquipmentSlots,
  toCatalogItem,
  toItemInstance,
  type CharacterItemEntry,
} from './characterMeta'

interface CharacterItemsViewProps {
  characterId: number
  characterName: string
  equipment: CharacterItemEntry[]
  inventory: CharacterItemEntry[]
  warehouses: Schemas['CharacterWarehouseGroup'][]
  detained: CharacterItemEntry[]
  canManage: boolean
  onChanged: () => void
}

function ItemTile({
  entry,
  onTake,
  takeLabel,
}: {
  entry: CharacterItemEntry
  onTake?: () => void
  takeLabel?: string
}) {
  const catalog = toCatalogItem(entry)
  const instance = toItemInstance(entry)
  return (
    <Box pos="relative" style={{ display: 'inline-flex' }}>
      <ItemTooltip item={catalog} instance={instance}>
        <Box>
          <ItemIcon item={catalog} instance={instance} size={48} />
        </Box>
      </ItemTooltip>
      {onTake && (
        <Tooltip label={takeLabel}>
          <ActionIcon
            size="xs"
            color="red"
            variant="filled"
            pos="absolute"
            top={-6}
            right={-6}
            aria-label={takeLabel}
            onClick={onTake}
          >
            <IconTrash size={12} />
          </ActionIcon>
        </Tooltip>
      )}
    </Box>
  )
}

export function CharacterItemsView({
  characterId,
  characterName,
  equipment,
  inventory,
  warehouses,
  detained,
  canManage,
  onChanged,
}: CharacterItemsViewProps) {
  const { t } = useTranslation('characters')
  const formatError = useErrorText('characters')
  const [taking, setTaking] = useState<CharacterItemEntry | null>(null)
  const [error, setError] = useState<unknown>(null)

  const takeMutation = useMutation({
    mutationFn: (itemId: number) =>
      unwrap(
        api.POST('/api/characters/{id}/take-item', {
          params: { path: { id: characterId } },
          body: { itemId },
        }),
      ),
    onSuccess: () => {
      notifications.show({ message: t('detail.items.takeSuccess'), color: 'green' })
      setTaking(null)
      setError(null)
      onChanged()
    },
    onError: setError,
  })

  const equippedBySlot = mapEquipmentSlots(equipment)

  return (
    <Stack>
      <Card withBorder>
        <Title order={4} mb="sm">
          {t('detail.items.equipment')}
        </Title>
        <SimpleGrid cols={{ base: 2, sm: 3, md: 4 }} spacing="sm">
          {EQUIPMENT_SLOTS.map((slot) => {
            const entry = equippedBySlot.get(slot)
            const catalog = entry ? toCatalogItem(entry) : null
            return (
              <Card key={slot} withBorder p="xs">
                <Group gap="sm" wrap="nowrap">
                  {entry && catalog ? (
                    <ItemTooltip item={catalog} instance={toItemInstance(entry)}>
                      <Box>
                        <ItemIcon item={catalog} instance={toItemInstance(entry)} size={44} />
                      </Box>
                    </ItemTooltip>
                  ) : (
                    <Box
                      w={44}
                      h={44}
                      style={{
                        borderRadius: 6,
                        border: '1px dashed var(--mantine-color-default-border)',
                        flexShrink: 0,
                      }}
                    />
                  )}
                  <Stack gap={0} style={{ minWidth: 0 }}>
                    <Text size="xs" c="dimmed">
                      {t(`slot.${slot}` as never)}
                    </Text>
                    <Text size="sm" fw={entry ? 600 : 400} c={entry ? undefined : 'dimmed'} truncate>
                      {catalog
                        ? `${catalog.name}${entry && entry.plus > 0 ? ` +${entry.plus}` : ''}`
                        : t('detail.items.emptySlot')}
                    </Text>
                  </Stack>
                </Group>
              </Card>
            )
          })}
        </SimpleGrid>
      </Card>

      <Card withBorder>
        <Title order={4} mb="sm">
          {t('detail.items.inventory', { count: inventory.length })}
        </Title>
        {inventory.length === 0 ? (
          <Text c="dimmed" size="sm">
            {t('detail.items.emptyInventory')}
          </Text>
        ) : (
          <Group gap="sm" wrap="wrap">
            {inventory.map((entry) => (
              <ItemTile
                key={entry.itemId}
                entry={entry}
                takeLabel={t('detail.items.take')}
                onTake={
                  canManage
                    ? () => {
                        setError(null)
                        setTaking(entry)
                      }
                    : undefined
                }
              />
            ))}
          </Group>
        )}
      </Card>

      <Card withBorder>
        <Title order={4} mb="sm">
          {t('detail.items.warehouses')}
        </Title>
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

      <Modal
        opened={Boolean(taking)}
        onClose={() => setTaking(null)}
        title={t('detail.items.takeConfirmTitle')}
      >
        {taking && (
          <Stack>
            {Boolean(error) && (
              <Text c="red" size="sm">
                {formatError(error)}
              </Text>
            )}
            <Text size="sm">
              {t('detail.items.takeConfirmBody', {
                name: toCatalogItem(taking).name,
                id: taking.itemId,
                character: characterName,
              })}
            </Text>
            <Group justify="flex-end">
              <Button variant="subtle" onClick={() => setTaking(null)}>
                {t('actions.cancel')}
              </Button>
              <Button
                color="red"
                loading={takeMutation.isPending}
                onClick={() => takeMutation.mutate(taking.itemId)}
              >
                {t('actions.confirm')}
              </Button>
            </Group>
          </Stack>
        )}
      </Modal>
    </Stack>
  )
}
