import { Alert } from '@mantine/core'
import { IconAlertTriangle } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../api/client'

export function useErrorText() {
  const { t } = useTranslation()
  return (error: unknown) => {
    const code = error instanceof ApiError ? error.code : 'server_error'
    return t(`errors.${code}`, { defaultValue: t('errors.unknown', { code }) })
  }
}

// Shows a failed query or mutation in the user's language
export function ErrorAlert({ error }: { error: unknown }) {
  const text = useErrorText()
  if (!error) return null
  return (
    <Alert color="red" icon={<IconAlertTriangle size={18} />}>
      {text(error)}
    </Alert>
  )
}
