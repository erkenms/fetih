import {
  Anchor,
  Badge,
  Group,
  Loader,
  Pagination,
  Paper,
  ScrollArea,
  SegmentedControl,
  Select,
  Stack,
  Table,
  Tabs,
  Text,
  Title,
} from '@mantine/core'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router'
import { api, unwrap } from '../../../api/client'
import { ErrorAlert } from '../../../app/ErrorAlert'
import { nobilityColors } from '../../../lib/gameMeta'
import { baseClassOptions, classKey, formatNumber } from './format'
import { PlayerAvatar } from './PlayerAvatar'

const pageSize = 25
const validTabs = ['level', 'nobility', 'guilds', 'arena', 'flowers', 'pk', 'families'] as const
type RankingTab = (typeof validTabs)[number]

export default function RankingsPage() {
  const { kind } = useParams<{ kind?: string }>()
  const navigate = useNavigate()
  const { t, i18n } = useTranslation('publicSite')

  const activeTab: RankingTab = validTabs.includes(kind as RankingTab) ? (kind as RankingTab) : 'level'
  const [page, setPage] = useState(1)
  const [baseClass, setBaseClass] = useState<string | null>(null)
  const [guildSort, setGuildSort] = useState('fund')
  const [arenaMode, setArenaMode] = useState('points')
  const [season, setSeason] = useState<string | null>(null)
  const [flowerKind, setFlowerKind] = useState('redRose')

  useEffect(() => {
    setPage(1)
  }, [activeTab, baseClass, guildSort, arenaMode, season, flowerKind])

  const seasons = useQuery({
    queryKey: ['public', 'arena', 'seasons'],
    queryFn: () => unwrap(api.GET('/api/public/rankings/arena/seasons')),
    enabled: activeTab === 'arena',
  })

  useEffect(() => {
    if (arenaMode === 'season' && !season && seasons.data && seasons.data.length > 0) {
      setSeason(String(seasons.data[0].season))
    }
  }, [arenaMode, season, seasons.data])

  const levelQuery = useQuery({
    queryKey: ['public', 'rank', 'level', baseClass, page],
    queryFn: () =>
      unwrap(
        api.GET('/api/public/rankings/level', {
          params: { query: { baseClass: baseClass ? Number(baseClass) : undefined, page, pageSize } },
        }),
      ),
    enabled: activeTab === 'level',
    placeholderData: keepPreviousData,
  })

  const nobilityQuery = useQuery({
    queryKey: ['public', 'rank', 'nobility', page],
    queryFn: () => unwrap(api.GET('/api/public/rankings/nobility', { params: { query: { page, pageSize } } })),
    enabled: activeTab === 'nobility',
    placeholderData: keepPreviousData,
  })

  const guildsQuery = useQuery({
    queryKey: ['public', 'rank', 'guilds', guildSort, page],
    queryFn: () =>
      unwrap(api.GET('/api/public/rankings/guilds', { params: { query: { sort: guildSort, page, pageSize } } })),
    enabled: activeTab === 'guilds',
    placeholderData: keepPreviousData,
  })

  const arenaQuery = useQuery({
    queryKey: ['public', 'rank', 'arena', arenaMode, page],
    queryFn: () =>
      unwrap(api.GET('/api/public/rankings/arena', { params: { query: { kind: arenaMode, page, pageSize } } })),
    enabled: activeTab === 'arena' && arenaMode !== 'season',
    placeholderData: keepPreviousData,
  })

  const arenaSeasonQuery = useQuery({
    queryKey: ['public', 'rank', 'arena', 'season', season, page],
    queryFn: () =>
      unwrap(
        api.GET('/api/public/rankings/arena/seasons/{season}', {
          params: { path: { season: Number(season) }, query: { page, pageSize } },
        }),
      ),
    enabled: activeTab === 'arena' && arenaMode === 'season' && !!season,
    placeholderData: keepPreviousData,
  })

  const flowersQuery = useQuery({
    queryKey: ['public', 'rank', 'flowers', flowerKind, page],
    queryFn: () =>
      unwrap(api.GET('/api/public/rankings/flowers', { params: { query: { kind: flowerKind, page, pageSize } } })),
    enabled: activeTab === 'flowers',
    placeholderData: keepPreviousData,
  })

  const pkQuery = useQuery({
    queryKey: ['public', 'rank', 'pk', page],
    queryFn: () => unwrap(api.GET('/api/public/rankings/pk', { params: { query: { page, pageSize } } })),
    enabled: activeTab === 'pk',
    placeholderData: keepPreviousData,
  })

  const familiesQuery = useQuery({
    queryKey: ['public', 'rank', 'families', page],
    queryFn: () => unwrap(api.GET('/api/public/rankings/families', { params: { query: { page, pageSize } } })),
    enabled: activeTab === 'families',
    placeholderData: keepPreviousData,
  })

  const currentQuery =
    activeTab === 'level'
      ? levelQuery
      : activeTab === 'nobility'
        ? nobilityQuery
        : activeTab === 'guilds'
          ? guildsQuery
          : activeTab === 'arena'
            ? arenaMode === 'season'
              ? arenaSeasonQuery
              : arenaQuery
            : activeTab === 'flowers'
              ? flowersQuery
              : activeTab === 'pk'
                ? pkQuery
                : familiesQuery

  const total = currentQuery.data?.total ?? 0

  return (
    <Stack py="md">
      <Group justify="space-between">
        <div>
          <Title order={2}>{t('rankings.title')}</Title>
          <Text c="dimmed" size="sm">
            {t('rankings.subtitle')}
          </Text>
        </div>
        {currentQuery.isFetching && <Loader size="sm" />}
      </Group>

      <Tabs
        value={activeTab}
        onChange={(value) => {
          if (value) void navigate(`/rankings/${value}`)
        }}
      >
        <Tabs.List>
          {validTabs.map((tab) => (
            <Tabs.Tab key={tab} value={tab}>
              {t(`rankings.tabs.${tab}`)}
            </Tabs.Tab>
          ))}
        </Tabs.List>
      </Tabs>

      {activeTab === 'level' && (
        <Group>
          <Select
            label={t('rankings.filters.class')}
            placeholder={t('rankings.filters.allClasses')}
            clearable
            value={baseClass}
            onChange={setBaseClass}
            data={baseClassOptions.map((c) => ({ value: String(c), label: t(`classes.${classKey(c)}`) }))}
            w={220}
          />
        </Group>
      )}

      {activeTab === 'guilds' && (
        <Group>
          <SegmentedControl
            value={guildSort}
            onChange={setGuildSort}
            data={[
              { value: 'fund', label: t('rankings.filters.sortFund') },
              { value: 'members', label: t('rankings.filters.sortMembers') },
            ]}
          />
        </Group>
      )}

      {activeTab === 'arena' && (
        <Group align="flex-end">
          <SegmentedControl
            value={arenaMode}
            onChange={setArenaMode}
            data={[
              { value: 'points', label: t('rankings.filters.arenaPoints') },
              { value: 'honor', label: t('rankings.filters.arenaHonor') },
              { value: 'season', label: t('rankings.filters.arenaSeason') },
            ]}
          />
          {arenaMode === 'season' && (
            <Select
              label={t('rankings.filters.season')}
              value={season}
              onChange={setSeason}
              data={(seasons.data ?? []).map((s) => ({ value: String(s.season), label: `#${s.season}` }))}
              w={140}
            />
          )}
        </Group>
      )}

      {activeTab === 'flowers' && (
        <Group>
          <SegmentedControl
            value={flowerKind}
            onChange={setFlowerKind}
            data={[
              { value: 'redRose', label: t('rankings.filters.redRose') },
              { value: 'lily', label: t('rankings.filters.lily') },
              { value: 'orchid', label: t('rankings.filters.orchid') },
              { value: 'tulip', label: t('rankings.filters.tulip') },
              { value: 'total', label: t('rankings.filters.totalFlowers') },
            ]}
          />
        </Group>
      )}

      <ErrorAlert error={currentQuery.error} />

      <Paper withBorder>
        <ScrollArea>
          {activeTab === 'level' && (
            <Table striped highlightOnHover miw={640}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('rankings.columns.rank')}</Table.Th>
                  <Table.Th>{t('rankings.columns.player')}</Table.Th>
                  <Table.Th>{t('rankings.columns.class')}</Table.Th>
                  <Table.Th>{t('rankings.columns.level')}</Table.Th>
                  <Table.Th>{t('rankings.columns.rebirths')}</Table.Th>
                  <Table.Th>{t('rankings.columns.guild')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {levelQuery.data?.items.map((r) => (
                  <Table.Tr key={r.characterId}>
                    <Table.Td>{r.rank}</Table.Td>
                    <Table.Td>
                      <Group gap="xs" wrap="nowrap">
                        <PlayerAvatar avatar={r.avatar} name={r.name} size="sm" />
                        <Anchor component={Link} to={`/characters/${r.characterId}`} fw={500}>
                          {r.name}
                        </Anchor>
                      </Group>
                    </Table.Td>
                    <Table.Td>{t(`classes.${classKey(r.class)}`)}</Table.Td>
                    <Table.Td>{r.level}</Table.Td>
                    <Table.Td>{r.rebirths}</Table.Td>
                    <Table.Td>
                      {r.guildId && r.guildName ? (
                        <Anchor component={Link} to={`/guilds/${r.guildId}`}>
                          {r.guildName}
                        </Anchor>
                      ) : (
                        '—'
                      )}
                    </Table.Td>
                  </Table.Tr>
                ))}
                {levelQuery.data && levelQuery.data.items.length === 0 && <EmptyRow cols={6} text={t('rankings.empty')} />}
              </Table.Tbody>
            </Table>
          )}

          {activeTab === 'nobility' && (
            <Table striped highlightOnHover miw={600}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('rankings.columns.rank')}</Table.Th>
                  <Table.Th>{t('rankings.columns.player')}</Table.Th>
                  <Table.Th>{t('rankings.columns.nobilityRank')}</Table.Th>
                  <Table.Th>{t('rankings.columns.level')}</Table.Th>
                  <Table.Th ta="right">{t('rankings.columns.donation')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {nobilityQuery.data?.items.map((r) => (
                  <Table.Tr key={r.characterId}>
                    <Table.Td>{r.rank}</Table.Td>
                    <Table.Td>
                      <Group gap="xs" wrap="nowrap">
                        <PlayerAvatar avatar={r.avatar} name={r.name} size="sm" />
                        <Anchor component={Link} to={`/characters/${r.characterId}`} fw={500}>
                          {r.name}
                        </Anchor>
                      </Group>
                    </Table.Td>
                    <Table.Td>
                      <Badge color={nobilityColors[r.nobilityRank] ?? 'gray'} variant="light">
                        {t(`nobilityRanks.${r.nobilityRank}`)}
                      </Badge>
                    </Table.Td>
                    <Table.Td>{r.level}</Table.Td>
                    <Table.Td ta="right">{formatNumber(r.donation, i18n.language)}</Table.Td>
                  </Table.Tr>
                ))}
                {nobilityQuery.data && nobilityQuery.data.items.length === 0 && (
                  <EmptyRow cols={5} text={t('rankings.empty')} />
                )}
              </Table.Tbody>
            </Table>
          )}

          {activeTab === 'guilds' && (
            <Table striped highlightOnHover miw={700}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('rankings.columns.rank')}</Table.Th>
                  <Table.Th>{t('rankings.columns.guild')}</Table.Th>
                  <Table.Th>{t('rankings.columns.level')}</Table.Th>
                  <Table.Th>{t('rankings.columns.leader')}</Table.Th>
                  <Table.Th ta="right">{t('rankings.columns.members')}</Table.Th>
                  <Table.Th ta="right">{t('rankings.columns.fund')}</Table.Th>
                  <Table.Th ta="right">{t('rankings.columns.cpFund')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {guildsQuery.data?.items.map((g) => (
                  <Table.Tr key={g.guildId}>
                    <Table.Td>{g.rank}</Table.Td>
                    <Table.Td>
                      <Group gap="xs">
                        <Anchor component={Link} to={`/guilds/${g.guildId}`} fw={500}>
                          {g.name}
                        </Anchor>
                        {g.isGuildWarHolder && (
                          <Badge size="xs" color="yellow" variant="light">
                            {t('rankings.badges.holder')}
                          </Badge>
                        )}
                        {g.isGuildWarWinner && !g.isGuildWarHolder && (
                          <Badge size="xs" color="orange" variant="light">
                            {t('rankings.badges.winner')}
                          </Badge>
                        )}
                      </Group>
                    </Table.Td>
                    <Table.Td>{g.level}</Table.Td>
                    <Table.Td>
                      {g.leaderName ? (
                        <Anchor component={Link} to={`/characters/${g.leaderId}`}>
                          {g.leaderName}
                        </Anchor>
                      ) : (
                        '—'
                      )}
                    </Table.Td>
                    <Table.Td ta="right">{g.memberCount}</Table.Td>
                    <Table.Td ta="right">{formatNumber(g.fund, i18n.language)}</Table.Td>
                    <Table.Td ta="right">{formatNumber(g.conquerPointsFund, i18n.language)}</Table.Td>
                  </Table.Tr>
                ))}
                {guildsQuery.data && guildsQuery.data.items.length === 0 && (
                  <EmptyRow cols={7} text={t('rankings.empty')} />
                )}
              </Table.Tbody>
            </Table>
          )}

          {activeTab === 'arena' && arenaMode !== 'season' && (
            <Table striped highlightOnHover miw={640}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('rankings.columns.rank')}</Table.Th>
                  <Table.Th>{t('rankings.columns.player')}</Table.Th>
                  <Table.Th>{t('rankings.columns.class')}</Table.Th>
                  <Table.Th>{t('rankings.columns.level')}</Table.Th>
                  <Table.Th ta="right">{t('rankings.columns.points')}</Table.Th>
                  <Table.Th ta="right">{t('rankings.columns.honor')}</Table.Th>
                  <Table.Th ta="right">{t('rankings.columns.record')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {arenaQuery.data?.items.map((a) => (
                  <Table.Tr key={a.characterId}>
                    <Table.Td>{a.rank}</Table.Td>
                    <Table.Td>
                      <Group gap="xs" wrap="nowrap">
                        <PlayerAvatar avatar={a.avatar} name={a.name} size="sm" />
                        <Anchor component={Link} to={`/characters/${a.characterId}`} fw={500}>
                          {a.name}
                        </Anchor>
                      </Group>
                    </Table.Td>
                    <Table.Td>{t(`classes.${classKey(a.class)}`)}</Table.Td>
                    <Table.Td>{a.level}</Table.Td>
                    <Table.Td ta="right">{formatNumber(a.points, i18n.language)}</Table.Td>
                    <Table.Td ta="right">{formatNumber(a.honorTotal, i18n.language)}</Table.Td>
                    <Table.Td ta="right">
                      {a.winsTotal} / {a.lossesTotal}
                    </Table.Td>
                  </Table.Tr>
                ))}
                {arenaQuery.data && arenaQuery.data.items.length === 0 && <EmptyRow cols={7} text={t('rankings.empty')} />}
              </Table.Tbody>
            </Table>
          )}

          {activeTab === 'arena' && arenaMode === 'season' && (
            <Table striped highlightOnHover miw={600}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('rankings.columns.rank')}</Table.Th>
                  <Table.Th>{t('rankings.columns.player')}</Table.Th>
                  <Table.Th>{t('rankings.columns.class')}</Table.Th>
                  <Table.Th>{t('rankings.columns.level')}</Table.Th>
                  <Table.Th ta="right">{t('rankings.columns.points')}</Table.Th>
                  <Table.Th ta="right">{t('rankings.columns.record')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {arenaSeasonQuery.data?.items.map((a) => (
                  <Table.Tr key={`${a.rank}-${a.characterId}`}>
                    <Table.Td>{a.rank}</Table.Td>
                    <Table.Td>
                      <Anchor component={Link} to={`/characters/${a.characterId}`} fw={500}>
                        {a.name}
                      </Anchor>
                    </Table.Td>
                    <Table.Td>{t(`classes.${classKey(a.class)}`)}</Table.Td>
                    <Table.Td>{a.level}</Table.Td>
                    <Table.Td ta="right">{formatNumber(a.points, i18n.language)}</Table.Td>
                    <Table.Td ta="right">
                      {a.wins} / {a.losses}
                    </Table.Td>
                  </Table.Tr>
                ))}
                {(!arenaSeasonQuery.data || arenaSeasonQuery.data.items.length === 0) && (
                  <EmptyRow cols={6} text={t('rankings.empty')} />
                )}
              </Table.Tbody>
            </Table>
          )}

          {activeTab === 'flowers' && (
            <Table striped highlightOnHover miw={600}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('rankings.columns.rank')}</Table.Th>
                  <Table.Th>{t('rankings.columns.player')}</Table.Th>
                  <Table.Th>{t('rankings.columns.class')}</Table.Th>
                  <Table.Th>{t('rankings.columns.level')}</Table.Th>
                  <Table.Th ta="right">{t('rankings.columns.amount')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {flowersQuery.data?.items.map((f) => (
                  <Table.Tr key={f.characterId}>
                    <Table.Td>{f.rank}</Table.Td>
                    <Table.Td>
                      <Group gap="xs" wrap="nowrap">
                        <PlayerAvatar avatar={f.avatar} name={f.name} size="sm" />
                        <Anchor component={Link} to={`/characters/${f.characterId}`} fw={500}>
                          {f.name}
                        </Anchor>
                      </Group>
                    </Table.Td>
                    <Table.Td>{t(`classes.${classKey(f.class)}`)}</Table.Td>
                    <Table.Td>{f.level}</Table.Td>
                    <Table.Td ta="right">{formatNumber(f.amount, i18n.language)}</Table.Td>
                  </Table.Tr>
                ))}
                {flowersQuery.data && flowersQuery.data.items.length === 0 && (
                  <EmptyRow cols={5} text={t('rankings.empty')} />
                )}
              </Table.Tbody>
            </Table>
          )}

          {activeTab === 'pk' && (
            <Table striped highlightOnHover miw={600}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('rankings.columns.rank')}</Table.Th>
                  <Table.Th>{t('rankings.columns.player')}</Table.Th>
                  <Table.Th>{t('rankings.columns.class')}</Table.Th>
                  <Table.Th>{t('rankings.columns.level')}</Table.Th>
                  <Table.Th>{t('rankings.columns.guild')}</Table.Th>
                  <Table.Th ta="right">{t('rankings.columns.pkPoints')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {pkQuery.data?.items.map((p) => (
                  <Table.Tr key={p.characterId}>
                    <Table.Td>{p.rank}</Table.Td>
                    <Table.Td>
                      <Group gap="xs" wrap="nowrap">
                        <PlayerAvatar avatar={p.avatar} name={p.name} size="sm" />
                        <Anchor component={Link} to={`/characters/${p.characterId}`} fw={500}>
                          {p.name}
                        </Anchor>
                      </Group>
                    </Table.Td>
                    <Table.Td>{t(`classes.${classKey(p.class)}`)}</Table.Td>
                    <Table.Td>{p.level}</Table.Td>
                    <Table.Td>
                      {p.guildId && p.guildName ? (
                        <Anchor component={Link} to={`/guilds/${p.guildId}`}>
                          {p.guildName}
                        </Anchor>
                      ) : (
                        '—'
                      )}
                    </Table.Td>
                    <Table.Td ta="right">
                      <Badge color="red" variant="light">
                        {p.pkPoints}
                      </Badge>
                    </Table.Td>
                  </Table.Tr>
                ))}
                {pkQuery.data && pkQuery.data.items.length === 0 && <EmptyRow cols={6} text={t('rankings.empty')} />}
              </Table.Tbody>
            </Table>
          )}

          {activeTab === 'families' && (
            <Table striped highlightOnHover miw={640}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t('rankings.columns.rank')}</Table.Th>
                  <Table.Th>{t('rankings.columns.family')}</Table.Th>
                  <Table.Th>{t('rankings.columns.level')}</Table.Th>
                  <Table.Th>{t('rankings.columns.leader')}</Table.Th>
                  <Table.Th ta="right">{t('rankings.columns.members')}</Table.Th>
                  <Table.Th ta="right">{t('rankings.columns.occupiedMaps')}</Table.Th>
                  <Table.Th ta="right">{t('rankings.columns.fund')}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {familiesQuery.data?.items.map((f) => (
                  <Table.Tr key={f.familyId}>
                    <Table.Td>{f.rank}</Table.Td>
                    <Table.Td fw={500}>{f.name}</Table.Td>
                    <Table.Td>{f.level}</Table.Td>
                    <Table.Td>
                      {f.leaderName ? (
                        <Anchor component={Link} to={`/characters/${f.leaderId}`}>
                          {f.leaderName}
                        </Anchor>
                      ) : (
                        '—'
                      )}
                    </Table.Td>
                    <Table.Td ta="right">{f.memberCount}</Table.Td>
                    <Table.Td ta="right">{f.occupiedMaps}</Table.Td>
                    <Table.Td ta="right">{formatNumber(f.fund, i18n.language)}</Table.Td>
                  </Table.Tr>
                ))}
                {familiesQuery.data && familiesQuery.data.items.length === 0 && (
                  <EmptyRow cols={7} text={t('rankings.empty')} />
                )}
              </Table.Tbody>
            </Table>
          )}
        </ScrollArea>
      </Paper>

      <Group justify="space-between">
        <Text size="sm" c="dimmed">
          {t('rankings.total', { count: total })}
        </Text>
        <Pagination total={Math.max(1, Math.ceil(total / pageSize))} value={page} onChange={setPage} />
      </Group>
    </Stack>
  )
}

function EmptyRow({ cols, text }: { cols: number; text: string }) {
  return (
    <Table.Tr>
      <Table.Td colSpan={cols}>
        <Text c="dimmed" ta="center" py="md">
          {text}
        </Text>
      </Table.Td>
    </Table.Tr>
  )
}
