import {
  Alert,
  Anchor,
  Badge,
  Button,
  Checkbox,
  Group,
  Loader,
  NumberInput,
  Pagination,
  Paper,
  ScrollArea,
  Select,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { IconInfoCircle, IconSearch } from '@tabler/icons-react'
import { keepPreviousData, useMutation, useQuery } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useSearchParams } from 'react-router'
import { api, unwrap } from '../../api/client'
import { can, useMe } from '../../app/auth'
import { ErrorAlert } from '../../app/ErrorAlert'
import { MapPicker } from '../../components/pickers'
import { formatProfession } from '../../lib/gameMeta'
import {
  ACCOUNT_STATUS_IDS,
  AUTHORITY_IDS,
  accountStatusColor,
  authorityColor,
} from '../accounts/accountMeta'
import { CharacterActionsBar } from '../characters/CharacterActionsBar'
import { pkColor } from '../characters/characterMeta'
import { CLASS_LINES, SORTS, filtersFromParams, queryFromFilters, type PlayerFilters } from './playerFilters'

const PAGE_SIZES = ['25', '50', '100', '250']

export default function PlayersPage() {
  const { t, i18n } = useTranslation(['players', 'accounts', 'characters', 'items', 'common'])
  const navigate = useNavigate()
  const me = useMe().data
  const [searchParams, setSearchParams] = useSearchParams()
  const filters = filtersFromParams(searchParams)

  // Text boxes are typed into locally and reach the URL debounced
  const [q, setQ] = useState(filters.q)
  const [ip, setIp] = useState(filters.ip)
  const [debounced] = useDebouncedValue({ q, ip }, 250)

  const setFilters = (patch: Partial<PlayerFilters>) => {
    const next = { ...filtersFromParams(searchParams), ...patch }
    if (!('page' in patch)) next.page = 1
    setSearchParams(queryFromFilters(next), { replace: true })
  }

  useEffect(() => {
    if (debounced.q !== filters.q || debounced.ip !== filters.ip) setFilters({ q: debounced.q, ip: debounced.ip })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [debounced.q, debounced.ip])

  useEffect(() => {
    setQ(filters.q)
    setIp(filters.ip)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchParams.get('q'), searchParams.get('ip')])

  const query = {
    q: filters.q.trim() || undefined,
    ip: filters.ip.trim() || undefined,
    status: filters.status ?? undefined,
    authority: filters.authority ?? undefined,
    online: filters.online,
    classLine: filters.classLine ?? undefined,
    mapId: filters.mapId ?? undefined,
    minLevel: filters.minLevel ?? undefined,
    maxLevel: filters.maxLevel ?? undefined,
    sort: filters.sort ?? undefined,
  }

  const players = useQuery({
    queryKey: ['players', query, filters.page, filters.pageSize],
    queryFn: () =>
      unwrap(
        api.GET('/api/players', {
          params: { query: { ...query, page: filters.page, pageSize: filters.pageSize } },
        }),
      ),
    placeholderData: keepPreviousData,
    refetchInterval: filters.online ? 15_000 : false,
  })

  // Ticked accounts, kept across pages; changing a filter starts over
  const [selected, setSelected] = useState<Set<number>>(new Set())
  const filterKey = JSON.stringify(query)
  useEffect(() => setSelected(new Set()), [filterKey])

  const selectAll = useMutation({
    mutationFn: () => unwrap(api.GET('/api/players/ids', { params: { query } })),
    onSuccess: (r) => setSelected(new Set(r.accountIds)),
  })

  const items = players.data?.items ?? []
  const total = players.data?.total ?? 0
  const pageIds = items.map((p) => p.accountId)
  const pageAllSelected = pageIds.length > 0 && pageIds.every((id) => selected.has(id))
  const pageSomeSelected = pageIds.some((id) => selected.has(id))

  const toggle = (id: number) =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const togglePage = () =>
    setSelected((prev) => {
      const next = new Set(prev)
      for (const id of pageIds) {
        if (pageAllSelected) next.delete(id)
        else next.add(id)
      }
      return next
    })

  const selectedIds = useMemo(() => [...selected], [selected])
  const canModerate = can(me, 'Moderate')
  const canManage = can(me, 'ManageCharacters')
  const hasFilters = searchParams.toString() !== ''

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={2}>{t('players:title')}</Title>
        {players.isFetching && <Loader size="sm" />}
      </Group>
      <Text c="dimmed">{t('players:description')}</Text>

      <Group align="end" wrap="wrap" gap="xs">
        <TextInput
          style={{ flex: 2, minWidth: 220 }}
          placeholder={t('players:searchPlaceholder')}
          leftSection={<IconSearch size={16} />}
          value={q}
          onChange={(e) => setQ(e.currentTarget.value)}
        />
        <Select
          w={150}
          placeholder={t('players:filter.class')}
          clearable
          value={filters.classLine != null ? String(filters.classLine) : null}
          onChange={(v) => setFilters({ classLine: v ? Number(v) : null })}
          data={CLASS_LINES.map((line) => ({
            value: String(line),
            label: t(`items:profession.line.${line}` as never),
          }))}
        />
        <div style={{ minWidth: 200 }}>
          <MapPicker
            label=""
            placeholder={t('players:filter.map')}
            value={filters.mapId}
            onChange={(id) => setFilters({ mapId: id })}
          />
        </div>
        <NumberInput
          w={100}
          placeholder={t('players:filter.minLevel')}
          min={1}
          max={140}
          value={filters.minLevel ?? ''}
          onChange={(v) => setFilters({ minLevel: v === '' ? null : Number(v) })}
        />
        <NumberInput
          w={100}
          placeholder={t('players:filter.maxLevel')}
          min={1}
          max={140}
          value={filters.maxLevel ?? ''}
          onChange={(v) => setFilters({ maxLevel: v === '' ? null : Number(v) })}
        />
        <Switch
          label={t('players:filter.online')}
          checked={filters.online}
          onChange={(e) => setFilters({ online: e.currentTarget.checked })}
        />
      </Group>
      <Group align="end" wrap="wrap" gap="xs">
        <TextInput
          w={160}
          placeholder={t('players:filter.ip')}
          value={ip}
          onChange={(e) => setIp(e.currentTarget.value)}
        />
        <Select
          w={160}
          placeholder={t('players:filter.status')}
          clearable
          value={filters.status != null ? String(filters.status) : null}
          onChange={(v) => setFilters({ status: v ? Number(v) : null })}
          data={ACCOUNT_STATUS_IDS.map((id) => ({
            value: String(id),
            label: t(`accounts:status.${id}` as never),
          }))}
        />
        <Select
          w={160}
          placeholder={t('players:filter.authority')}
          clearable
          value={filters.authority != null ? String(filters.authority) : null}
          onChange={(v) => setFilters({ authority: v ? Number(v) : null })}
          data={AUTHORITY_IDS.map((id) => ({
            value: String(id),
            label: t(`common:authority.${id}` as never),
          }))}
        />
        <Select
          w={170}
          placeholder={t('players:filter.sort')}
          clearable
          value={filters.sort}
          onChange={(v) => setFilters({ sort: v })}
          data={SORTS.map((s) => ({ value: s, label: t(`players:sort.${s}` as never) }))}
        />
        {hasFilters && (
          <Button
            variant="subtle"
            onClick={() => {
              setQ('')
              setIp('')
              setSearchParams({}, { replace: true })
            }}
          >
            {t('players:filter.clear')}
          </Button>
        )}
      </Group>

      <ErrorAlert error={players.error} />
      {players.data && !players.data.gameConnected && (
        <Alert color="yellow" icon={<IconInfoCircle size={18} />}>
          {t('players:gameOffline')}
        </Alert>
      )}

      {selected.size > 0 && (
        <Paper withBorder p="xs">
          <Stack gap={6}>
            <Group gap="sm" wrap="wrap">
              <Text size="sm" fw={600}>
                {t('players:selection.count', { count: selected.size })}
              </Text>
              {pageAllSelected && selected.size < total && (
                <>
                  <Text size="sm" c="dimmed">
                    {t('players:selection.pageSelected', { count: pageIds.length })}
                  </Text>
                  <Anchor component="button" size="sm" onClick={() => selectAll.mutate()}>
                    {t('players:selection.selectAll', { count: total })}
                  </Anchor>
                </>
              )}
              <Anchor component="button" size="sm" onClick={() => setSelected(new Set())}>
                {t('players:selection.clear')}
              </Anchor>
            </Group>
            <CharacterActionsBar
              bulkAccountIds={selectedIds}
              canModerate={canModerate}
              canManage={canManage}
              onChanged={() => void players.refetch()}
            />
          </Stack>
        </Paper>
      )}

      <Paper withBorder>
        <ScrollArea>
          <Table highlightOnHover striped miw={980}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th w={32}>
                  <Checkbox
                    size="xs"
                    checked={pageAllSelected}
                    indeterminate={!pageAllSelected && pageSomeSelected}
                    onChange={togglePage}
                    aria-label={t('players:selection.selectAll', { count: pageIds.length })}
                  />
                </Table.Th>
                <Table.Th>{t('players:column.character')}</Table.Th>
                <Table.Th>{t('players:column.account')}</Table.Th>
                <Table.Th>{t('players:column.level')}</Table.Th>
                <Table.Th>{t('players:column.class')}</Table.Th>
                <Table.Th>{t('players:column.location')}</Table.Th>
                <Table.Th>{t('players:column.silver')}</Table.Th>
                <Table.Th>{t('players:column.cp')}</Table.Th>
                <Table.Th>{t('players:column.pk')}</Table.Th>
                <Table.Th>{t('players:column.status')}</Table.Th>
                <Table.Th>{t('players:column.lastLogin')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {items.map((p) => {
                const hasChar = p.characterId != null
                return (
                  <Table.Tr
                    key={`${p.accountId}-${p.characterId ?? 0}`}
                    style={{ cursor: 'pointer' }}
                    bg={selected.has(p.accountId) ? 'var(--mantine-primary-color-light)' : undefined}
                    onClick={() => navigate(`/admin/players/${p.accountId}`)}
                  >
                    <Table.Td onClick={(e) => e.stopPropagation()}>
                      <Checkbox
                        size="xs"
                        checked={selected.has(p.accountId)}
                        onChange={() => toggle(p.accountId)}
                        aria-label={p.name ?? p.username}
                      />
                    </Table.Td>
                    <Table.Td>
                      {hasChar ? (
                        <>
                          <Text size="sm" fw={600}>
                            {p.name}
                          </Text>
                          <Text size="xs" c="dimmed">
                            #{p.characterId}
                          </Text>
                        </>
                      ) : (
                        <Text size="sm" c="dimmed">
                          {t('players:noCharacter')}
                        </Text>
                      )}
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm">{p.username}</Text>
                      <Group gap={4}>
                        {p.statusId !== 2 && (
                          <Badge size="xs" variant="light" color={accountStatusColor(p.statusId)}>
                            {t(`accounts:status.${p.statusId}` as never)}
                          </Badge>
                        )}
                        {p.authorityId > 1 && (
                          <Badge size="xs" variant="light" color={authorityColor(p.authorityId)}>
                            {t(`common:authority.${p.authorityId}` as never)}
                          </Badge>
                        )}
                      </Group>
                    </Table.Td>
                    <Table.Td>
                      {hasChar && (
                        <Text size="sm">
                          {p.level}
                          {p.rebirths > 0 ? ` (RB ${p.rebirths})` : ''}
                        </Text>
                      )}
                    </Table.Td>
                    <Table.Td>
                      {hasChar && <Text size="sm">{formatProfession(p.currentClass, t)}</Text>}
                    </Table.Td>
                    <Table.Td>
                      {hasChar && (
                        <Text size="sm">
                          {p.mapName ? `${p.mapName} ` : `#${p.mapId} `}
                          <Text span size="xs" c="dimmed">
                            ({p.x}, {p.y})
                          </Text>
                        </Text>
                      )}
                    </Table.Td>
                    <Table.Td>{hasChar && p.silver.toLocaleString(i18n.language)}</Table.Td>
                    <Table.Td>{hasChar && p.conquerPoints.toLocaleString(i18n.language)}</Table.Td>
                    <Table.Td>
                      {hasChar && (
                        <Badge size="sm" variant="light" color={pkColor(p.killPoints)}>
                          {p.killPoints}
                        </Badge>
                      )}
                    </Table.Td>
                    <Table.Td>
                      {hasChar && (
                        <Badge size="sm" variant="light" color={p.online ? 'green' : 'gray'}>
                          {t(p.online ? 'players:online' : 'players:offline')}
                        </Badge>
                      )}
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm">
                        {p.lastLogin ? new Date(p.lastLogin).toLocaleString(i18n.language) : '—'}
                      </Text>
                    </Table.Td>
                  </Table.Tr>
                )
              })}
              {players.data && items.length === 0 && (
                <Table.Tr>
                  <Table.Td colSpan={11}>
                    <Text c="dimmed" ta="center" py="md">
                      {t('players:empty')}
                    </Text>
                  </Table.Td>
                </Table.Tr>
              )}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      </Paper>

      <Group justify="space-between">
        <Group gap="xs">
          <Text size="sm" c="dimmed">
            {t('players:total', { count: total })}
          </Text>
          <Select
            w={80}
            size="xs"
            aria-label={t('players:perPage')}
            value={String(filters.pageSize)}
            onChange={(v) => v && setFilters({ pageSize: Number(v) })}
            data={PAGE_SIZES}
          />
        </Group>
        <Pagination
          total={Math.max(1, Math.ceil(total / filters.pageSize))}
          value={filters.page}
          onChange={(page) => setFilters({ page })}
        />
      </Group>
    </Stack>
  )
}
