import { Alert } from '@mantine/core'
import { IconAlertTriangle } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../api/client'

export function useErrorText(namespace?: string) {
  const { t, i18n } = useTranslation(namespace ? [namespace, 'common'] : 'common')
  return (error: unknown) => {
    if (error instanceof ApiError && namespace) {
      const key = `${namespace}:errors.${error.code}`
      if (i18n.exists(key)) return t(key)
    }
    const code = error instanceof ApiError ? error.code : 'server_error'
    return t(`common:errors.${code}`, { defaultValue: t('common:errors.unknown', { code }) })
  }
}

// Shows a failed query or mutation in the user's language
export function ErrorAlert({ error, namespace }: { error: unknown; namespace?: string }) {
  const text = useErrorText(namespace)
  if (!error) return null
  return (
    <Alert color="red" icon={<IconAlertTriangle size={18} />}>
      {text(error)}
    </Alert>
  )
}
