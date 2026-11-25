import { notifications } from '@mantine/notifications'
import { useMutation } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { api, unwrap, type Schemas } from '../../api/client'
import { useErrorText } from '../../app/ErrorAlert'

export type ReloadResult = Schemas['ReloadResponse'] | Schemas['MonsterReloadResponse']

export interface ReloadOptions<TVariables> {
  mutationFn: (variables: TVariables) => Promise<ReloadResult>
  successTitle: (res: ReloadResult) => string
  rejectedTitle: (res: ReloadResult) => string
  errorTitle: string
  fallbackApplied?: (res: ReloadResult) => string
  fallbackRejected?: (res: ReloadResult) => string
  onSuccess?: (res: ReloadResult) => void
}

export function useReloadMutation<TVariables = void>(options: ReloadOptions<TVariables>) {
  const errorText = useErrorText()

  return useMutation({
    mutationFn: options.mutationFn,
    onSuccess: (res) => {
      options.onSuccess?.(res)
      if (res.applied) {
        notifications.show({
          color: res.warnings.length > 0 ? 'yellow' : 'green',
          title: options.successTitle(res),
          message: res.summary ?? (options.fallbackApplied ? options.fallbackApplied(res) : res.file),
        })
      } else {
        notifications.show({
          color: 'red',
          title: options.rejectedTitle(res),
          message: res.errors.join('; ') || (options.fallbackRejected ? options.fallbackRejected(res) : res.file),
        })
      }
    },
    onError: (err) => {
      notifications.show({
        color: 'red',
        title: options.errorTitle,
        message: errorText(err),
      })
    },
  })
}

export function useDataReload(fileName: string) {
  const { t } = useTranslation('data')

  return useReloadMutation<void>({
    mutationFn: () => unwrap(api.POST('/api/changes/{file}/reload', { params: { path: { file: fileName } } })),
    successTitle: (res) => t('status.reloadSuccessTitle', { file: res.file }),
    rejectedTitle: (res) => t('status.reloadRejectedTitle', { file: res.file }),
    errorTitle: t('status.reloadErrorTitle'),
  })
}
