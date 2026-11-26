import { Box, Center } from '@mantine/core'
import { IconUser } from '@tabler/icons-react'
import { useEffect, useState } from 'react'
import { assetUrl } from '../pickers/catalogUtils'
import type { MonsterLook, NpcLook } from './looks'

interface LookSpriteProps {
  look: NpcLook | MonsterLook | null | undefined
  alt: string
  // The square box the picture is fitted into, standing on its bottom edge
  size?: number
}

export function LookSprite({ look, alt, size = 64 }: LookSpriteProps) {
  const url = assetUrl(look?.image)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    setFailed(false)
  }, [url])

  if (!url || failed) {
    return (
      <Center
        w={size}
        h={size}
        style={{
          background: 'var(--mantine-color-default-hover)',
          borderRadius: 'var(--mantine-radius-sm)',
          flexShrink: 0,
        }}
      >
        <IconUser size={Math.max(14, size / 3)} />
      </Center>
    )
  }

  return (
    <Box
      component="img"
      src={url}
      alt={alt}
      title={alt}
      w={size}
      h={size}
      loading="lazy"
      onError={() => setFailed(true)}
      style={{
        objectFit: 'contain',
        objectPosition: 'center bottom',
        flexShrink: 0,
      }}
    />
  )
}
