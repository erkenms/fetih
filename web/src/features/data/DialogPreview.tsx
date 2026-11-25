import { Badge, Divider, Group, Paper, Stack, Text } from '@mantine/core'
import { IconMoodSmile } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useMemo } from 'react'
import { api, unwrap } from '../../api/client'
import { AssetImage } from '../../components/pickers'

export interface DialogPreviewProps {
  title: string
  face: number
  lines: string[]
  choices: string[]
  closeLabel?: string
  badge?: string
}

export function DialogPreview({ title, face, lines, choices, closeLabel, badge }: DialogPreviewProps) {
  const faces = useQuery({
    queryKey: ['catalogs', 'faces'],
    queryFn: () => unwrap(api.GET('/api/catalogs/faces')),
  })

  const facePath = useMemo(() => {
    if (!faces.data || face <= 0) return null
    return faces.data.npc.find((f) => f.id === face)?.path ?? null
  }, [faces.data, face])

  return (
    <Paper withBorder p="md" radius="md" bg="var(--mantine-color-default-hover)">
      <Stack gap="sm">
        <Group justify="space-between" align="center">
          <Text size="xs" fw={600} tt="uppercase" c="dimmed">
            {title}
          </Text>
          {badge && (
            <Badge size="sm" variant="light">
              {badge}
            </Badge>
          )}
        </Group>

        <Group align="flex-start" wrap="nowrap" gap="md">
          {face > 0 && (
            <AssetImage
              path={facePath}
              alt={`#${face}`}
              size={64}
              fallback={<IconMoodSmile size={28} />}
            />
          )}
          <Stack gap={6} style={{ flex: 1, minWidth: 0 }}>
            {lines.length > 0 ? (
              lines.map((line, i) => (
                <Text key={i} size="sm" style={{ whiteSpace: 'pre-wrap' }}>
                  {line}
                </Text>
              ))
            ) : (
              <Text size="sm" c="dimmed" fs="italic">
                —
              </Text>
            )}
          </Stack>
        </Group>

        <Divider />

        <Stack gap={4}>
          {choices.map((choice, idx) => (
            <Text key={idx} size="sm" c="blue" fw={500}>
              {idx + 1}. {choice || '…'}
            </Text>
          ))}
          {closeLabel && (
            <Text size="sm" c="dimmed" fw={500}>
              {choices.length > 0 ? `${choices.length + 1}. ` : '• '}
              {closeLabel}
            </Text>
          )}
        </Stack>
      </Stack>
    </Paper>
  )
}
