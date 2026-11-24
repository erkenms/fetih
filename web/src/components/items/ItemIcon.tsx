import { Badge, Box, Center } from '@mantine/core'
import { IconPackage } from '@tabler/icons-react'
import { useEffect, useState } from 'react'
import { itemIconUrl, qualityColor, type CatalogItem, type ItemInstance } from './itemMeta'

export interface ItemIconProps {
  item: Pick<CatalogItem, 'icon' | 'name' | 'quality'>
  instance?: Pick<ItemInstance, 'plus' | 'stackCount'>
  size?: number
  showQualityBorder?: boolean
}

export function ItemIcon({ item, instance, size = 40, showQualityBorder = true }: ItemIconProps) {
  const [src, setSrc] = useState(() => itemIconUrl(item.icon))
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    setSrc(itemIconUrl(item.icon))
    setFailed(false)
  }, [item.icon])

  const borderColor =
    showQualityBorder && item.quality && item.quality !== 'None' && item.quality !== 'Normal'
      ? qualityColor(item.quality)
      : 'var(--mantine-color-default-border)'

  return (
    <Box
      pos="relative"
      w={size}
      h={size}
      style={{
        flexShrink: 0,
        borderRadius: 6,
        border: `1px solid ${borderColor}`,
        background: 'rgba(18, 20, 26, 0.85)',
        overflow: 'hidden',
      }}
    >
      {!failed ? (
        <img
          src={src}
          alt={item.name ?? ''}
          width={size - 2}
          height={size - 2}
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
