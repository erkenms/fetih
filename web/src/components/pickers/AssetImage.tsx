import { Box, Center } from '@mantine/core'
import { useEffect, useState, type ReactNode } from 'react'
import { assetUrl } from './catalogUtils'

interface AssetImageProps {
  path: string | null | undefined
  alt: string
  size?: number
  fallback: ReactNode
  radius?: string | number
}

export function AssetImage({ path, alt, size = 32, fallback, radius = 'sm' }: AssetImageProps) {
  const url = assetUrl(path)
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
          borderRadius: typeof radius === 'number' ? radius : 'var(--mantine-radius-sm)',
          background: 'var(--mantine-color-default-hover)',
          flexShrink: 0,
        }}
      >
        {fallback}
      </Center>
    )
  }

  return (
    <Box
      component="img"
      src={url}
      alt={alt}
      w={size}
      h={size}
      onError={() => setFailed(true)}
      style={{
        objectFit: 'contain',
        borderRadius: typeof radius === 'number' ? radius : 'var(--mantine-radius-sm)',
        flexShrink: 0,
      }}
    />
  )
}
