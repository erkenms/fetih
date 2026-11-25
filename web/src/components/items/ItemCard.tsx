import { Group, Paper, Stack, Text } from '@mantine/core'
import type { ReactNode } from 'react'
import { ItemIcon } from './ItemIcon'
import { ItemTooltip } from './ItemTooltip'
import { GAME_TEXT_EDGE, hasQualityColor, qualityColor, type CatalogItem, type ItemInstance } from './itemMeta'

export interface ItemCardProps {
  item: CatalogItem
  instance?: ItemInstance
  rightSection?: ReactNode
  onClick?: () => void
  withTooltip?: boolean
}

export function ItemCard({
  item,
  instance,
  rightSection,
  onClick,
  withTooltip = true,
}: ItemCardProps) {
  const card = (
    <Paper
      withBorder
      p="xs"
      onClick={onClick}
      style={{ cursor: onClick ? 'pointer' : undefined }}
    >
      <Group justify="space-between" wrap="nowrap" gap="sm">
        <Group wrap="nowrap" gap="sm" style={{ minWidth: 0 }}>
          <ItemIcon item={item} instance={instance} size={36} />
          <Stack gap={0} style={{ minWidth: 0 }}>
            <Text
              size="sm"
              fw={600}
              truncate
              style={
                hasQualityColor(item.quality)
                  ? { color: qualityColor(item.quality), textShadow: GAME_TEXT_EDGE }
                  : undefined
              }
            >
              {item.name}
              {instance?.plus ? ` +${instance.plus}` : ''}
            </Text>
            <Text size="xs" c="dimmed" truncate>
              {item.type || item.category} · #{item.id}
            </Text>
          </Stack>
        </Group>
        {rightSection}
      </Group>
    </Paper>
  )

  return withTooltip ? (
    <ItemTooltip item={item} instance={instance}>
      <div>{card}</div>
    </ItemTooltip>
  ) : (
    card
  )
}
