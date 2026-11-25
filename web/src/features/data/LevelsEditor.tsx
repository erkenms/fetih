import {
  ActionIcon,
  Badge,
  Button,
  Group,
  Loader,
  NumberInput,
  Paper,
  ScrollArea,
  Stack,
  Table,
  Text,
  TextInput,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { IconDeviceFloppy, IconPlus, IconRefresh, IconRotate, IconSearch, IconTrash } from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, ApiError, unwrap, type Schemas } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { expBallsForLevel, nextLevelNumber, type LevelRow } from './dataUtils'
import { ProblemsAlert } from './ProblemsAlert'
import { useDataReload } from './useDataReload'

type DataProblem = Schemas['DataProblemDto']

export function LevelsEditor() {
  const { t } = useTranslation('data')
  const queryClient = useQueryClient()
  const reloadMutation = useDataReload('levels.json')

  const query = useQuery({
    queryKey: ['data', 'levels'],
    queryFn: () => unwrap(api.GET('/api/data/levels')),
  })

  const [rows, setRows] = useState<LevelRow[]>([])
  const [hash, setHash] = useState('')
  const [dirty, setDirty] = useState(false)
  const [search, setSearch] = useState('')
  const [problems, setProblems] = useState<DataProblem[]>([])

  useEffect(() => {
    if (query.data && !dirty) {
      setRows(query.data.entries.map((e) => ({ ...e })))
      setHash(query.data.hash)
      setProblems(query.data.problems)
    }
  }, [query.data, dirty])

  const saveMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.PUT('/api/data/levels', {
          body: {
            hash,
            entries: rows.map((r) => ({
              level: r.level,
              experience: r.experience,
              upLevTime: r.upLevTime,
            })),
          },
        }),
      ),
    onSuccess: (res) => {
      setProblems(res.problems)
      if (!res.saved) {
        notifications.show({
          color: 'red',
          title: t('status.validationFailedTitle', { file: 'levels.json' }),
          message: t('status.validationFailedMessage'),
        })
        return
      }
      setHash(res.hash)
      setDirty(false)
      void queryClient.invalidateQueries({ queryKey: ['data', 'levels'] })
      notifications.show({
        color: 'green',
        title: t('status.savedTitle', { file: 'levels.json' }),
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

  const filtered = useMemo(() => {
    const term = search.trim()
    return rows
      .map((row, index) => ({ row, index }))
      .filter(({ row }) => !term || String(row.level).includes(term))
  }, [rows, search])

  const levelCap = useMemo(() => {
    if (rows.length === 0) return 0
    return rows.reduce((m, r) => (r.level > m ? r.level : m), 0) + 1
  }, [rows])

  function updateRow(index: number, patch: Partial<LevelRow>) {
    setRows((prev) => prev.map((r, i) => (i === index ? { ...r, ...patch } : r)))
    setDirty(true)
  }

  function addLevel() {
    const next = nextLevelNumber(rows)
    const last = rows[rows.length - 1]
    setRows((prev) => [
      ...prev,
      {
        level: next,
        experience: last ? last.experience : 100,
        upLevTime: last ? last.upLevTime : 2,
      },
    ])
    setDirty(true)
  }

  function removeRow(index: number) {
    setRows((prev) => prev.filter((_, i) => i !== index))
    setDirty(true)
  }

  function resetChanges() {
    if (!query.data) return
    setRows(query.data.entries.map((e) => ({ ...e })))
    setHash(query.data.hash)
    setProblems(query.data.problems)
    setDirty(false)
  }

  if (query.isLoading) return <Loader />
  if (query.error) return <ErrorAlert error={query.error} />

  return (
    <Stack gap="md">
      <Group justify="space-between" align="center" wrap="wrap">
        <Group gap="sm">
          <TextInput
            placeholder={t('levels.search')}
            leftSection={<IconSearch size={16} />}
            value={search}
            onChange={(e) => setSearch(e.currentTarget.value)}
            w={220}
          />
          <Text size="sm" c="dimmed">
            {t('levels.summary', { count: rows.length, cap: levelCap })}
          </Text>
          {dirty && (
            <Badge color="yellow" variant="light">
              {t('status.unsaved')}
            </Badge>
          )}
        </Group>

        <Group gap="xs">
          <Button variant="default" leftSection={<IconPlus size={16} />} onClick={addLevel}>
            {t('levels.addLevel')}
          </Button>
          {dirty && (
            <Button variant="subtle" color="gray" leftSection={<IconRotate size={16} />} onClick={resetChanges}>
              {t('actions.reset')}
            </Button>
          )}
          <Button
            leftSection={<IconDeviceFloppy size={16} />}
            loading={saveMutation.isPending}
            disabled={!dirty}
            onClick={() => saveMutation.mutate()}
          >
            {t('actions.save')}
          </Button>
          <Button
            variant="light"
            leftSection={<IconRefresh size={16} />}
            loading={reloadMutation.isPending}
            onClick={() => reloadMutation.mutate()}
          >
            {t('actions.reload')}
          </Button>
        </Group>
      </Group>

      {saveMutation.error && <ErrorAlert error={saveMutation.error} />}
      <ProblemsAlert problems={problems} />

      <Paper withBorder>
        <ScrollArea h={560}>
          <Table striped highlightOnHover stickyHeader>
            <Table.Thead>
              <Table.Tr>
                <Table.Th w={110}>{t('levels.columns.level')}</Table.Th>
                <Table.Th w={220}>{t('levels.columns.experience')}</Table.Th>
                <Table.Th w={160}>{t('levels.columns.upLevTime')}</Table.Th>
                <Table.Th w={140}>{t('levels.columns.expBalls')}</Table.Th>
                <Table.Th w={60}>{t('levels.columns.actions')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {filtered.map(({ row, index }) => (
                <Table.Tr key={`${index}-${row.line ?? 'new'}`}>
                  <Table.Td>
                    <NumberInput
                      size="xs"
                      value={row.level}
                      min={1}
                      max={255}
                      allowDecimal={false}
                      onChange={(v) => updateRow(index, { level: Number(v) || 0 })}
                    />
                  </Table.Td>
                  <Table.Td>
                    <NumberInput
                      size="xs"
                      value={row.experience}
                      min={0}
                      thousandSeparator=","
                      allowDecimal={false}
                      onChange={(v) => updateRow(index, { experience: Number(v) || 0 })}
                    />
                  </Table.Td>
                  <Table.Td>
                    <NumberInput
                      size="xs"
                      value={row.upLevTime}
                      min={0}
                      allowDecimal={false}
                      onChange={(v) => updateRow(index, { upLevTime: Number(v) || 0 })}
                    />
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" c="dimmed">
                      {expBallsForLevel(row.upLevTime).toFixed(2)}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <ActionIcon
                      variant="subtle"
                      color="red"
                      aria-label={t('actions.delete')}
                      onClick={() => removeRow(index)}
                    >
                      <IconTrash size={16} />
                    </ActionIcon>
                  </Table.Td>
                </Table.Tr>
              ))}
              {filtered.length === 0 && (
                <Table.Tr>
                  <Table.Td colSpan={5}>
                    <Text c="dimmed" ta="center" py="md">
                      {t('levels.empty')}
                    </Text>
                  </Table.Td>
                </Table.Tr>
              )}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      </Paper>
    </Stack>
  )
}
