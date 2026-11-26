import {
  Alert,
  Anchor,
  Badge,
  Code,
  Group,
  Loader,
  ScrollArea,
  SegmentedControl,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
} from '@mantine/core'
import { IconInfoCircle, IconMapPin, IconSearch } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router'
import { api, unwrap } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { Panel } from '../../theme/Panel'
import { DialogPreviewBox } from './DialogPreviewBox'
import classes from './NpcDialogsPage.module.css'
import { kindBadgeColor, type DialogRange } from './npcDialogUtils'

export default function NpcDialogsPage() {
  const { npcId: routeNpcId } = useParams<{ npcId?: string }>()
  const navigate = useNavigate()
  const { t, i18n } = useTranslation('npcDialogs')

  const selectedNpcId = routeNpcId && /^\d+$/.test(routeNpcId) ? Number(routeNpcId) : null
  const [search, setSearch] = useState('')
  const [kindFilter, setKindFilter] = useState<string | null>(null)
  const [mapFilter, setMapFilter] = useState('')
  const [range, setRange] = useState<DialogRange>('7d')

  const parsedMapId = /^\d+$/.test(mapFilter.trim()) ? Number(mapFilter.trim()) : undefined

  const listQuery = useQuery({
    queryKey: ['npc-dialogs', 'list', range],
    queryFn: () => unwrap(api.GET('/api/npc-dialogs', { params: { query: { range } } })),
  })

  const detailQuery = useQuery({
    queryKey: ['npc-dialogs', 'detail', selectedNpcId, range],
    queryFn: () =>
      unwrap(
        api.GET('/api/npc-dialogs/{npcId}', {
          params: {
            path: { npcId: selectedNpcId! },
            query: { range },
          },
        }),
      ),
    enabled: selectedNpcId != null,
  })

  const filteredNpcs = useMemo(() => {
    const q = search.trim().toLowerCase()
    return (listQuery.data?.npcs ?? []).filter(
      (n) =>
        (parsedMapId == null || n.mapId === parsedMapId) &&
        (kindFilter == null || n.kind === kindFilter) &&
        (!q ||
          String(n.npcId).includes(q) ||
          (n.name && n.name.toLowerCase().includes(q)) ||
          (n.mapName && n.mapName.toLowerCase().includes(q))),
    )
  }, [listQuery.data, search, parsedMapId, kindFilter])

  const detail = detailQuery.data

  const formatTime = (iso: string) => {
    try {
      return new Intl.DateTimeFormat(i18n.resolvedLanguage ?? i18n.language, {
        dateStyle: 'short',
        timeStyle: 'short',
      }).format(new Date(iso))
    } catch {
      return iso
    }
  }

  return (
    <Stack gap="sm">
      <Panel
        title={t('title')}
        icon="icons/items/610120.png"
        aside={
          <SegmentedControl
            size="xs"
            value={range}
            onChange={(v) => setRange(v as DialogRange)}
            data={[
              { value: '24h', label: t('range.24h') },
              { value: '7d', label: t('range.7d') },
            ]}
          />
        }
      >
        <Group gap="xs" wrap="wrap">
          <TextInput
            size="xs"
            placeholder={t('filters.search')}
            value={search}
            onChange={(e) => setSearch(e.currentTarget.value)}
            leftSection={<IconSearch size={13} />}
            style={{ width: 220 }}
          />
          <TextInput
            size="xs"
            placeholder={t('filters.mapId')}
            value={mapFilter}
            onChange={(e) => setMapFilter(e.currentTarget.value)}
            style={{ width: 120 }}
          />
          <Select
            size="xs"
            clearable
            placeholder={t('filters.allKinds')}
            value={kindFilter}
            onChange={setKindFilter}
            data={[
              { value: 'script', label: t('kind.script') },
              { value: 'teleporter', label: t('kind.teleporter') },
              { value: 'talker', label: t('kind.talker') },
              { value: 'shop', label: t('kind.shop') },
              { value: 'warehouse', label: t('kind.warehouse') },
              { value: 'default', label: t('kind.default') },
            ]}
            style={{ width: 150 }}
          />
          {listQuery.data && (
            <Text size="xs" c="dimmed">
              {t('filters.count', { count: filteredNpcs.length })}
            </Text>
          )}
        </Group>
      </Panel>

      <div className={classes.layout}>
        <Panel title={t('list.title')} icon="icons/items/711394.png" p="xs">
          {listQuery.isLoading && (
            <Group gap="xs">
              <Loader size="xs" />
              <Text size="xs">{t('loading')}</Text>
            </Group>
          )}
          {listQuery.error && <ErrorAlert error={listQuery.error} />}
          {listQuery.data && (
            <ScrollArea h={560}>
              <Table striped highlightOnHover verticalSpacing={4}>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>{t('list.columns.npc')}</Table.Th>
                    <Table.Th style={{ width: 85 }}>{t('list.columns.kind')}</Table.Th>
                    <Table.Th style={{ width: 58 }}>{t('list.columns.talks')}</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {filteredNpcs.slice(0, 300).map((npc) => {
                    const selected = selectedNpcId === npc.npcId
                    return (
                      <Table.Tr
                        key={npc.npcId}
                        className={selected ? classes.selectedRow : undefined}
                        style={{ cursor: 'pointer' }}
                        onClick={() => void navigate(`/admin/npc-dialogs/${npc.npcId}`)}
                      >
                        <Table.Td>
                          <Stack gap={0}>
                            <Text size="xs" fw={700}>
                              {npc.name ?? `#${npc.npcId}`}
                            </Text>
                            <Text size="xs" c="dimmed">
                              #{npc.npcId} · {npc.mapName ?? t('mapFallback', { id: npc.mapId })}
                            </Text>
                          </Stack>
                        </Table.Td>
                        <Table.Td>
                          <Badge size="xs" color={kindBadgeColor(npc.kind)}>
                            {t(`kind.${npc.kind}` as never)}
                          </Badge>
                        </Table.Td>
                        <Table.Td>
                          <Text size="xs">{npc.talkCount != null ? npc.talkCount : '—'}</Text>
                        </Table.Td>
                      </Table.Tr>
                    )
                  })}
                </Table.Tbody>
              </Table>
            </ScrollArea>
          )}
        </Panel>

        <Stack gap="sm">
          {selectedNpcId == null ? (
            <Panel title={t('detail.emptyTitle')} icon="icons/items/200007.png">
              <Text size="xs" c="dimmed">
                {t('detail.emptyBody')}
              </Text>
            </Panel>
          ) : detailQuery.isLoading ? (
            <Panel title={t('detail.loadingTitle', { id: selectedNpcId })}>
              <Group gap="xs">
                <Loader size="xs" />
                <Text size="xs">{t('loading')}</Text>
              </Group>
            </Panel>
          ) : detailQuery.error ? (
            <ErrorAlert error={detailQuery.error} />
          ) : (
            detail && (
              <>
                <Panel
                  title={`${detail.npc.name ?? `#${detail.npc.npcId}`} (#${detail.npc.npcId})`}
                  icon="icons/items/200007.png"
                  aside={
                    <Badge size="xs" color={kindBadgeColor(detail.npc.kind)}>
                      {t(`kind.${detail.npc.kind}` as never)}
                    </Badge>
                  }
                >
                  <Stack gap="xs">
                    <Group gap="md" wrap="wrap">
                      <Group gap={4}>
                        <IconMapPin size={14} />
                        <Anchor
                          component={Link}
                          to={`/admin/maps/${detail.npc.mapId}?x=${detail.npc.x}&y=${detail.npc.y}`}
                          size="xs"
                          fw={700}
                        >
                          {detail.npc.mapName ?? t('mapFallback', { id: detail.npc.mapId })} ({detail.npc.x},{' '}
                          {detail.npc.y})
                        </Anchor>
                      </Group>
                      <Text size="xs" c="dimmed">
                        {t('detail.role')}: <Code fz="xs">{detail.role}</Code>
                      </Text>
                      <Text size="xs" c="dimmed">
                        {t('detail.lookface')}: <Code fz="xs">{detail.npc.lookface}</Code>
                      </Text>
                      {detail.handler && (
                        <Text size="xs" c="dimmed">
                          {t('detail.handler')}: <Code fz="xs">{detail.handler}</Code>
                        </Text>
                      )}
                      {detail.sourceFile && (
                        <Text size="xs" c="dimmed">
                          {t('detail.source')}: <Code fz="xs">{detail.sourceFile}</Code>
                        </Text>
                      )}
                    </Group>

                    {detail.npc.kind === 'script' && (
                      <Alert color="yellow" icon={<IconInfoCircle size={15} />} title={t('detail.partialTitle')}>
                        <Text size="xs">{t('detail.partialBody')}</Text>
                      </Alert>
                    )}
                    {detail.npc.kind === 'shop' && (
                      <Text size="xs">{t('detail.opensShop', { count: detail.shopItemCount ?? 0 })}</Text>
                    )}
                    {detail.npc.kind === 'warehouse' && <Text size="xs">{t('detail.opensWarehouse')}</Text>}
                    {detail.npc.kind === 'script' && detail.pages.length === 0 && (
                      <Text size="xs" c="dimmed" fs="italic">
                        {t('preview.noPages')}
                      </Text>
                    )}

                    <Stack gap="xs">
                      {detail.pages.map((page, idx) => (
                        <DialogPreviewBox key={idx} page={page} index={idx} fare={detail.fare} />
                      ))}
                    </Stack>
                  </Stack>
                </Panel>

                <Panel title={t('stats.title', { range })} icon="icons/items/724426.png">
                  {!detail.stats.lokiAvailable ? (
                    <Text size="xs" c="dimmed">
                      {t('stats.lokiUnavailable')}
                    </Text>
                  ) : (
                    <Stack gap="xs">
                      <Group gap="lg" wrap="wrap">
                        <Text size="xs">
                          <strong>{t('stats.totalTalks')}:</strong> {detail.stats.totalTalks}
                        </Text>
                        <Text size="xs">
                          <strong>{t('stats.uniquePlayers')}:</strong> {detail.stats.uniquePlayers}
                        </Text>
                        <Text size="xs">
                          <strong>{t('stats.lastTalk')}:</strong>{' '}
                          {detail.stats.lastTalkAt ? formatTime(detail.stats.lastTalkAt) : '—'}
                        </Text>
                      </Group>

                      {Object.keys(detail.stats.byOption).length > 0 && (
                        <Group gap="xs" wrap="wrap">
                          <Text size="xs" fw={700}>
                            {t('stats.byOption')}:
                          </Text>
                          {Object.entries(detail.stats.byOption)
                            .sort((a, b) => Number(a[0]) - Number(b[0]))
                            .map(([opt, count]) => (
                              <Badge key={opt} size="xs" variant="light">
                                #{opt}: {count}
                              </Badge>
                            ))}
                        </Group>
                      )}
                    </Stack>
                  )}
                </Panel>
              </>
            )
          )}
        </Stack>
      </div>
    </Stack>
  )
}
