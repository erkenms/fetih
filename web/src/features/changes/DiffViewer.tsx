import { Box, Paper, ScrollArea, Text } from '@mantine/core'
import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { parseUnifiedDiff, type DiffLineKind } from './diff'

const bgByKind: Record<DiffLineKind, string | undefined> = {
  header: undefined,
  hunk: 'rgba(59, 130, 246, 0.12)',
  add: 'rgba(34, 197, 94, 0.14)',
  remove: 'rgba(239, 68, 68, 0.14)',
  context: undefined,
}

const prefixByKind: Record<DiffLineKind, string> = {
  header: ' ',
  hunk: ' ',
  add: '+',
  remove: '-',
  context: ' ',
}

export function DiffViewer({ diff }: { diff: string }) {
  const { t } = useTranslation('changes')
  const lines = useMemo(() => parseUnifiedDiff(diff), [diff])

  if (lines.length === 0) {
    return (
      <Paper withBorder p="md">
        <Text c="dimmed" size="sm">
          {t('diff.empty')}
        </Text>
      </Paper>
    )
  }

  return (
    <Paper withBorder>
      <ScrollArea>
        <Box
          component="pre"
          m={0}
          p="xs"
          style={{
            fontFamily: 'var(--mantine-font-family-monospace)',
            fontSize: '12px',
            lineHeight: 1.5,
          }}
        >
          {lines.map((line, index) => (
            <Box
              key={index}
              px="xs"
              style={{
                display: 'flex',
                gap: '8px',
                backgroundColor: bgByKind[line.kind],
                whiteSpace: 'pre',
              }}
            >
              <Text
                span
                c="dimmed"
                size="xs"
                style={{ width: 40, textAlign: 'right', userSelect: 'none', flexShrink: 0 }}
              >
                {line.oldLine ?? ''}
              </Text>
              <Text
                span
                c="dimmed"
                size="xs"
                style={{ width: 40, textAlign: 'right', userSelect: 'none', flexShrink: 0 }}
              >
                {line.newLine ?? ''}
              </Text>
              <Text
                span
                c={
                  line.kind === 'add'
                    ? 'green'
                    : line.kind === 'remove'
                      ? 'red'
                      : line.kind === 'hunk'
                        ? 'blue'
                        : line.kind === 'header'
                          ? 'dimmed'
                          : undefined
                }
                size="xs"
                style={{ userSelect: 'none', width: 10, flexShrink: 0 }}
              >
                {prefixByKind[line.kind]}
              </Text>
              <Text
                span
                c={line.kind === 'header' ? 'dimmed' : line.kind === 'hunk' ? 'blue' : undefined}
                size="xs"
                style={{ fontFamily: 'inherit' }}
              >
                {line.text}
              </Text>
            </Box>
          ))}
        </Box>
      </ScrollArea>
    </Paper>
  )
}
