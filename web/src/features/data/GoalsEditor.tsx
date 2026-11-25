import {
  ActionIcon,
  Badge,
  Button,
  Group,
  Loader,
  Modal,
  NumberInput,
  Pagination,
  Paper,
  SegmentedControl,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import {
  IconDeviceFloppy,
  IconEdit,
  IconMapPin,
  IconPlus,
  IconRefresh,
  IconRotate,
  IconSearch,
  IconTrash,
} from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, ApiError, unwrap, type Schemas } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { LookfacePicker, MapPicker } from '../../components/pickers'
import { nextGoalId, type GoalRow } from './dataUtils'
import { MapCellPickerModal } from './MapCellPickerModal'
import { ProblemsAlert } from './ProblemsAlert'
import { useDataReload } from './useDataReload'

type DataProblem = Schemas['DataProblemDto']

const PAGE_SIZE = 50

export function GoalsEditor() {
  const { t } = useTranslation('data')
  const queryClient = useQueryClient()
  const reloadMutation = useDataReload('goals.json')

  const query = useQuery({
    queryKey: ['data', 'goals'],
    queryFn: () => unwrap(api.GET('/api/data/goals')),
  })

  const [rows, setRows] = useState<GoalRow[]>([])
  const [hash, setHash] = useState('')
  const [dirty, setDirty] = useState(false)
  const [search, setSearch] = useState('')
  const [roleFilter, setRoleFilter] = useState<'all' | '21' | '22'>('all')
  const [page, setPage] = useState(1)
  const [problems, setProblems] = useState<DataProblem[]>([])

  const [editingIndex, setEditingIndex] = useState<number | null>(null)
  const [draft, setDraft] = useState<GoalRow | null>(null)
  const [mapPickerOpen, setMapPickerOpen] = useState(false)

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
        api.PUT('/api/data/goals', {
          body: {
            hash,
            entries: rows.map((r) => ({
              id: r.id,
              lookface: r.lookface,
              role: r.role,
              level: r.level,
              life: r.life,
              map: r.map,
              x: r.x,
              y: r.y,
            })),
          },
        }),
      ),
    onSuccess: (res) => {
      setProblems(res.problems)
      if (!res.saved) {
        notifications.show({
          color: 'red',
          title: t('status.validationFailedTitle', { file: 'goals.json' }),
          message: t('status.validationFailedMessage'),
        })
        return
      }
      setHash(res.hash)
      setDirty(false)
      void queryClient.invalidateQueries({ queryKey: ['data', 'goals'] })
      notifications.show({
        color: 'green',
        title: t('status.savedTitle', { file: 'goals.json' }),
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
      .filter(({ row }) => {
        if (roleFilter !== 'all' && String(row.role) !== roleFilter) return false
        if (!term) return true
        return (
          String(row.id).includes(term) ||
          String(row.map).includes(term) ||
          String(row.level).includes(term)
        )
      })
  }, [rows, search, roleFilter])

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE))
  const currentPage = Math.min(page, totalPages)
  const pageItems = filtered.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE)

  function openNew() {
    setEditingIndex(-1)
    setDraft({
      id: nextGoalId(rows),
      lookface: 427,
      role: 21,
      level: 20,
      life: 10000,
      map: 1039,
      x: 50,
      y: 50,
    })
  }

  function openEdit(index: number) {
    setEditingIndex(index)
    setDraft({ ...rows[index] })
  }

  function commitDraft() {
    if (!draft) return
    if (editingIndex === -1) {
      setRows((prev) => [...prev, draft])
    } else if (editingIndex != null) {
      setRows((prev) => prev.map((r, i) => (i === editingIndex ? draft : r)))
    }
    setDirty(true)
    setEditingIndex(null)
    setDraft(null)
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
        <Group gap="sm" wrap="wrap">
          <TextInput
            placeholder={t('goals.search')}
            leftSection={<IconSearch size={16} />}
            value={search}
            onChange={(e) => {
              setSearch(e.currentTarget.value)
              setPage(1)
            }}
            w={240}
          />
          <SegmentedControl
            size="xs"
            value={roleFilter}
            onChange={(v) => {
              setRoleFilter(v as 'all' | '21' | '22')
              setPage(1)
            }}
            data={[
              { value: 'all', label: t('goals.roleFilter.all') },
              { value: '21', label: t('goals.roleFilter.stake') },
              { value: '22', label: t('goals.roleFilter.scarecrow') },
            ]}
          />
          <Text size="sm" c="dimmed">
            {t('goals.summary', { count: filtered.length })}
          </Text>
          {dirty && (
            <Badge color="yellow" variant="light">
              {t('status.unsaved')}
            </Badge>
          )}
        </Group>

        <Group gap="xs">
          <Button variant="default" leftSection={<IconPlus size={16} />} onClick={openNew}>
            {t('goals.addGoal')}
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
        <Table striped highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>{t('goals.columns.id')}</Table.Th>
              <Table.Th>{t('goals.columns.role')}</Table.Th>
              <Table.Th>{t('goals.columns.level')}</Table.Th>
              <Table.Th>{t('goals.columns.life')}</Table.Th>
              <Table.Th>{t('goals.columns.lookface')}</Table.Th>
              <Table.Th>{t('goals.columns.location')}</Table.Th>
              <Table.Th w={90}>{t('goals.columns.actions')}</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {pageItems.map(({ row, index }) => (
              <Table.Tr key={`${index}-${row.id}`}>
                <Table.Td>{row.id}</Table.Td>
                <Table.Td>
                  <Badge variant="light" color={row.role === 21 ? 'blue' : 'violet'}>
                    {row.role === 21 ? t('goals.roles.21') : row.role === 22 ? t('goals.roles.22') : row.role}
                  </Badge>
                </Table.Td>
                <Table.Td>{row.level}</Table.Td>
                <Table.Td>{row.life.toLocaleString()}</Table.Td>
                <Table.Td>{row.lookface}</Table.Td>
                <Table.Td>
                  {row.map} ({row.x}, {row.y})
                </Table.Td>
                <Table.Td>
                  <Group gap={4} wrap="nowrap">
                    <ActionIcon variant="subtle" aria-label={t('goals.editGoal', { id: row.id })} onClick={() => openEdit(index)}>
                      <IconEdit size={16} />
                    </ActionIcon>
                    <ActionIcon variant="subtle" color="red" aria-label={t('actions.delete')} onClick={() => removeRow(index)}>
                      <IconTrash size={16} />
                    </ActionIcon>
                  </Group>
                </Table.Td>
              </Table.Tr>
            ))}
            {pageItems.length === 0 && (
              <Table.Tr>
                <Table.Td colSpan={7}>
                  <Text c="dimmed" ta="center" py="md">
                    {t('goals.empty')}
                  </Text>
                </Table.Td>
              </Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </Paper>

      {totalPages > 1 && (
        <Group justify="center">
          <Pagination value={currentPage} onChange={setPage} total={totalPages} />
        </Group>
      )}

      <Modal
        opened={draft != null}
        onClose={() => {
          setEditingIndex(null)
          setDraft(null)
        }}
        title={editingIndex === -1 ? t('goals.newGoal') : t('goals.editGoal', { id: draft?.id })}
        size="md"
      >
        {draft && (
          <Stack gap="sm">
            <Group grow>
              <NumberInput
                label={t('goals.fields.id')}
                value={draft.id}
                min={1}
                allowDecimal={false}
                onChange={(v) => setDraft({ ...draft, id: Number(v) || 0 })}
              />
              <Select
                label={t('goals.fields.role')}
                value={String(draft.role)}
                data={[
                  { value: '21', label: t('goals.roles.21') },
                  { value: '22', label: t('goals.roles.22') },
                ]}
                onChange={(v) => setDraft({ ...draft, role: Number(v) || 21 })}
              />
            </Group>

            <Group grow>
              <NumberInput
                label={t('goals.fields.level')}
                value={draft.level}
                min={1}
                max={255}
                allowDecimal={false}
                onChange={(v) => setDraft({ ...draft, level: Number(v) || 1 })}
              />
              <NumberInput
                label={t('goals.fields.life')}
                value={draft.life}
                min={1}
                allowDecimal={false}
                onChange={(v) => setDraft({ ...draft, life: Number(v) || 1 })}
              />
            </Group>

            <LookfacePicker
              label={t('goals.fields.lookface')}
              mode="lookface"
              value={draft.lookface}
              clearable={false}
              onChange={(v) => setDraft({ ...draft, lookface: v ?? 0 })}
            />

            <MapPicker
              label={t('goals.fields.map')}
              value={draft.map || null}
              clearable={false}
              onChange={(id) => setDraft({ ...draft, map: id ?? 1039 })}
            />

            <Group align="flex-end" grow>
              <NumberInput
                label={t('goals.fields.x')}
                value={draft.x}
                min={0}
                max={65535}
                allowDecimal={false}
                onChange={(v) => setDraft({ ...draft, x: Number(v) || 0 })}
              />
              <NumberInput
                label={t('goals.fields.y')}
                value={draft.y}
                min={0}
                max={65535}
                allowDecimal={false}
                onChange={(v) => setDraft({ ...draft, y: Number(v) || 0 })}
              />
              <Button variant="light" leftSection={<IconMapPin size={16} />} onClick={() => setMapPickerOpen(true)}>
                {t('actions.pickOnMap')}
              </Button>
            </Group>

            <Group justify="flex-end" mt="sm">
              <Button
                variant="default"
                onClick={() => {
                  setEditingIndex(null)
                  setDraft(null)
                }}
              >
                {t('actions.cancel')}
              </Button>
              <Button onClick={commitDraft}>{t('actions.apply')}</Button>
            </Group>
          </Stack>
        )}
      </Modal>

      {draft && (
        <MapCellPickerModal
          opened={mapPickerOpen}
          onClose={() => setMapPickerOpen(false)}
          initialMap={draft.map}
          initialX={draft.x}
          initialY={draft.y}
          onApply={(map, x, y) => setDraft({ ...draft, map, x, y })}
        />
      )}
    </Stack>
  )
}
