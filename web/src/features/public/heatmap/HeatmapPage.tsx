import { Group, Loader, Select, Table, Text, TextInput } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap } from '../../../api/client'
import { ErrorAlert } from '../../../app/ErrorAlert'
import { Panel } from '../../../theme/Panel'
import classes from './HeatmapPage.module.css'
import { partitionWorldMaps, type HeatmapMapItem } from './worldMapData'
import { WorldMapSvg } from './WorldMapSvg'

type SortKey = 'players' | 'name'

export default function HeatmapPage() {
  const { t, i18n } = useTranslation('publicHeatmap')
  const [selectedMapId, setSelectedMapId] = useState<number | null>(null)
  const [search, setSearch] = useState('')
  const [sortBy, setSortBy] = useState<SortKey>('players')

  const query = useQuery({
    queryKey: ['public', 'heatmap'],
    queryFn: () => unwrap(api.GET('/api/public/heatmap')),
    refetchInterval: 60_000,
  })

  const data = query.data
  const maps: HeatmapMapItem[] = useMemo(() => data?.maps ?? [], [data])
  const { other } = useMemo(() => partitionWorldMaps(maps), [maps])

  const filteredMaps = useMemo(() => {
    const q = search.trim().toLowerCase()
    const list = q
      ? maps.filter((m) => m.name.toLowerCase().includes(q) || String(m.mapId).includes(q))
      : [...maps]
    if (sortBy === 'name') {
      list.sort((a, b) => a.name.localeCompare(b.name) || b.players - a.players)
    } else {
      list.sort((a, b) => b.players - a.players || a.mapId - b.mapId)
    }
    return list
  }, [maps, search, sortBy])

  const cachedAtLabel = useMemo(() => {
    if (!data?.cachedAt) return '—'
    try {
      return new Intl.DateTimeFormat(i18n.resolvedLanguage ?? i18n.language, {
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
      }).format(new Date(data.cachedAt))
    } catch {
      return data.cachedAt
    }
  }, [data?.cachedAt, i18n.language, i18n.resolvedLanguage])

  return (
    <div className={classes.stack}>
      <Panel
        title={t('title')}
        icon="icons/items/724426.png"
        aside={data ? t('updatedAt', { time: cachedAtLabel }) : undefined}
      >
        {query.isLoading && (
          <Group gap="xs">
            <Loader size="xs" />
            <Text size="xs">{t('loading')}</Text>
          </Group>
        )}
        {query.error && <ErrorAlert error={query.error} />}
        {data && (
          <>
            <div className={classes.summaryRow}>
              <div className={classes.summaryStats}>
                <div className={classes.statItem}>
                  <span className={classes.statLabel}>{t('summary.status')}:</span>
                  <span className={data.gameOnline ? classes.statusOnline : classes.statusOffline}>
                    {data.gameOnline ? t('summary.online') : t('summary.offline')}
                  </span>
                </div>
                <div className={classes.statItem}>
                  <span className={classes.statLabel}>{t('summary.totalOnline')}:</span>
                  <span className={classes.statValue}>{data.totalOnline}</span>
                </div>
                <div className={classes.statItem}>
                  <span className={classes.statLabel}>{t('summary.activeMaps')}:</span>
                  <span className={classes.statValue}>{maps.length}</span>
                </div>
              </div>
              <Text size="xs" c="dimmed">
                {t('summary.refreshNote', { seconds: data.cacheSeconds })}
              </Text>
            </div>
            <div className={classes.privacyNote}>
              {t('summary.privacyNote', { min: data.minPlayers })}
            </div>
          </>
        )}
      </Panel>

      <Panel title={t('map.title')} icon="icons/items/711394.png">
        <WorldMapSvg maps={maps} selectedMapId={selectedMapId} onSelect={setSelectedMapId} />

        {other.length > 0 && (
          <div className={classes.otherZones}>
            <Text size="xs" fw={700}>
              {t('map.otherZones')}:
            </Text>
            {other.map((m) => {
              const selected = selectedMapId === m.mapId
              return (
                <button
                  key={m.mapId}
                  type="button"
                  className={`${classes.otherZoneButton} ${selected ? classes.otherZoneSelected : ''}`}
                  onClick={() => setSelectedMapId(selected ? null : m.mapId)}
                >
                  {m.name} ({m.players})
                </button>
              )
            })}
          </div>
        )}
      </Panel>

      <Panel
        title={t('list.title')}
        icon="icons/items/200007.png"
        aside={
          <Group gap="xs">
            <TextInput
              size="xs"
              placeholder={t('list.searchPlaceholder')}
              value={search}
              onChange={(e) => setSearch(e.currentTarget.value)}
              style={{ width: 170 }}
            />
            <Select
              size="xs"
              value={sortBy}
              onChange={(v) => setSortBy((v as SortKey) ?? 'players')}
              data={[
                { value: 'players', label: t('list.sortPlayers') },
                { value: 'name', label: t('list.sortName') },
              ]}
              style={{ width: 130 }}
            />
          </Group>
        }
      >
        {!data || !data.gameOnline ? (
          <Text size="xs">{t('list.offline')}</Text>
        ) : filteredMaps.length === 0 ? (
          <Text size="xs">
            {maps.length === 0
              ? t('list.emptyThreshold', { min: data.minPlayers })
              : t('list.noMatches')}
          </Text>
        ) : (
          <Table striped highlightOnHover verticalSpacing={4}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th style={{ width: 46 }} />
                <Table.Th>{t('list.columns.map')}</Table.Th>
                <Table.Th style={{ width: 90 }}>{t('list.columns.players')}</Table.Th>
                <Table.Th style={{ width: 165 }}>{t('list.columns.share')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {filteredMaps.map((m) => {
                const pct = Math.round(m.share * 100)
                const selected = selectedMapId === m.mapId
                return (
                  <Table.Tr
                    key={m.mapId}
                    className={selected ? classes.selectedRow : undefined}
                    style={{ cursor: 'pointer' }}
                    onClick={() => setSelectedMapId(selected ? null : m.mapId)}
                  >
                    <Table.Td>
                      {m.minimap && (
                        <img
                          className={classes.minimap}
                          src={`/assets/${m.minimap}`}
                          alt=""
                          onError={(e) => (e.currentTarget.style.display = 'none')}
                        />
                      )}
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs" fw={700}>
                        {m.name}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs" fw={700}>
                        {m.players}
                      </Text>
                    </Table.Td>
                    <Table.Td>
                      <Group gap={8} wrap="nowrap">
                        <div className={classes.shareBarTrack}>
                          <div className={classes.shareBarFill} style={{ width: `${Math.max(pct, 4)}%` }} />
                        </div>
                        <Text size="xs">{pct}%</Text>
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                )
              })}
            </Table.Tbody>
          </Table>
        )}
      </Panel>
    </div>
  )
}
