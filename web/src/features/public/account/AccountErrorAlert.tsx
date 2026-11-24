import { Alert } from '@mantine/core'
import { IconAlertTriangle } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../../../api/client'

export function AccountErrorAlert({ error }: { error: unknown }) {
  const { t, i18n } = useTranslation(['publicAccount', 'common'])
  if (!error) return null

  const code = error instanceof ApiError ? error.code : 'server_error'
  const featureKey = `publicAccount:errors.${code}`
  const message = i18n.exists(featureKey)
    ? t(featureKey)
    : t(`common:errors.${code}`, { defaultValue: t('common:errors.unknown', { code }) })

  return (
    <Alert color="red" icon={<IconAlertTriangle size={18} />}>
      {message}
    </Alert>
  )
}
