import { Badge, Box, Center } from '@mantine/core'
import { IconPackage } from '@tabler/icons-react'
import { useEffect, useState } from 'react'
import { itemIconUrl, type CatalogItem, type ItemInstance } from './itemMeta'

// The client's inventory slot, frame included, for an icon or an empty slot
export const itemSlotStyle = {
  background: 'url(/assets/ui/item-slot.png) center / 100% 100% no-repeat, #0f2a33',
} as const

export interface ItemIconProps {
  item: Pick<CatalogItem, 'icon' | 'name' | 'quality'>
  instance?: Pick<ItemInstance, 'plus' | 'stackCount'>
  size?: number
}

export function ItemIcon({ item, instance, size = 40 }: ItemIconProps) {
  const [src, setSrc] = useState(() => itemIconUrl(item.icon))
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    setSrc(itemIconUrl(item.icon))
    setFailed(false)
  }, [item.icon])

  return (
    <Box
      pos="relative"
      w={size}
      h={size}
      style={{ flexShrink: 0, overflow: 'hidden', ...itemSlotStyle }}
    >
      {!failed ? (
        <img
          src={src}
          alt={item.name ?? ''}
          width={size}
          height={size}
          style={{ display: 'block', width: '100%', height: '100%', objectFit: 'contain' }}
          onError={() => {
            if (src !== '/assets/icons/items/default.png') {
              setSrc('/assets/icons/items/default.png')
            } else {
              setFailed(true)
            }
          }}
        />
      ) : (
        <Center w="100%" h="100%" c="dimmed">
          <IconPackage size={Math.round(size * 0.55)} stroke={1.5} />
        </Center>
      )}
      {instance?.plus && instance.plus > 0 ? (
        <Badge
          size="xs"
          variant="filled"
          color="yellow"
          pos="absolute"
          top={1}
          left={1}
          px={3}
          style={{ pointerEvents: 'none', lineHeight: 1.2 }}
        >
          +{instance.plus}
        </Badge>
      ) : null}
      {instance?.stackCount && instance.stackCount > 1 ? (
        <Badge
          size="xs"
          variant="filled"
          color="dark"
          pos="absolute"
          bottom={1}
          right={1}
          px={3}
          style={{ pointerEvents: 'none', lineHeight: 1.2 }}
        >
          {instance.stackCount}
        </Badge>
      ) : null}
    </Box>
  )
}
