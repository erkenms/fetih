import { notifications } from '@mantine/notifications'
import { useMutation } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { api, ApiError, unwrap } from '../../api/client'

export function useDataReload(fileName: string) {
  const { t } = useTranslation('data')
  const { t: tCommon } = useTranslation('common')

  return useMutation({
    mutationFn: () => unwrap(api.POST('/api/changes/{file}/reload', { params: { path: { file: fileName } } })),
    onSuccess: (res) => {
      if (res.applied) {
        notifications.show({
          color: res.warnings.length > 0 ? 'yellow' : 'green',
          title: t('status.reloadSuccessTitle', { file: res.file }),
          message: res.summary ?? res.file,
        })
      } else {
        notifications.show({
          color: 'red',
          title: t('status.reloadRejectedTitle', { file: res.file }),
          message: res.errors.join('; ') || res.file,
        })
      }
    },
    onError: (err) => {
      const code = err instanceof ApiError ? err.code : 'server_error'
      notifications.show({
        color: 'red',
        title: t('status.reloadErrorTitle'),
        message: tCommon(`errors.${code}`, { defaultValue: tCommon('errors.unknown', { code }) }),
      })
    },
  })
}
