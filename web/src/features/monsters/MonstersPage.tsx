import {
  ActionIcon,
  Badge,
  Button,
  Code,
  Group,
  Loader,
  NumberInput,
  Pagination,
  Paper,
  ScrollArea,
  SegmentedControl,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
  Title,
  Tooltip,
} from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import { IconCopy, IconEdit, IconPlus, IconRefresh, IconRotate } from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, ApiError, unwrap, type Schemas } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { MonsterEditorDrawer } from './MonsterEditorDrawer'
import {
  ATTACK_USER_GUARD,
  createEmptyMonster,
  filterMonsters,
  hasAttackUserFlag,
  nextAvailableMonsterId,
  type MonsterDraft,
  type MonsterFilterRole,
} from './monsterUtils'

const pageSize = 50
type MonsterProblem = Schemas['MonsterProblemDto']

export default function MonstersPage() {
  const { t } = useTranslation('monsters')
  const queryClient = useQueryClient()

  const [search, setSearch] = useState('')
  const [role, setRole] = useState<MonsterFilterRole>('all')
  const [minLevel, setMinLevel] = useState<number | string>('')
  const [maxLevel, setMaxLevel] = useState<number | string>('')
  const [spawnedOnly, setSpawnedOnly] = useState(false)
  const [page, setPage] = useState(1)
  const [debouncedSearch] = useDebouncedValue(search, 200)

  const [drawerOpen, setDrawerOpen] = useState(false)
  const [drawerMode, setDrawerMode] = useState<'create' | 'edit'>('edit')
  const [editingDraft, setEditingDraft] = useState<MonsterDraft | null>(null)
  const [originalId, setOriginalId] = useState<number | null>(null)
  const [serverProblems, setServerProblems] = useState<MonsterProblem[]>([])

  const listQuery = useQuery({
    queryKey: ['studio', 'monsters'],
    queryFn: () => unwrap(api.GET('/api/studio/monsters')),
  })

  const rows = listQuery.data?.monsters ?? []
  const existingIds = useMemo(() => new Set(rows.map((r) => r.monster.id)), [rows])

  const filtered = useMemo(
    () =>
      filterMonsters(rows, {
        search: debouncedSearch,
        role,
        minLevel: typeof minLevel === 'number' ? minLevel : undefined,
        maxLevel: typeof maxLevel === 'number' ? maxLevel : undefined,
        spawnedOnly,
      }),
    [rows, debouncedSearch, role, minLevel, maxLevel, spawnedOnly],
  )

  const pageCount = Math.max(1, Math.ceil(filtered.length / pageSize))
  const safePage = Math.min(page, pageCount)
  const pageRows = filtered.slice((safePage - 1) * pageSize, safePage * pageSize)

  const saveMutation = useMutation({
    mutationFn: async (draft: MonsterDraft) => {
      const hash = listQuery.data?.hash ?? null
      const res =
        drawerMode === 'create'
          ? await api.POST('/api/studio/monsters', { body: { hash, monster: draft } })
          : await api.PUT('/api/studio/monsters/{id}', {
              params: { path: { id: originalId! } },
              body: { hash, monster: draft },
            })
      if (!res.response.ok) {
        const errBody = res.error as { error?: string; problems?: MonsterProblem[] } | undefined
        throw { status: res.response.status, code: errBody?.error ?? 'server_error', problems: errBody?.problems ?? [] }
      }
      return res.data!
    },
    onSuccess: (res) => {
      setServerProblems([])
      setDrawerOpen(false)
      void queryClient.invalidateQueries({ queryKey: ['studio', 'monsters'] })
      void queryClient.invalidateQueries({ queryKey: ['changes'] })
      notifications.show({
        color: res.warnings.length > 0 ? 'yellow' : 'green',
        title:
          drawerMode === 'create'
            ? t('notification.createdTitle', { name: res.monster.name })
            : t('notification.savedTitle', { name: res.monster.name }),
        message:
          res.warnings.length > 0
            ? res.warnings.map((w) => w.message).join('; ')
            : drawerMode === 'create'
              ? t('notification.createdBody')
              : t('notification.savedBody'),
      })
    },
    onError: (err: { status?: number; code?: string; problems?: MonsterProblem[] }) => {
      if (err.problems && err.problems.length > 0) setServerProblems(err.problems)
      if (err.status === 409) {
        notifications.show({
          color: 'orange',
          title: t('notification.conflictTitle'),
          message: t('notification.conflictBody'),
        })
      } else {
        notifications.show({
          color: 'red',
          title: t('notification.saveErrorTitle'),
          message: err.problems?.[0]?.message ?? err.code ?? String(err),
        })
      }
    },
  })

  const deleteMutation = useMutation({
    mutationFn: async (id: number) => {
      const hash = listQuery.data?.hash
      const res = await api.DELETE('/api/studio/monsters/{id}', {
        params: { path: { id }, query: { hash } },
      })
      if (!res.response.ok) {
        const errBody = res.error as { error?: string; problems?: MonsterProblem[] } | undefined
        throw { status: res.response.status, code: errBody?.error ?? 'server_error', problems: errBody?.problems ?? [] }
      }
      return res.data!
    },
    onSuccess: () => {
      const deletedName = editingDraft?.name ?? `#${originalId}`
      setServerProblems([])
      setDrawerOpen(false)
      void queryClient.invalidateQueries({ queryKey: ['studio', 'monsters'] })
      void queryClient.invalidateQueries({ queryKey: ['changes'] })
      notifications.show({
        color: 'green',
        title: t('notification.deletedTitle', { name: deletedName }),
        message: t('notification.deletedBody'),
      })
    },
    onError: (err: { status?: number; code?: string; problems?: MonsterProblem[] }) => {
      if (err.problems && err.problems.length > 0) setServerProblems(err.problems)
      if (err.code === 'monster_in_use') {
        notifications.show({
          color: 'red',
          title: t('notification.inUseTitle'),
          message: err.problems?.[0]?.message ?? t('notification.inUseBody'),
        })
      } else if (err.status === 409) {
        notifications.show({
          color: 'orange',
          title: t('notification.conflictTitle'),
          message: t('notification.conflictBody'),
        })
      } else {
        notifications.show({
          color: 'red',
          title: t('notification.saveErrorTitle'),
          message: err.code ?? String(err),
        })
      }
    },
  })

  const reloadMutation = useMutation({
    mutationFn: () => unwrap(api.POST('/api/studio/monsters/reload')),
    onSuccess: (res) => {
      if (res.applied) {
        notifications.show({
          color: res.warnings.length > 0 ? 'yellow' : 'green',
          title: t('notification.reloadSuccessTitle'),
          message: res.summary ?? t('notification.reloadApplied'),
        })
      } else {
        notifications.show({
          color: 'red',
          title: t('notification.reloadRejectedTitle'),
          message: res.errors.join('; ') || t('notification.reloadRejected'),
        })
      }
    },
    onError: (err) => {
      notifications.show({
        color: 'red',
        title: t('notification.reloadErrorTitle'),
        message: err instanceof ApiError ? err.code : String(err),
      })
    },
  })

  const openCreate = () => {
    setServerProblems([])
    setDrawerMode('create')
    setOriginalId(null)
    setEditingDraft(createEmptyMonster(nextAvailableMonsterId(existingIds)))
    setDrawerOpen(true)
  }

  const openEdit = (monster: MonsterDraft) => {
    setServerProblems([])
    setDrawerMode('edit')
    setOriginalId(monster.id)
    setEditingDraft({ ...monster, spells: [...monster.spells] })
    setDrawerOpen(true)
  }

  const openDuplicate = (monster: MonsterDraft) => {
    setServerProblems([])
    setDrawerMode('create')
    setOriginalId(null)
    setEditingDraft({
      ...monster,
      id: nextAvailableMonsterId(existingIds),
      name: `${monster.name}Copy`,
      spells: [...monster.spells],
    })
    setDrawerOpen(true)
  }

  const totalProblems = listQuery.data?.problems.length ?? 0

  return (
    <Stack>
      <Group justify="space-between" align="flex-start" wrap="wrap">
        <Stack gap={4}>
          <Group gap="sm">
            <Title order={2}>{t('title')}</Title>
            {listQuery.isFetching && <Loader size="sm" />}
          </Group>
          <Text c="dimmed" size="sm">
            {t('description')}
          </Text>
        </Stack>

        <Group gap="xs">
          <Button leftSection={<IconPlus size={16} />} onClick={openCreate}>
            {t('newMonster')}
          </Button>
          <Button
            variant="light"
            leftSection={<IconRotate size={16} />}
            loading={reloadMutation.isPending}
            disabled={!listQuery.data?.gameOnline}
            onClick={() => reloadMutation.mutate()}
          >
            {t('reload')}
          </Button>
          <Button
            variant="default"
            leftSection={<IconRefresh size={16} />}
            onClick={() => void listQuery.refetch()}
          >
            {t('refresh')}
          </Button>
        </Group>
      </Group>

      <Group gap="xs">
        <Badge color={listQuery.data?.gameOnline ? 'green' : 'gray'} variant="light">
          {listQuery.data?.gameOnline ? t('status.gameOnline') : t('status.gameOffline')}
        </Badge>
        <Badge variant="light">{t('status.total', { count: rows.length })}</Badge>
        {totalProblems > 0 && (
          <Badge color="yellow" variant="light">
            {t('status.problems', { count: totalProblems })}
          </Badge>
        )}
      </Group>

      <Group align="flex-end" wrap="wrap">
        <TextInput
          style={{ flex: 1, minWidth: 220 }}
          placeholder={t('filter.searchPlaceholder')}
          value={search}
          onChange={(e) => {
            setSearch(e.currentTarget.value)
            setPage(1)
          }}
        />
        <NumberInput
          w={100}
          placeholder={t('filter.minLevel')}
          min={0}
          max={255}
          value={minLevel}
          onChange={(v) => {
            setMinLevel(v)
            setPage(1)
          }}
        />
        <NumberInput
          w={100}
          placeholder={t('filter.maxLevel')}
          min={0}
          max={255}
          value={maxLevel}
          onChange={(v) => {
            setMaxLevel(v)
            setPage(1)
          }}
        />
        <SegmentedControl
          value={role}
          onChange={(v) => {
            setRole(v as MonsterFilterRole)
            setPage(1)
          }}
          data={[
            { value: 'all', label: t('filter.role.all') },
            { value: 'boss', label: t('filter.role.boss') },
            { value: 'guard', label: t('filter.role.guard') },
            { value: 'magic', label: t('filter.role.magic') },
          ]}
        />
        <Switch
          label={t('filter.spawnedOnly')}
          checked={spawnedOnly}
          onChange={(e) => {
            setSpawnedOnly(e.currentTarget.checked)
            setPage(1)
          }}
        />
      </Group>

      <ErrorAlert error={listQuery.error} />

      <Paper withBorder>
        <ScrollArea>
          <Table highlightOnHover striped miw={860}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('column.id')}</Table.Th>
                <Table.Th>{t('column.name')}</Table.Th>
                <Table.Th>{t('column.level')}</Table.Th>
                <Table.Th>{t('column.life')}</Table.Th>
                <Table.Th>{t('column.attack')}</Table.Th>
                <Table.Th>{t('column.defense')}</Table.Th>
                <Table.Th>{t('column.lookface')}</Table.Th>
                <Table.Th>{t('column.spawns')}</Table.Th>
                <Table.Th>{t('column.actions')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {pageRows.map((row) => {
                const m = row.monster
                const isGuard = hasAttackUserFlag(m.attackUser, ATTACK_USER_GUARD)
                const hasMagic = m.magicType > 0 || m.spells.length > 0
                return (
                  <Table.Tr key={m.id} onClick={() => openEdit(m)} style={{ cursor: 'pointer' }}>
                    <Table.Td>
                      <Code>{m.id}</Code>
                    </Table.Td>
                    <Table.Td>
                      <Group gap={6} wrap="nowrap">
                        <Text fw={500}>{m.name}</Text>
                        {m.boss && (
                          <Badge size="xs" color="red" variant="light">
                            {t('badge.boss')}
                          </Badge>
                        )}
                        {isGuard && (
                          <Badge size="xs" color="blue" variant="light">
                            {t('badge.guard')}
                          </Badge>
                        )}
                        {hasMagic && (
                          <Badge size="xs" color="grape" variant="light">
                            {t('badge.magic')}
                          </Badge>
                        )}
                        {row.hasCustomDrops && (
                          <Badge size="xs" color="teal" variant="outline">
                            {t('badge.drops')}
                          </Badge>
                        )}
                      </Group>
                    </Table.Td>
                    <Table.Td>{m.level}</Table.Td>
                    <Table.Td>{m.life.toLocaleString()}</Table.Td>
                    <Table.Td>
                      {m.attackMin}–{m.attackMax}
                    </Table.Td>
                    <Table.Td>{m.defense}</Table.Td>
                    <Table.Td>
                      <Code>{m.lookface}</Code>
                    </Table.Td>
                    <Table.Td>
                      {row.spawnAreaCount > 0 ? (
                        <Text size="xs">
                          {t('spawnsSummary', { areas: row.spawnAreaCount, total: row.totalSpawnAmount })}
                        </Text>
                      ) : row.bossScheduleCount > 0 ? (
                        <Badge size="xs" color="orange" variant="light">
                          {t('badge.scheduled')}
                        </Badge>
                      ) : (
                        <Text size="xs" c="dimmed">
                          {t('spawnsNone')}
                        </Text>
                      )}
                    </Table.Td>
                    <Table.Td onClick={(e) => e.stopPropagation()}>
                      <Group gap={4} wrap="nowrap">
                        <Tooltip label={t('action.edit')}>
                          <ActionIcon variant="subtle" onClick={() => openEdit(m)} aria-label={t('action.edit')}>
                            <IconEdit size={16} />
                          </ActionIcon>
                        </Tooltip>
                        <Tooltip label={t('action.duplicate')}>
                          <ActionIcon
                            variant="subtle"
                            color="gray"
                            onClick={() => openDuplicate(m)}
                            aria-label={t('action.duplicate')}
                          >
                            <IconCopy size={16} />
                          </ActionIcon>
                        </Tooltip>
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                )
              })}
              {listQuery.data && pageRows.length === 0 && (
                <Table.Tr>
                  <Table.Td colSpan={9}>
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

      <Group justify="space-between">
        <Text size="sm" c="dimmed">
          {t('showing', { shown: filtered.length, total: rows.length })}
        </Text>
        <Pagination total={pageCount} value={safePage} onChange={setPage} />
      </Group>

      <MonsterEditorDrawer
        opened={drawerOpen}
        mode={drawerMode}
        initialDraft={editingDraft}
        originalId={originalId}
        existingIds={existingIds}
        saving={saveMutation.isPending}
        deleting={deleteMutation.isPending}
        serverProblems={serverProblems}
        onClose={() => setDrawerOpen(false)}
        onSave={(draft) => saveMutation.mutate(draft)}
        onDelete={(id) => deleteMutation.mutate(id)}
        onDuplicate={openDuplicate}
      />
    </Stack>
  )
}
