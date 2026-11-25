import {
  Alert,
  Badge,
  Button,
  Group,
  Loader,
  Modal,
  Paper,
  ScrollArea,
  SegmentedControl,
  Stack,
  Table,
  Text,
  Title,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { IconAlertTriangle, IconCheck, IconRefresh, IconRotate, IconTrash } from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, ApiError, unwrap, type Schemas } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { DiffViewer } from './DiffViewer'
import { useReloadMutation } from '../data/useDataReload'

type DataFileChange = Schemas['DataFileChange']
type ValidationProblem = Schemas['ValidationProblemDto']

const statusColor: Record<string, string> = {
  modified: 'yellow',
  added: 'green',
  untracked: 'teal',
  deleted: 'red',
  renamed: 'blue',
  clean: 'gray',
}

function problemCounts(problems: ValidationProblem[]) {
  let errors = 0
  let warnings = 0
  for (const p of problems) {
    if (p.level === 'warning') warnings++
    else errors++
  }
  return { errors, warnings }
}

export default function ChangesPage() {
  const { t } = useTranslation('changes')
  const queryClient = useQueryClient()
  const [filter, setFilter] = useState<'changed' | 'all'>('changed')
  const [selectedFile, setSelectedFile] = useState<string | null>(null)
  const [discardTarget, setDiscardTarget] = useState<string | null>(null)
  const [reloadConfirmTarget, setReloadConfirmTarget] = useState<DataFileChange | null>(null)

  const summary = useQuery({
    queryKey: ['changes'],
    queryFn: () => unwrap(api.GET('/api/changes')),
  })

  const visibleFiles = useMemo(() => {
    const files = summary.data?.files ?? []
    if (filter === 'all') return files
    const changed = files.filter((f) => f.gitStatus !== 'clean' || f.problems.length > 0)
    return changed
  }, [summary.data?.files, filter])

  useEffect(() => {
    if (visibleFiles.length === 0) {
      setSelectedFile(null)
      return
    }
    if (selectedFile && visibleFiles.some((f) => f.file === selectedFile)) return
    const firstChanged = visibleFiles.find((f) => f.gitStatus !== 'clean')
    setSelectedFile(firstChanged?.file ?? visibleFiles[0].file)
  }, [visibleFiles, selectedFile])

  const diffQuery = useQuery({
    queryKey: ['changes', 'diff', selectedFile],
    queryFn: () =>
      unwrap(api.GET('/api/changes/{file}/diff', { params: { path: { file: selectedFile! } } })),
    enabled: !!selectedFile,
  })

  const reloadMutation = useReloadMutation<string>({
    mutationFn: (file: string) =>
      unwrap(api.POST('/api/changes/{file}/reload', { params: { path: { file } } })),
    successTitle: (res) => t('reload.successTitle', { file: res.file }),
    rejectedTitle: (res) => t('reload.rejectedTitle', { file: res.file }),
    errorTitle: t('reload.errorTitle'),
    fallbackApplied: () => t('reload.applied'),
    fallbackRejected: () => t('reload.rejected'),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['changes'] })
    },
  })

  const discardMutation = useMutation({
    mutationFn: (file: string) =>
      unwrap(api.POST('/api/changes/{file}/discard', { params: { path: { file } } })),
    onSuccess: (res) => {
      setDiscardTarget(null)
      void queryClient.invalidateQueries({ queryKey: ['changes'] })
      notifications.show({
        color: res.discarded ? 'green' : 'yellow',
        title: res.file,
        message: res.discarded ? t('discard.success') : t('discard.failed'),
      })
    },
    onError: (err) => {
      const msg = err instanceof ApiError ? err.code : String(err)
      notifications.show({
        color: 'red',
        title: t('discard.errorTitle'),
        message: msg,
      })
    },
  })

  const totals = useMemo(() => {
    const files = summary.data?.files ?? []
    const changedCount = files.filter((f) => f.gitStatus !== 'clean').length
    const { errors, warnings } = problemCounts(summary.data?.allProblems ?? [])
    return { changedCount, errors, warnings }
  }, [summary.data])

  const selectedEntry = summary.data?.files.find((f) => f.file === selectedFile) ?? null

  function handleReloadClick(entry: DataFileChange) {
    const { errors } = problemCounts(entry.problems)
    if (errors > 0) {
      setReloadConfirmTarget(entry)
      return
    }
    reloadMutation.mutate(entry.file)
  }

  return (
    <Stack style={{ minWidth: 0 }}>
      <Group justify="space-between" align="flex-start">
        <Stack gap={4}>
          <Title order={2}>{t('title')}</Title>
          <Text c="dimmed">{t('description')}</Text>
        </Stack>
        <Group>
          {summary.isFetching && <Loader size="sm" />}
          <Button
            variant="light"
            leftSection={<IconRefresh size={16} />}
            onClick={() => void queryClient.invalidateQueries({ queryKey: ['changes'] })}
          >
            {t('refresh')}
          </Button>
        </Group>
      </Group>

      {summary.data && (
        <Group gap="xs">
          <Badge color={summary.data.gameConnected ? 'green' : 'red'} variant="light">
            {summary.data.gameConnected ? t('status.gameOnline') : t('status.gameOffline')}
          </Badge>
          <Badge color={summary.data.gitAvailable ? 'blue' : 'orange'} variant="light">
            {summary.data.gitAvailable ? t('status.gitAvailable') : t('status.gitUnavailable')}
          </Badge>
          <Badge color={totals.changedCount > 0 ? 'yellow' : 'gray'} variant="light">
            {t('status.changedFiles', { count: totals.changedCount })}
          </Badge>
          <Badge color={totals.errors > 0 ? 'red' : 'green'} variant="light">
            {t('status.validationErrors', { count: totals.errors })}
          </Badge>
          {totals.warnings > 0 && (
            <Badge color="yellow" variant="light">
              {t('status.validationWarnings', { count: totals.warnings })}
            </Badge>
          )}
        </Group>
      )}

      <ErrorAlert error={summary.error} />

      <Group justify="space-between">
        <SegmentedControl
          value={filter}
          onChange={(v) => setFilter(v as 'changed' | 'all')}
          data={[
            { label: t('filter.changed'), value: 'changed' },
            { label: t('filter.all'), value: 'all' },
          ]}
        />
        {summary.data?.dataDirectory && (
          <Text size="xs" c="dimmed" ff="monospace">
            {summary.data.dataDirectory}
          </Text>
        )}
      </Group>

      <Paper withBorder>
        <ScrollArea>
          <Table highlightOnHover striped miw={720}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('column.file')}</Table.Th>
                <Table.Th>{t('column.status')}</Table.Th>
                <Table.Th>{t('column.diff')}</Table.Th>
                <Table.Th>{t('column.validation')}</Table.Th>
                <Table.Th>{t('column.actions')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {visibleFiles.map((entry) => {
                const { errors, warnings } = problemCounts(entry.problems)
                const isSelected = entry.file === selectedFile
                const isChanged = entry.gitStatus !== 'clean'
                return (
                  <Table.Tr
                    key={entry.file}
                    onClick={() => setSelectedFile(entry.file)}
                    style={{
                      cursor: 'pointer',
                      backgroundColor: isSelected ? 'var(--mantine-color-blue-light)' : undefined,
                    }}
                  >
                    <Table.Td fw={isSelected ? 700 : 500}>{entry.file}</Table.Td>
                    <Table.Td>
                      <Badge color={statusColor[entry.gitStatus] ?? 'gray'} variant="light" size="sm">
                        {t(`gitStatus.${entry.gitStatus}`, { defaultValue: entry.gitStatus })}
                      </Badge>
                    </Table.Td>
                    <Table.Td>
                      {isChanged ? (
                        <Group gap={6}>
                          <Text span size="xs" c="green" ff="monospace">
                            +{entry.addedLines}
                          </Text>
                          <Text span size="xs" c="red" ff="monospace">
                            -{entry.removedLines}
                          </Text>
                        </Group>
                      ) : (
                        <Text span size="xs" c="dimmed">
                          —
                        </Text>
                      )}
                    </Table.Td>
                    <Table.Td>
                      {errors > 0 ? (
                        <Badge color="red" variant="light" size="sm" leftSection={<IconAlertTriangle size={12} />}>
                          {t('validation.errors', { count: errors })}
                        </Badge>
                      ) : warnings > 0 ? (
                        <Badge color="yellow" variant="light" size="sm">
                          {t('validation.warnings', { count: warnings })}
                        </Badge>
                      ) : (
                        <Badge color="green" variant="light" size="sm" leftSection={<IconCheck size={12} />}>
                          {t('validation.ok')}
                        </Badge>
                      )}
                    </Table.Td>
                    <Table.Td onClick={(e) => e.stopPropagation()}>
                      <Group gap="xs">
                        {entry.reloadable ? (
                          <Button
                            size="xs"
                            variant="light"
                            leftSection={<IconRotate size={14} />}
                            disabled={!summary.data?.gameConnected || reloadMutation.isPending}
                            loading={reloadMutation.isPending && reloadMutation.variables === entry.file}
                            onClick={() => handleReloadClick(entry)}
                          >
                            {t('action.reload')}
                          </Button>
                        ) : (
                          <Badge variant="outline" color="gray" size="sm">
                            {t('action.restartRequired')}
                          </Badge>
                        )}
                        {isChanged && (
                          <Button
                            size="xs"
                            color="red"
                            variant="subtle"
                            leftSection={<IconTrash size={14} />}
                            disabled={discardMutation.isPending}
                            onClick={() => setDiscardTarget(entry.file)}
                          >
                            {t('action.discard')}
                          </Button>
                        )}
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                )
              })}
              {summary.data && visibleFiles.length === 0 && (
                <Table.Tr>
                  <Table.Td colSpan={5}>
                    <Text c="dimmed" ta="center" py="md">
                      {t('empty')}
                    </Text>
                  </Table.Td>
                </Table.Tr>
              )}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      </Paper>

      {selectedFile && (
        <Stack gap="sm">
          <Group justify="space-between">
            <Title order={4}>{selectedFile}</Title>
            {diffQuery.isFetching && <Loader size="xs" />}
          </Group>
          <ErrorAlert error={diffQuery.error} />

          {selectedEntry && selectedEntry.problems.length > 0 && (
            <Alert
              color={problemCounts(selectedEntry.problems).errors > 0 ? 'red' : 'yellow'}
              icon={<IconAlertTriangle size={16} />}
              title={t('validation.heading', { count: selectedEntry.problems.length })}
            >
              <Stack gap={4}>
                {selectedEntry.problems.map((p, idx) => (
                  <Text key={idx} size="xs" ff="monospace" style={{ wordBreak: 'break-word' }}>
                    [{p.level}] {p.line != null ? `L${p.line}` : ''}{' '}
                    {p.entry != null ? `#${p.entry}` : ''} {p.message}
                  </Text>
                ))}
              </Stack>
            </Alert>
          )}

          {diffQuery.data && <DiffViewer diff={diffQuery.data.diff} />}
        </Stack>
      )}

      <Modal
        opened={!!discardTarget}
        onClose={() => setDiscardTarget(null)}
        title={t('discard.modalTitle', { file: discardTarget ?? '' })}
      >
        <Stack>
          <Text size="sm">{t('discard.modalBody', { file: discardTarget ?? '' })}</Text>
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setDiscardTarget(null)}>
              {t('discard.cancel')}
            </Button>
            <Button
              color="red"
              loading={discardMutation.isPending}
              onClick={() => discardTarget && discardMutation.mutate(discardTarget)}
            >
              {t('discard.confirm')}
            </Button>
          </Group>
        </Stack>
      </Modal>

      <Modal
        opened={!!reloadConfirmTarget}
        onClose={() => setReloadConfirmTarget(null)}
        title={t('reload.confirmTitle', { file: reloadConfirmTarget?.file ?? '' })}
      >
        <Stack>
          <Text size="sm">
            {t('reload.confirmBody', {
              file: reloadConfirmTarget?.file ?? '',
              count: reloadConfirmTarget ? problemCounts(reloadConfirmTarget.problems).errors : 0,
            })}
          </Text>
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setReloadConfirmTarget(null)}>
              {t('discard.cancel')}
            </Button>
            <Button
              color="yellow"
              onClick={() => {
                const file = reloadConfirmTarget?.file
                setReloadConfirmTarget(null)
                if (file) reloadMutation.mutate(file)
              }}
            >
              {t('reload.confirmAction')}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  )
}
