import {
  ActionIcon,
  Badge,
  Box,
  Button,
  Code,
  Divider,
  Group,
  Loader,
  NumberInput,
  Paper,
  ScrollArea,
  Stack,
  Switch,
  Tabs,
  Text,
  TextInput,
  Title,
  Tooltip,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import {
  IconArrowLeft,
  IconArrowRight,
  IconCheck,
  IconEdit,
  IconFocusCentered,
  IconLink,
  IconMapPin,
} from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useCallback, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useParams, useSearchParams } from 'react-router'
import { api, unwrap } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { useCan } from '../../app/auth'
import { AssetImage } from '../../components/pickers'
import {
  defaultOverlays,
  MapViewer,
  type MapOverlaysState,
  type MapViewPosition,
  type SelectedEntity,
} from './MapViewer'

function parseNum(value: string | null): number | undefined {
  if (value == null || value.trim() === '') return undefined
  const n = Number(value)
  return Number.isFinite(n) ? n : undefined
}

export default function MapViewerPage() {
  const { t } = useTranslation(['maps', 'mapEdit'])
  const canEdit = useCan('Studio')
  const { id } = useParams()
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const mapId = Number(id)

  const [overlays, setOverlays] = useState<MapOverlaysState>(defaultOverlays)
  const [selected, setSelected] = useState<SelectedEntity | null>(null)
  const [entitySearch, setEntitySearch] = useState('')
  const [gotoX, setGotoX] = useState<number | string>('')
  const [gotoY, setGotoY] = useState<number | string>('')
  const [focusCell, setFocusCell] = useState<{ x: number; y: number; seq: number } | null>(null)
  const [fitSeq, setFitSeq] = useState(0)

  const initialView = useMemo(
    () => ({
      x: parseNum(searchParams.get('x')),
      y: parseNum(searchParams.get('y')),
      z: parseNum(searchParams.get('z')),
    }),
    // Read initial view once per mapId navigation
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [mapId],
  )

  const detail = useQuery({
    queryKey: ['maps', 'detail', mapId],
    queryFn: () => unwrap(api.GET('/api/maps/{id}', { params: { path: { id: mapId } } })),
    enabled: Number.isFinite(mapId) && mapId >= 0,
  })

  const gridQuery = useQuery({
    queryKey: ['maps', 'grid', mapId],
    queryFn: () => unwrap(api.GET('/api/maps/{id}/grid', { params: { path: { id: mapId } } })),
    enabled: Boolean(detail.data?.hasGrid),
  })

  const handleViewChange = useCallback(
    (view: MapViewPosition) => {
      setSearchParams(
        (prev) => {
          const next = new URLSearchParams(prev)
          next.set('x', String(view.x))
          next.set('y', String(view.y))
          next.set('z', String(view.z))
          return next
        },
        { replace: true },
      )
    },
    [setSearchParams],
  )

  const jumpTo = useCallback(
    (targetMapId: number, x?: number, y?: number) => {
      if (targetMapId === mapId && x != null && y != null) {
        setFocusCell((prev) => ({ x, y, seq: (prev?.seq ?? 0) + 1 }))
        return
      }
      setSelected(null)
      const params = new URLSearchParams()
      if (x != null) params.set('x', String(x))
      if (y != null) params.set('y', String(y))
      const qs = params.toString()
      navigate(`/admin/maps/${targetMapId}${qs ? `?${qs}` : ''}`)
    },
    [mapId, navigate],
  )

  const handleCopyLink = () => {
    void navigator.clipboard?.writeText(window.location.href)
    notifications.show({
      message: t('viewer.linkCopied'),
      icon: <IconCheck size={16} />,
      color: 'teal',
    })
  }

  const handleGoto = () => {
    const x = Number(gotoX)
    const y = Number(gotoY)
    if (Number.isFinite(x) && Number.isFinite(y)) {
      setFocusCell((prev) => ({ x, y, seq: (prev?.seq ?? 0) + 1 }))
    }
  }

  const toggle = (key: keyof MapOverlaysState) =>
    setOverlays((prev) => ({ ...prev, [key]: !prev[key] }))

  const map = detail.data
  const term = entitySearch.trim().toLowerCase()

  const filteredNpcs = useMemo(() => {
    if (!map) return []
    return map.npcs.filter(
      (n) =>
        !n.isGoal &&
        (!term ||
          String(n.id).includes(term) ||
          (n.name?.toLowerCase().includes(term) ?? false) ||
          n.kind.includes(term)),
    )
  }, [map, term])

  const filteredSpawns = useMemo(() => {
    if (!map) return []
    return map.spawns.filter(
      (s) =>
        !term ||
        String(s.monsterId).includes(term) ||
        (s.monsterName?.toLowerCase().includes(term) ?? false),
    )
  }, [map, term])

  const filteredPortals = useMemo(() => {
    if (!map) return []
    return map.portals.filter(
      (p) =>
        !term ||
        String(p.index).includes(term) ||
        (p.toMap != null && String(p.toMap).includes(term)) ||
        (p.toMapName?.toLowerCase().includes(term) ?? false),
    )
  }, [map, term])

  const goalCount = useMemo(() => map?.npcs.filter((n) => n.isGoal).length ?? 0, [map])

  if (detail.isLoading) {
    return (
      <Group justify="center" py="xl">
        <Loader />
      </Group>
    )
  }

  if (detail.error) {
    return (
      <Stack>
        <Button variant="subtle" leftSection={<IconArrowLeft size={16} />} onClick={() => navigate('/admin/maps')} w="fit-content">
          {t('viewer.backToBrowser')}
        </Button>
        <ErrorAlert error={detail.error} />
      </Stack>
    )
  }

  if (!map) {
    return (
      <Stack>
        <Button variant="subtle" leftSection={<IconArrowLeft size={16} />} onClick={() => navigate('/admin/maps')} w="fit-content">
          {t('viewer.backToBrowser')}
        </Button>
        <Text c="dimmed">{t('viewer.notFound')}</Text>
      </Stack>
    )
  }

  const title = map.name ?? map.serverName ?? `#${map.id}`

  return (
    <Stack gap="sm" h="calc(100vh - 96px)" style={{ minHeight: 620 }}>
      {/* Top bar */}
      <Group justify="space-between" wrap="wrap" gap="xs">
        <Group gap="xs" wrap="wrap">
          <Button
            size="xs"
            variant="subtle"
            leftSection={<IconArrowLeft size={14} />}
            onClick={() => navigate('/admin/maps')}
          >
            {t('viewer.backToBrowser')}
          </Button>
          <Title order={3}>{title}</Title>
          <Code>#{map.id}</Code>
          {map.serverName && map.name && <Code>{map.serverName}</Code>}
          {map.aliases.map((a) => (
            <Badge key={a} size="xs" variant="light">
              {a}
            </Badge>
          ))}
          {map.flags.map((f) => (
            <Badge key={f} size="xs" variant="outline" color="gray">
              {f}
            </Badge>
          ))}
          {map.ridePlus > 0 && (
            <Badge size="xs" variant="light" color="grape">
              {t('viewer.ridePlus', { plus: map.ridePlus })}
            </Badge>
          )}
        </Group>

        <Group gap="xs" wrap="wrap">
          {map.portal && (
            <Button
              size="xs"
              variant="light"
              color="teal"
              leftSection={<IconMapPin size={14} />}
              onClick={() => jumpTo(map.id, map.portal!.x, map.portal!.y)}
            >
              {t('viewer.startPoint', { x: map.portal.x, y: map.portal.y })}
            </Button>
          )}
          {map.rebornMap != null && (
            <Button
              size="xs"
              variant="light"
              color="cyan"
              rightSection={<IconArrowRight size={14} />}
              onClick={() => jumpTo(map.rebornMap!)}
            >
              {t('viewer.rebornLink', { target: map.rebornMapName ?? `#${map.rebornMap}` })}
            </Button>
          )}

          <Group gap={4}>
            <NumberInput
              size="xs"
              w={72}
              hideControls
              placeholder={t('viewer.gotoX')}
              value={gotoX}
              onChange={setGotoX}
              onKeyDown={(e) => e.key === 'Enter' && handleGoto()}
            />
            <NumberInput
              size="xs"
              w={72}
              hideControls
              placeholder={t('viewer.gotoY')}
              value={gotoY}
              onChange={setGotoY}
              onKeyDown={(e) => e.key === 'Enter' && handleGoto()}
            />
            <Button size="xs" variant="default" onClick={handleGoto}>
              {t('viewer.goto')}
            </Button>
          </Group>

          <Tooltip label={t('viewer.fitMap')}>
            <ActionIcon variant="default" size="md" onClick={() => setFitSeq((n) => n + 1)}>
              <IconFocusCentered size={16} />
            </ActionIcon>
          </Tooltip>
          <Button size="xs" variant="default" leftSection={<IconLink size={14} />} onClick={handleCopyLink}>
            {t('viewer.copyLink')}
          </Button>
          {canEdit && (
            <Button
              size="xs"
              variant="filled"
              leftSection={<IconEdit size={14} />}
              onClick={() => navigate(`/admin/maps/${map.id}/edit`)}
            >
              {t('mapEdit:openEditor')}
            </Button>
          )}
        </Group>
      </Group>

      {/* Map canvas + side panel */}
      <Box style={{ display: 'flex', gap: 12, flex: 1, minHeight: 0 }}>
        <Box style={{ flex: 1, minWidth: 0, height: '100%' }}>
          <MapViewer
            key={map.id}
            map={map}
            grid={gridQuery.data ?? null}
            overlays={overlays}
            initialView={initialView}
            focusCell={focusCell}
            fitSeq={fitSeq}
            onViewChange={handleViewChange}
            onNavigateMap={jumpTo}
            onSelectEntity={setSelected}
          />
        </Box>

        <Paper withBorder w={330} p="sm" style={{ display: 'flex', flexDirection: 'column', minHeight: 0 }}>
          <Tabs defaultValue="overlays" style={{ display: 'flex', flexDirection: 'column', flex: 1, minHeight: 0 }}>
            <Tabs.List mb="xs">
              <Tabs.Tab value="overlays">{t('viewer.tabs.overlays')}</Tabs.Tab>
              <Tabs.Tab value="entities">{t('viewer.tabs.entities')}</Tabs.Tab>
            </Tabs.List>

            <Tabs.Panel value="overlays" style={{ flex: 1, minHeight: 0, overflow: 'auto' }}>
              <Stack gap="xs">
                {map.hasTiles && (
                  <Switch
                    label={t('viewer.overlays.covers')}
                    checked={overlays.covers}
                    onChange={() => toggle('covers')}
                  />
                )}
                <Switch
                  label={t('viewer.overlays.walkability')}
                  checked={overlays.walkability}
                  disabled={!map.hasGrid}
                  onChange={() => toggle('walkability')}
                />
                <Switch
                  label={t('viewer.overlays.elevation')}
                  checked={overlays.elevation}
                  disabled={!map.hasGrid}
                  onChange={() => toggle('elevation')}
                />
                {!map.hasGrid && (
                  <Text size="xs" c="dimmed">
                    {t('viewer.overlays.gridUnavailable')}
                  </Text>
                )}
                {map.hasGrid && gridQuery.isLoading && (
                  <Group gap="xs">
                    <Loader size="xs" />
                    <Text size="xs" c="dimmed">
                      {t('viewer.overlays.loadingGrid')}
                    </Text>
                  </Group>
                )}
                {overlays.elevation && gridQuery.data && (
                  <Text size="xs" c="dimmed">
                    {t('viewer.overlays.elevationRange', {
                      min: gridQuery.data.minElevation,
                      max: gridQuery.data.maxElevation,
                    })}
                  </Text>
                )}

                <Divider my={4} />

                <Switch
                  label={`${t('viewer.overlays.portals')} (${map.portals.length})`}
                  checked={overlays.portals}
                  onChange={() => toggle('portals')}
                />
                <Switch
                  label={t('viewer.overlays.portalDestinations')}
                  checked={overlays.portalDestinations}
                  onChange={() => toggle('portalDestinations')}
                />
                <Switch
                  label={`${t('viewer.overlays.npcs')} (${map.npcs.length - goalCount})`}
                  checked={overlays.npcs}
                  onChange={() => toggle('npcs')}
                />
                {goalCount > 0 && (
                  <Switch
                    label={`${t('viewer.overlays.goals')} (${goalCount})`}
                    checked={overlays.goals}
                    onChange={() => toggle('goals')}
                  />
                )}
                <Switch
                  label={`${t('viewer.overlays.spawns')} (${map.spawns.length})`}
                  checked={overlays.spawns}
                  onChange={() => toggle('spawns')}
                />
                <Switch
                  label={t('viewer.overlays.startAndReborn')}
                  checked={overlays.startAndReborn}
                  onChange={() => toggle('startAndReborn')}
                />
                <Switch
                  label={`${t('viewer.overlays.teleporters')} (${map.teleporters.length})`}
                  checked={overlays.teleporters}
                  onChange={() => toggle('teleporters')}
                />
                {map.guildWar && (
                  <Switch
                    label={t('viewer.overlays.guildWar')}
                    checked={overlays.guildWar}
                    onChange={() => toggle('guildWar')}
                  />
                )}
              </Stack>
            </Tabs.Panel>

            <Tabs.Panel value="entities" style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
              <TextInput
                size="xs"
                mb="xs"
                placeholder={t('viewer.entities.search')}
                value={entitySearch}
                onChange={(e) => setEntitySearch(e.currentTarget.value)}
              />
              <ScrollArea style={{ flex: 1 }}>
                <Stack gap="sm">
                  {map.guildWar && (
                    <Stack gap={4}>
                      <Text size="xs" fw={700} c="dimmed" tt="uppercase">
                        {t('viewer.entities.guildWar')}
                      </Text>
                      {[map.guildWar.pole, map.guildWar.leftGate, map.guildWar.rightGate]
                        .filter((o): o is NonNullable<typeof o> => Boolean(o))
                        .map((o) => (
                          <Button
                            key={o.npcId}
                            size="xs"
                            variant="subtle"
                            justify="space-between"
                            onClick={() => {
                              setSelected({ type: 'guild_object', data: o })
                              jumpTo(map.id, o.x, o.y)
                            }}
                          >
                            <span>{o.name}</span>
                            <Code fz="xs">
                              {o.x},{o.y}
                            </Code>
                          </Button>
                        ))}
                    </Stack>
                  )}

                  {map.teleporters.length > 0 && (
                    <Stack gap={4}>
                      <Text size="xs" fw={700} c="dimmed" tt="uppercase">
                        {t('viewer.entities.teleporters', { count: map.teleporters.length })}
                      </Text>
                      {map.teleporters.map((tp) => (
                        <Button
                          key={tp.npcId}
                          size="xs"
                          variant="subtle"
                          color="grape"
                          justify="space-between"
                          onClick={() => {
                            setSelected({ type: 'teleporter', data: tp })
                            jumpTo(map.id, tp.x, tp.y)
                          }}
                        >
                          <span>{tp.npcName ?? `#${tp.npcId}`}</span>
                          <Code fz="xs">
                            {tp.x},{tp.y}
                          </Code>
                        </Button>
                      ))}
                    </Stack>
                  )}

                  <Stack gap={4}>
                    <Text size="xs" fw={700} c="dimmed" tt="uppercase">
                      {t('viewer.entities.portals', { count: filteredPortals.length })}
                    </Text>
                    {filteredPortals.map((p) => (
                      <Button
                        key={p.index}
                        size="xs"
                        variant="subtle"
                        color="cyan"
                        justify="space-between"
                        onClick={() => {
                          setSelected({ type: 'portal', data: p })
                          if (p.x != null && p.y != null) jumpTo(map.id, p.x, p.y)
                        }}
                      >
                        <span>
                          #{p.index} → {p.toMapName ?? (p.toMap != null ? `#${p.toMap}` : '?')}
                        </span>
                        {p.x != null && p.y != null && (
                          <Code fz="xs">
                            {p.x},{p.y}
                          </Code>
                        )}
                      </Button>
                    ))}
                    {filteredPortals.length === 0 && (
                      <Text size="xs" c="dimmed">
                        {t('viewer.entities.empty')}
                      </Text>
                    )}
                  </Stack>

                  <Stack gap={4}>
                    <Text size="xs" fw={700} c="dimmed" tt="uppercase">
                      {t('viewer.entities.npcs', { count: filteredNpcs.length })}
                    </Text>
                    {filteredNpcs.slice(0, 100).map((n) => (
                      <Button
                        key={n.id}
                        size="xs"
                        variant="subtle"
                        justify="space-between"
                        onClick={() => {
                          setSelected({ type: 'npc', data: n })
                          jumpTo(map.id, n.x, n.y)
                        }}
                      >
                        <span>{n.name ? `${n.name} (#${n.id})` : `#${n.id}`}</span>
                        <Code fz="xs">
                          {n.x},{n.y}
                        </Code>
                      </Button>
                    ))}
                    {filteredNpcs.length === 0 && (
                      <Text size="xs" c="dimmed">
                        {t('viewer.entities.empty')}
                      </Text>
                    )}
                  </Stack>

                  <Stack gap={4}>
                    <Text size="xs" fw={700} c="dimmed" tt="uppercase">
                      {t('viewer.entities.spawns', { count: filteredSpawns.length })}
                    </Text>
                    {filteredSpawns.slice(0, 100).map((s) => (
                      <Button
                        key={s.id}
                        size="xs"
                        variant="subtle"
                        color={s.monsterBoss ? 'red' : 'orange'}
                        justify="space-between"
                        onClick={() => {
                          setSelected({ type: 'spawn', data: s })
                          jumpTo(map.id, s.x, s.y)
                        }}
                      >
                        <span>
                          {s.monsterName ?? `#${s.monsterId}`} ×{s.amount}
                        </span>
                        <Code fz="xs">
                          {s.x},{s.y}
                        </Code>
                      </Button>
                    ))}
                    {filteredSpawns.length === 0 && (
                      <Text size="xs" c="dimmed">
                        {t('viewer.entities.empty')}
                      </Text>
                    )}
                  </Stack>
                </Stack>
              </ScrollArea>
            </Tabs.Panel>
          </Tabs>

          {selected && (
            <>
              <Divider my="xs" />
              <Stack gap={4}>
                <Text size="xs" fw={700} c="dimmed" tt="uppercase">
                  {t('viewer.inspector.title')}
                </Text>

                {selected.type === 'npc' && (
                  <Stack gap={4}>
                    <Group gap="xs">
                      {selected.data.facePath && (
                        <AssetImage path={selected.data.facePath} alt={selected.data.name ?? ''} size={36} fallback={<IconMapPin size={18} />} />
                      )}
                      <div>
                        <Text size="sm" fw={600}>
                          {selected.data.name ?? `#${selected.data.id}`}
                        </Text>
                        <Text size="xs" c="dimmed">
                          {t('viewer.inspector.id')}: {selected.data.id} • {t('viewer.inspector.role')}: {selected.data.role}
                        </Text>
                      </div>
                    </Group>
                    <Text size="xs">
                      {t('viewer.inspector.position')}: <Code>({selected.data.x}, {selected.data.y})</Code>
                    </Text>
                    <Text size="xs">
                      {t('viewer.inspector.lookface')}: <Code>{selected.data.lookface}</Code> ({t('viewer.inspector.direction')}: {selected.data.direction})
                    </Text>
                  </Stack>
                )}

                {selected.type === 'spawn' && (
                  <Stack gap={4}>
                    <Group gap="xs">
                      <Text size="sm" fw={600}>
                        {selected.data.monsterName ?? `#${selected.data.monsterId}`}
                      </Text>
                      {selected.data.monsterBoss && (
                        <Badge size="xs" color="red">
                          {t('viewer.inspector.boss')}
                        </Badge>
                      )}
                    </Group>
                    <Text size="xs">
                      {t('viewer.inspector.position')}: <Code>({selected.data.x}, {selected.data.y})</Code> • {t('viewer.inspector.spread')}: {selected.data.spread}
                    </Text>
                    <Text size="xs">
                      {t('viewer.inspector.amount')}: {selected.data.amount} • {t('viewer.inspector.respawn')}: {t('viewer.inspector.seconds', { count: selected.data.respawn })}
                    </Text>
                    {selected.data.monsterLevel != null && (
                      <Text size="xs">
                        {t('viewer.inspector.level')}: {selected.data.monsterLevel}
                      </Text>
                    )}
                  </Stack>
                )}

                {selected.type === 'portal' && (
                  <Stack gap={4}>
                    <Text size="sm" fw={600}>
                      {t('viewer.inspector.portalIndex', { index: selected.data.index })}
                    </Text>
                    {selected.data.x != null && selected.data.y != null && (
                      <Text size="xs">
                        {t('viewer.inspector.position')}: <Code>({selected.data.x}, {selected.data.y})</Code>
                      </Text>
                    )}
                    {selected.data.toMap != null && selected.data.toX != null && selected.data.toY != null ? (
                      <Button
                        size="xs"
                        variant="light"
                        color="cyan"
                        rightSection={<IconArrowRight size={14} />}
                        onClick={() => jumpTo(selected.data.toMap!, selected.data.toX!, selected.data.toY!)}
                      >
                        {t('viewer.inspector.jumpTo', {
                          target: `${selected.data.toMapName ?? `#${selected.data.toMap}`} (${selected.data.toX}, ${selected.data.toY})`,
                        })}
                      </Button>
                    ) : (
                      <Text size="xs" c="dimmed">
                        {t('viewer.inspector.unlinked')}
                      </Text>
                    )}
                  </Stack>
                )}

                {selected.type === 'teleporter' && (
                  <Stack gap={4}>
                    <Group gap="xs">
                      {selected.data.facePath && (
                        <AssetImage path={selected.data.facePath} alt={selected.data.npcName ?? ''} size={36} fallback={<IconMapPin size={18} />} />
                      )}
                      <div>
                        <Text size="sm" fw={600}>
                          {selected.data.npcName ?? `#${selected.data.npcId}`}
                        </Text>
                        <Text size="xs" c="dimmed">
                          {t('viewer.inspector.fare')}: {t('viewer.inspector.silver', { amount: selected.data.fare })}
                        </Text>
                      </div>
                    </Group>
                    <Stack gap={2} mah={140} style={{ overflow: 'auto' }}>
                      {selected.data.destinations.map((d, i) => (
                        <Button
                          key={`${d.mapId}-${d.x}-${d.y}-${i}`}
                          size="xs"
                          variant="light"
                          color="grape"
                          justify="space-between"
                          rightSection={<IconArrowRight size={12} />}
                          onClick={() => jumpTo(d.mapId, d.x, d.y)}
                        >
                          <span>{d.name}</span>
                        </Button>
                      ))}
                    </Stack>
                  </Stack>
                )}

                {selected.type === 'guild_object' && (
                  <Stack gap={4}>
                    <Text size="sm" fw={600}>
                      {selected.data.name}
                    </Text>
                    <Text size="xs">
                      {t('viewer.inspector.position')}: <Code>({selected.data.x}, {selected.data.y})</Code>
                    </Text>
                    <Text size="xs">
                      {t('viewer.inspector.life')}: {selected.data.maxLife.toLocaleString()}
                    </Text>
                    {selected.data.insidePoint && (
                      <Text size="xs">
                        {t('viewer.inspector.insidePoint')}: <Code>({selected.data.insidePoint.x}, {selected.data.insidePoint.y})</Code>
                      </Text>
                    )}
                  </Stack>
                )}

                {selected.type === 'guild_conductor' && (
                  <Stack gap={4}>
                    <Text size="sm" fw={600}>
                      {t('viewer.inspector.conductor')}: {selected.data.city}
                    </Text>
                    <Button
                      size="xs"
                      variant="light"
                      color="grape"
                      rightSection={<IconArrowRight size={14} />}
                      onClick={() => jumpTo(selected.data.toMap, selected.data.toX, selected.data.toY)}
                    >
                      {t('viewer.inspector.jumpTo', {
                        target: `${selected.data.toMapName ?? `#${selected.data.toMap}`} (${selected.data.toX}, ${selected.data.toY})`,
                      })}
                    </Button>
                  </Stack>
                )}
              </Stack>
            </>
          )}
        </Paper>
      </Box>
    </Stack>
  )
}
