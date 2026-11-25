import { Alert, List, Text } from '@mantine/core'
import { IconAlertCircle, IconAlertTriangle } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import type { Schemas } from '../../api/client'

type DataProblem = Schemas['DataProblemDto']

export function ProblemsAlert({ problems }: { problems: DataProblem[] }) {
  const { t } = useTranslation('data')
  if (problems.length === 0) return null

  const errors = problems.filter((p) => p.level !== 'warning')
  const warnings = problems.filter((p) => p.level === 'warning')
  const hasErrors = errors.length > 0

  return (
    <Alert
      color={hasErrors ? 'red' : 'yellow'}
      icon={hasErrors ? <IconAlertCircle size={18} /> : <IconAlertTriangle size={18} />}
      title={
        hasErrors
          ? t('status.errorsCount', { count: errors.length })
          : t('status.warningsCount', { count: warnings.length })
      }
    >
      <List size="sm" spacing={2}>
        {problems.slice(0, 12).map((p, idx) => (
          <List.Item key={idx}>
            <Text size="sm" span c={p.level === 'warning' ? 'yellow.8' : undefined}>
              {p.line != null ? `L${p.line}: ` : p.entry != null ? `#${p.entry + 1}: ` : ''}
              {p.message}
            </Text>
          </List.Item>
        ))}
        {problems.length > 12 && (
          <List.Item>
            <Text size="xs" c="dimmed">
              +{problems.length - 12}…
            </Text>
          </List.Item>
        )}
      </List>
    </Alert>
  )
}
