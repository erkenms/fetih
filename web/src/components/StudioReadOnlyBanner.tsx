import { Alert } from '@mantine/core'
import { IconLock } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { useMe } from '../app/auth'

export function useStudioReadOnly(): boolean {
  return !!useMe().data?.studioReadOnly
}

export function StudioReadOnlyBanner() {
  const { t } = useTranslation('common')
  const readOnly = useStudioReadOnly()
  if (!readOnly) return null

  return (
    <Alert color="yellow" icon={<IconLock size={16} />} title={t('studio.readOnlyTitle')}>
      {t('studio.readOnlyBody')}
    </Alert>
  )
}
