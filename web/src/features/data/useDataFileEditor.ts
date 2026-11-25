import { notifications } from '@mantine/notifications'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError, type Schemas } from '../../api/client'
import { useDataReload } from './useDataReload'

type DataProblem = Schemas['DataProblemDto']
type SaveDataResponse = Schemas['SaveDataResponse']

export interface DataFileEditorOptions<TEntry, TRow> {
  queryKey: readonly string[]
  fileName: string
  queryFn: () => Promise<{ hash: string; entries: TEntry[]; problems: DataProblem[] }>
  mapEntry: (entry: TEntry) => TRow
  saveFn: (hash: string, rows: TRow[]) => Promise<SaveDataResponse>
  onSynced?: (entries: TEntry[]) => void
}

export function useDataFileEditor<TEntry, TRow>({
  queryKey,
  fileName,
  queryFn,
  mapEntry,
  saveFn,
  onSynced,
}: DataFileEditorOptions<TEntry, TRow>) {
  const { t } = useTranslation('data')
  const queryClient = useQueryClient()
  const reloadMutation = useDataReload(fileName)

  const query = useQuery({ queryKey, queryFn })

  const [rows, setRows] = useState<TRow[]>([])
  const [hash, setHash] = useState('')
  const [dirty, setDirty] = useState(false)
  const [problems, setProblems] = useState<DataProblem[]>([])

  useEffect(() => {
    if (query.data && !dirty) {
      setRows(query.data.entries.map(mapEntry))
      setHash(query.data.hash)
      setProblems(query.data.problems)
      onSynced?.(query.data.entries)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query.data, dirty])

  const saveMutation = useMutation({
    mutationFn: () => saveFn(hash, rows),
    onSuccess: (res) => {
      setProblems(res.problems)
      if (!res.saved) {
        notifications.show({
          color: 'red',
          title: t('status.validationFailedTitle', { file: fileName }),
          message: t('status.validationFailedMessage'),
        })
        return
      }
      setHash(res.hash)
      setDirty(false)
      void queryClient.invalidateQueries({ queryKey })
      notifications.show({
        color: 'green',
        title: t('status.savedTitle', { file: fileName }),
        message:
          res.added + res.updated + res.removed > 0
            ? t('status.savedSummary', { added: res.added, updated: res.updated, removed: res.removed })
            : t('status.noChanges'),
      })
    },
    onError: (err) => {
      if (err instanceof ApiError && err.code === 'conflict') {
        notifications.show({
          color: 'orange',
          title: t('status.conflictTitle'),
          message: t('status.conflictMessage'),
        })
      }
    },
  })

  function resetChanges() {
    if (!query.data) return
    setRows(query.data.entries.map(mapEntry))
    setHash(query.data.hash)
    setProblems(query.data.problems)
    setDirty(false)
  }

  return {
    query,
    rows,
    setRows,
    dirty,
    setDirty,
    problems,
    saveMutation,
    reloadMutation,
    resetChanges,
  }
}
