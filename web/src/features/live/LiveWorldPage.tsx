import {
  ActionIcon,
  Alert,
  Badge,
  Box,
  Button,
  Code,
  Divider,
  Group,
  Paper,
  Progress,
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
  IconCrosshair,
  IconExternalLink,
  IconFocusCentered,
  IconInfoCircle,
  IconMap2,
  IconPlayerStop,
  IconRadar2,
  IconUser,
} from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router'
import { api, unwrap } from '../../api/client'
import { professionKey } from '../../components/items'
import { MapPicker } from '../../components/pickers'
import { LiveMapCanvas } from './LiveMapCanvas'
import {
  defaultLiveLayers,
  hpPercent,
  pkNameStyle,
  summarizeActiveMaps,
  type LiveLayersState,
  type SelectedLiveEntity,
} from './liveState'
import { useLiveWorld } from './useLiveWorld'

const DEFAULT_MAP_ID = 1002 // Twin City

export default function LiveWorldPage() {
  const { t } = useTranslation(['live', 'items'])
  const { mapId: paramMapId } = useParams()
  const navigate = useNavigate()

  const parsedMapId = paramMapId != null && /^\d+$/.test(paramMapId) ? Number(paramMapId) : DEFAULT_MAP_ID
  const [layers, setLayers] = useState<LiveLayersState>(defaultLiveLayers)
  const [selected, setSelected] = useState<SelectedLiveEntity | null>(null)
  const [followedId, setFollowedId] = useState<number | null>(null)
  const [followedName, setFollowedName] = useState<string>('')
  const [onlineSearch, setOnlineSearch] = useState('')
  const [thisMapOnly, setThisMapOnly] = useState(false)
  const [mapSearch, setMapSearch] = useState('')
  const [focusCell, setFocusCell] = useState<{ x: number; y: number; seq: number } | null>(null)
  const [fitSeq, setFitSeq] = useState(0)

  const { status, gameOnline, onlinePlayers, mapState } = useLiveWorld(parsedMapId)

  const mapDetail = useQuery({
    queryKey: ['maps', 'detail', parsedMapId],
    queryFn: () => unwrap(api.GET('/api/maps/{id}', { params: { path: { id: parsedMapId } } })),
    enabled: Number.isFinite(parsedMapId),
    retry: false,
  })

  const gridQuery = useQuery({
    queryKey: ['maps', 'grid', parsedMapId],
    queryFn: () => unwrap(api.GET('/api/maps/{id}/grid', { params: { path: { id: parsedMapId } } })),
    enabled: Boolean(mapDetail.data?.hasGrid && layers.walkability),
  })

  const switchMap = useCallback(
    (nextMapId: number, x?: number, y?: number) => {
      if (nextMapId !== parsedMapId) {
        setSelected(null)
        navigate(`/admin/live/${nextMapId}`)
      }
      if (x != null && y != null) {
        setFocusCell((prev) => ({ x, y, seq: (prev?.seq ?? 0) + 1 }))
      }
    },
    [navigate, parsedMapId],
  )

  const startFollowing = useCallback(
    (characterId: number, name: string, targetMapId: number, x?: number, y?: number) => {
      setFollowedId(characterId)
      setFollowedName(name)
      setSelected({ kind: 'player', id: characterId })
      switchMap(targetMapId, x, y)
    },
    [switchMap],
  )

  const stopFollowing = useCallback(() => {
    setFollowedId(null)
    setFollowedName('')
  }, [])

  // Follow player across map changes via OnlinePlayers stream
  const lastFollowedPosRef = useRef<{ mapId: number; x: number; y: number } | null>(null)
  useEffect(() => {
    if (followedId == null) {
      lastFollowedPosRef.current = null
      return
    }
    const online = onlinePlayers.find((p) => p.characterId === followedId)
    if (!online) {
      if (gameOnline && lastFollowedPosRef.current != null) {
        notifications.show({
          message: t('live:banner.followedOffline', { name: followedName || `#${followedId}` }),
          color: 'yellow',
        })
        setFollowedId(null)
        setFollowedName('')
      }
      return
    }

    if (online.name && online.name !== followedName) {
      setFollowedName(online.name)
    }

    if (online.mapId !== parsedMapId) {
      lastFollowedPosRef.current = { mapId: online.mapId, x: online.x, y: online.y }
      navigate(`/admin/live/${online.mapId}`)
      setFocusCell((prev) => ({ x: online.x, y: online.y, seq: (prev?.seq ?? 0) + 1 }))
    }
  }, [followedId, followedName, onlinePlayers, parsedMapId, gameOnline, navigate, t])

  // Keep camera centered on followed player as they walk on the current map
  const followedOnMap = followedId != null ? mapState.players[followedId] : undefined
  useEffect(() => {
    if (!followedOnMap) return
    const prev = lastFollowedPosRef.current
    if (!prev || prev.mapId !== mapState.mapId || prev.x !== followedOnMap.x || prev.y !== followedOnMap.y) {
      lastFollowedPosRef.current = { mapId: mapState.mapId, x: followedOnMap.x, y: followedOnMap.y }
      setFocusCell((cur) => ({ x: followedOnMap.x, y: followedOnMap.y, seq: (cur?.seq ?? 0) + 1 }))
    }
  }, [followedOnMap, mapState.mapId])

  const playersList = useMemo(
    () => Object.values(mapState.players).sort((a, b) => a.name.localeCompare(b.name)),
    [mapState.players],
  )
  const monstersList = useMemo(() => Object.values(mapState.monsters), [mapState.monsters])
  const aliveMonstersCount = useMemo(() => monstersList.filter((m) => m.alive).length, [monstersList])
  const groundItemsList = useMemo(() => Object.values(mapState.groundItems), [mapState.groundItems])
  const boothsList = useMemo(() => Object.values(mapState.booths), [mapState.booths])
  const activeMaps = useMemo(() => summarizeActiveMaps(onlinePlayers), [onlinePlayers])

  const filteredOnline = useMemo(() => {
    const term = onlineSearch.trim().toLowerCase()
    return onlinePlayers.filter((p) => {
      if (thisMapOnly && p.mapId !== parsedMapId) return false
      if (!term) return true
      return (
        p.name.toLowerCase().includes(term) ||
        String(p.characterId).includes(term) ||
        String(p.mapId).includes(term) ||
        (p.mapName?.toLowerCase().includes(term) ?? false)
      )
    })
  }, [onlinePlayers, onlineSearch, thisMapOnly, parsedMapId])

  const mapTerm = mapSearch.trim().toLowerCase()
  const filteredMapPlayers = useMemo(
    () => playersList.filter((p) => !mapTerm || p.name.toLowerCase().includes(mapTerm) || String(p.characterID).includes(mapTerm)),
    [playersList, mapTerm],
  )
  const filteredMapMonsters = useMemo(
    () =>
      monstersList.filter(
        (m) =>
          (m.alive || layers.deadMonsters) &&
          (!mapTerm || m.name.toLowerCase().includes(mapTerm) || String(m.typeID).includes(mapTerm)),
      ),
    [monstersList, layers.deadMonsters, mapTerm],
  )
  const filteredMapItems = useMemo(
    () =>
      groundItemsList.filter(
        (i) =>
          !mapTerm ||
          (i.itemName?.toLowerCase().includes(mapTerm) ?? false) ||
          String(i.typeId).includes(mapTerm) ||
          (i.silver > 0 && 'silver'.includes(mapTerm)),
      ),
    [groundItemsList, mapTerm],
  )
  const filteredMapBooths = useMemo(
    () =>
      boothsList.filter(
        (b) =>
          !mapTerm ||
          b.ownerName.toLowerCase().includes(mapTerm) ||
          (b.hawk?.toLowerCase().includes(mapTerm) ?? false),
      ),
    [boothsList, mapTerm],
  )

  const toggleLayer = (key: keyof LiveLayersState) =>
    setLayers((prev) => ({ ...prev, [key]: !prev[key] }))

  const formatClass = (cls: number) => {
    const key = professionKey(cls)
    return key ? t(`items:profession.${key}`) : String(cls)
  }

  const statusColor =
    status === 'connected' ? 'green' : status === 'connecting' || status === 'reconnecting' ? 'yellow' : 'red'

  const selectedPlayer = selected?.kind === 'player' ? mapState.players[selected.id] : undefined
  const selectedMonster = selected?.kind === 'monster' ? mapState.monsters[selected.id] : undefined
  const selectedItem = selected?.kind === 'item' ? mapState.groundItems[selected.id] : undefined
  const selectedBooth = selected?.kind === 'booth' ? mapState.booths[selected.id] : undefined

  return (
    <Stack gap="sm" h="calc(100vh - 96px)" style={{ minHeight: 620 }}>
      {/* Top toolbar */}
      <Group justify="space-between" wrap="wrap" gap="xs">
        <Group gap="xs" wrap="wrap">
          <IconRadar2 size={20} />
          <Title order={3}>{t('live:title')}</Title>
          <Badge size="sm" color={statusColor} variant="dot">
            {t(`live:status.${status}`)}
          </Badge>
          <Badge size="sm" color={gameOnline ? 'teal' : 'red'} variant="light">
            {gameOnline ? t('live:status.gameOnline') : t('live:status.gameOffline')}
          </Badge>

          <Box w={240}>
            <MapPicker
              value={parsedMapId}
              onChange={(id) => {
                if (id != null) switchMap(id)
              }}
              placeholder={t('live:toolbar.mapPlaceholder')}
              clearable={false}
            />
          </Box>
        </Group>

        <Group gap="xs" wrap="wrap">
          {followedId != null && (
            <Badge
              size="lg"
              color="cyan"
              variant="filled"
              rightSection={
                <ActionIcon size="xs" variant="transparent" color="white" onClick={stopFollowing}>
                  <IconPlayerStop size={14} />
                </ActionIcon>
              }
            >
              {t('live:banner.following', { name: followedName || `#${followedId}` })}
            </Badge>
          )}

          <Badge size="sm" variant="light" color="blue">
            {t('live:toolbar.playersCount', { count: playersList.length })}
          </Badge>
          <Badge size="sm" variant="light" color="orange">
            {t('live:toolbar.monstersCount', { count: aliveMonstersCount })}
          </Badge>
          <Badge size="sm" variant="light" color="teal">
            {t('live:toolbar.itemsCount', { count: groundItemsList.length })}
          </Badge>
          <Badge size="sm" variant="light" color="grape">
            {t('live:toolbar.boothsCount', { count: boothsList.length })}
          </Badge>

          <Tooltip label={t('live:toolbar.fitMap')}>
            <ActionIcon variant="default" size="md" onClick={() => setFitSeq((n) => n + 1)}>
              <IconFocusCentered size={16} />
            </ActionIcon>
          </Tooltip>
          <Button
            size="xs"
            variant="default"
            component={Link}
            to={`/admin/maps/${parsedMapId}`}
            leftSection={<IconMap2 size={14} />}
          >
            {t('live:toolbar.openInMaps')}
          </Button>
        </Group>
      </Group>

      {/* Active maps quick bar */}
      {activeMaps.length > 0 && (
        <Group gap={6} wrap="wrap">
          <Text size="xs" c="dimmed" fw={600}>
            {t('live:toolbar.activeMaps')}:
          </Text>
          {activeMaps.map((m) => (
            <Button
              key={m.mapId}
              size="compact-xs"
              variant={m.mapId === parsedMapId ? 'filled' : 'light'}
              onClick={() => switchMap(m.mapId)}
            >
              {m.mapName ?? `#${m.mapId}`} ({m.count})
            </Button>
          ))}
        </Group>
      )}

      {!gameOnline && status === 'connected' && (
        <Alert color="yellow" icon={<IconInfoCircle size={16} />} py="xs">
          {t('live:banner.gameOffline')}
        </Alert>
      )}

      {gameOnline && mapState.takenAt && !mapState.exists && (
        <Alert color="gray" icon={<IconInfoCircle size={16} />} py="xs">
          {t('live:banner.mapNotLoaded')}
        </Alert>
      )}

      {/* Main split: Live map canvas + side panel */}
      <Box style={{ display: 'flex', gap: 12, flex: 1, minHeight: 0 }}>
        <Box style={{ flex: 1, minWidth: 0, height: '100%' }}>
          <LiveMapCanvas
            map={mapDetail.data ?? null}
            mapId={parsedMapId}
            grid={gridQuery.data ?? null}
            live={mapState}
            layers={layers}
            followedCharacterId={followedId}
            selected={selected}
            focusCell={focusCell}
            fitSeq={fitSeq}
            onSelect={setSelected}
          />
        </Box>

        <Paper withBorder w={350} p="sm" style={{ display: 'flex', flexDirection: 'column', minHeight: 0 }}>
          <Tabs defaultValue="online" style={{ display: 'flex', flexDirection: 'column', flex: 1, minHeight: 0 }}>
            <Tabs.List mb="xs">
              <Tabs.Tab value="online">{t('live:tabs.online', { count: onlinePlayers.length })}</Tabs.Tab>
              <Tabs.Tab value="map">{t('live:tabs.map')}</Tabs.Tab>
              <Tabs.Tab value="layers">{t('live:tabs.layers')}</Tabs.Tab>
            </Tabs.List>

            {/* Tab 1: Online players */}
            <Tabs.Panel value="online" style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
              <Stack gap="xs" mb="xs">
                <TextInput
                  size="xs"
                  placeholder={t('live:online.search')}
                  value={onlineSearch}
                  onChange={(e) => setOnlineSearch(e.currentTarget.value)}
                />
                <Switch
                  size="xs"
                  label={t('live:online.thisMapOnly')}
                  checked={thisMapOnly}
                  onChange={(e) => setThisMapOnly(e.currentTarget.checked)}
                />
              </Stack>

              <ScrollArea style={{ flex: 1 }}>
                {onlinePlayers.length === 0 ? (
                  <Text size="xs" c="dimmed">
                    {t('live:online.empty')}
                  </Text>
                ) : filteredOnline.length === 0 ? (
                  <Text size="xs" c="dimmed">
                    {t('live:online.noMatches')}
                  </Text>
                ) : (
                  <Stack gap={6}>
                    {filteredOnline.map((p) => {
                      const isFollowing = followedId === p.characterId
                      return (
                        <Paper
                          key={p.characterId}
                          withBorder
                          p="xs"
                          radius="sm"
                          style={{
                            cursor: 'pointer',
                            borderColor: isFollowing ? 'var(--mantine-color-cyan-5)' : undefined,
                          }}
                          onClick={() => {
                            setSelected({ kind: 'player', id: p.characterId })
                            switchMap(p.mapId, p.x, p.y)
                          }}
                        >
                          <Group justify="space-between" wrap="nowrap" gap={6}>
                            <div style={{ minWidth: 0 }}>
                              <Group gap={6} wrap="nowrap">
                                <Text size="sm" fw={600} truncate>
                                  {p.name}
                                </Text>
                                <Badge size="xs" variant="light">
                                  Lv.{p.level}
                                </Badge>
                                {p.pkPoints > 0 && (
                                  <Badge size="xs" color={p.pkPoints >= 100 ? 'dark' : p.pkPoints >= 30 ? 'red' : 'gray'}>
                                    {t('live:online.pk', { points: p.pkPoints })}
                                  </Badge>
                                )}
                              </Group>
                              <Text size="xs" c="dimmed" truncate>
                                {formatClass(p.class)} • {p.mapName ?? `#${p.mapId}`} ({p.x}, {p.y})
                              </Text>
                            </div>

                            <Group gap={4} wrap="nowrap" onClick={(e) => e.stopPropagation()}>
                              <Tooltip label={isFollowing ? t('live:online.following') : t('live:online.follow')}>
                                <ActionIcon
                                  size="sm"
                                  variant={isFollowing ? 'filled' : 'subtle'}
                                  color="cyan"
                                  onClick={() =>
                                    isFollowing
                                      ? stopFollowing()
                                      : startFollowing(p.characterId, p.name, p.mapId, p.x, p.y)
                                  }
                                >
                                  <IconCrosshair size={14} />
                                </ActionIcon>
                              </Tooltip>
                              <Tooltip label={t('live:online.openCharacter')}>
                                <ActionIcon
                                  size="sm"
                                  variant="subtle"
                                  component={Link}
                                  to={`/admin/characters/${p.characterId}`}
                                >
                                  <IconExternalLink size={14} />
                                </ActionIcon>
                              </Tooltip>
                            </Group>
                          </Group>
                        </Paper>
                      )
                    })}
                  </Stack>
                )}
              </ScrollArea>
            </Tabs.Panel>

            {/* Tab 2: Entities on this map */}
            <Tabs.Panel value="map" style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
              <TextInput
                size="xs"
                mb="xs"
                placeholder={t('live:map.search')}
                value={mapSearch}
                onChange={(e) => setMapSearch(e.currentTarget.value)}
              />
              <ScrollArea style={{ flex: 1 }}>
                <Stack gap="sm">
                  <Stack gap={4}>
                    <Text size="xs" fw={700} c="dimmed" tt="uppercase">
                      {t('live:map.players', { count: filteredMapPlayers.length })}
                    </Text>
                    {filteredMapPlayers.map((p) => (
                      <Button
                        key={p.characterID}
                        size="xs"
                        variant="subtle"
                        justify="space-between"
                        onClick={() => {
                          setSelected({ kind: 'player', id: p.characterID })
                          switchMap(parsedMapId, p.x, p.y)
                        }}
                      >
                        <span>
                          {p.name} (Lv.{p.level})
                        </span>
                        <Code fz="xs">
                          {p.x},{p.y}
                        </Code>
                      </Button>
                    ))}
                    {filteredMapPlayers.length === 0 && (
                      <Text size="xs" c="dimmed">
                        {t('live:map.empty')}
                      </Text>
                    )}
                  </Stack>

                  <Stack gap={4}>
                    <Text size="xs" fw={700} c="dimmed" tt="uppercase">
                      {t('live:map.booths', { count: filteredMapBooths.length })}
                    </Text>
                    {filteredMapBooths.map((b) => (
                      <Button
                        key={b.id}
                        size="xs"
                        variant="subtle"
                        color="grape"
                        justify="space-between"
                        onClick={() => {
                          setSelected({ kind: 'booth', id: b.id })
                          switchMap(parsedMapId, b.x, b.y)
                        }}
                      >
                        <span>
                          {b.ownerName} ({t('live:map.boothItems', { count: b.items })})
                        </span>
                        <Code fz="xs">
                          {b.x},{b.y}
                        </Code>
                      </Button>
                    ))}
                    {filteredMapBooths.length === 0 && (
                      <Text size="xs" c="dimmed">
                        {t('live:map.empty')}
                      </Text>
                    )}
                  </Stack>

                  <Stack gap={4}>
                    <Text size="xs" fw={700} c="dimmed" tt="uppercase">
                      {t('live:map.groundItems', { count: filteredMapItems.length })}
                    </Text>
                    {filteredMapItems.slice(0, 80).map((item) => (
                      <Button
                        key={item.id}
                        size="xs"
                        variant="subtle"
                        color={item.silver > 0 ? 'yellow' : 'teal'}
                        justify="space-between"
                        onClick={() => {
                          setSelected({ kind: 'item', id: item.id })
                          switchMap(parsedMapId, item.x, item.y)
                        }}
                      >
                        <span>
                          {item.silver > 0
                            ? t('live:map.silver', { amount: item.silver.toLocaleString() })
                            : item.itemName ?? t('live:map.itemFallback', { id: item.typeId })}
                        </span>
                        <Code fz="xs">
                          {item.x},{item.y}
                        </Code>
                      </Button>
                    ))}
                    {filteredMapItems.length === 0 && (
                      <Text size="xs" c="dimmed">
                        {t('live:map.empty')}
                      </Text>
                    )}
                  </Stack>

                  <Stack gap={4}>
                    <Text size="xs" fw={700} c="dimmed" tt="uppercase">
                      {t('live:map.monsters', { alive: aliveMonstersCount, total: monstersList.length })}
                    </Text>
                    {filteredMapMonsters.slice(0, 80).map((m) => (
                      <Button
                        key={m.id}
                        size="xs"
                        variant="subtle"
                        color={m.guard ? 'cyan' : 'orange'}
                        justify="space-between"
                        onClick={() => {
                          setSelected({ kind: 'monster', id: m.id })
                          switchMap(parsedMapId, m.x, m.y)
                        }}
                      >
                        <span>
                          {m.name} Lv.{m.level}
                          {m.guard ? ` [${t('live:map.guard')}]` : ''}
                          {!m.alive ? ` (${t('live:map.dead')})` : ''}
                        </span>
                        <Code fz="xs">
                          {m.x},{m.y}
                        </Code>
                      </Button>
                    ))}
                    {filteredMapMonsters.length === 0 && (
                      <Text size="xs" c="dimmed">
                        {t('live:map.empty')}
                      </Text>
                    )}
                  </Stack>
                </Stack>
              </ScrollArea>
            </Tabs.Panel>

            {/* Tab 3: Layers */}
            <Tabs.Panel value="layers" style={{ flex: 1, minHeight: 0, overflow: 'auto' }}>
              <Stack gap="xs">
                <Text size="xs" fw={700} c="dimmed" tt="uppercase">
                  {t('live:layers.liveHeading')}
                </Text>
                <Switch
                  label={`${t('live:layers.players')} (${playersList.length})`}
                  checked={layers.players}
                  onChange={() => toggleLayer('players')}
                />
                <Switch
                  label={`${t('live:layers.monsters')} (${aliveMonstersCount})`}
                  checked={layers.monsters}
                  onChange={() => toggleLayer('monsters')}
                />
                <Switch
                  label={`${t('live:layers.deadMonsters')} (${monstersList.length - aliveMonstersCount})`}
                  checked={layers.deadMonsters}
                  disabled={!layers.monsters}
                  onChange={() => toggleLayer('deadMonsters')}
                />
                <Switch
                  label={`${t('live:layers.groundItems')} (${groundItemsList.length})`}
                  checked={layers.groundItems}
                  onChange={() => toggleLayer('groundItems')}
                />
                <Switch
                  label={`${t('live:layers.booths')} (${boothsList.length})`}
                  checked={layers.booths}
                  onChange={() => toggleLayer('booths')}
                />

                <Divider my={4} />

                <Text size="xs" fw={700} c="dimmed" tt="uppercase">
                  {t('live:layers.staticHeading')}
                </Text>
                {mapDetail.data?.hasTiles && (
                  <Switch
                    label={t('live:layers.covers')}
                    checked={layers.covers}
                    onChange={() => toggleLayer('covers')}
                  />
                )}
                <Switch
                  label={t('live:layers.walkability')}
                  checked={layers.walkability}
                  disabled={!mapDetail.data?.hasGrid}
                  onChange={() => toggleLayer('walkability')}
                />
                <Switch
                  label={t('live:layers.portals')}
                  checked={layers.portals}
                  onChange={() => toggleLayer('portals')}
                />
                <Switch
                  label={t('live:layers.npcs')}
                  checked={layers.npcs}
                  onChange={() => toggleLayer('npcs')}
                />
              </Stack>
            </Tabs.Panel>
          </Tabs>

          {/* Selected entity inspector */}
          {(selectedPlayer || selectedMonster || selectedItem || selectedBooth) && (
            <>
              <Divider my="xs" />
              <Stack gap={6}>
                <Text size="xs" fw={700} c="dimmed" tt="uppercase">
                  {t('live:inspector.title')}
                </Text>

                {selectedPlayer && (
                  <Stack gap={6}>
                    <Group justify="space-between" wrap="nowrap">
                      <Group gap={6}>
                        <Text size="sm" fw={700}>
                          {selectedPlayer.name}
                        </Text>
                        <Badge size="xs" color={pkNameStyle(selectedPlayer.nameColor).badgeColor}>
                          {t(`live:pkColor.${selectedPlayer.nameColor}`, { defaultValue: selectedPlayer.nameColor })}
                        </Badge>
                        {selectedPlayer.dead && (
                          <Badge size="xs" color="red">
                            {t('live:inspector.dead')}
                          </Badge>
                        )}
                      </Group>
                      <Text size="xs" c="dimmed">
                        {t('live:inspector.level', { level: selectedPlayer.level })}
                      </Text>
                    </Group>

                    <Text size="xs" c="dimmed">
                      {formatClass(selectedPlayer.class)} • {t('live:inspector.position')}:{' '}
                      <Code>
                        ({selectedPlayer.x}, {selectedPlayer.y})
                      </Code>{' '}
                      • {t('live:inspector.pk')}: {selectedPlayer.pkPoints}
                    </Text>

                    <div>
                      <Group justify="space-between" mb={2}>
                        <Text size="xs">{t('live:inspector.hp')}</Text>
                        <Text size="xs" ff="monospace">
                          {selectedPlayer.health} / {selectedPlayer.maxHealth}
                        </Text>
                      </Group>
                      <Progress
                        size="sm"
                        value={hpPercent(selectedPlayer.health, selectedPlayer.maxHealth)}
                        color="green"
                      />
                    </div>

                    <Group gap="xs">
                      <Button
                        size="xs"
                        variant={followedId === selectedPlayer.characterID ? 'filled' : 'light'}
                        color="cyan"
                        leftSection={
                          followedId === selectedPlayer.characterID ? (
                            <IconPlayerStop size={14} />
                          ) : (
                            <IconCrosshair size={14} />
                          )
                        }
                        onClick={() =>
                          followedId === selectedPlayer.characterID
                            ? stopFollowing()
                            : startFollowing(
                                selectedPlayer.characterID,
                                selectedPlayer.name,
                                parsedMapId,
                                selectedPlayer.x,
                                selectedPlayer.y,
                              )
                        }
                      >
                        {followedId === selectedPlayer.characterID
                          ? t('live:inspector.stopFollowing')
                          : t('live:inspector.follow')}
                      </Button>
                      <Button
                        size="xs"
                        variant="default"
                        component={Link}
                        to={`/admin/characters/${selectedPlayer.characterID}`}
                        leftSection={<IconUser size={14} />}
                      >
                        {t('live:inspector.openCharacter')}
                      </Button>
                    </Group>
                  </Stack>
                )}

                {selectedMonster && (
                  <Stack gap={6}>
                    <Group justify="space-between">
                      <Group gap={6}>
                        <Text size="sm" fw={700}>
                          {selectedMonster.name}
                        </Text>
                        {selectedMonster.guard && (
                          <Badge size="xs" color="cyan">
                            {t('live:inspector.guard')}
                          </Badge>
                        )}
                        <Badge size="xs" color={selectedMonster.alive ? 'green' : 'gray'}>
                          {selectedMonster.alive ? t('live:inspector.alive') : t('live:inspector.dead')}
                        </Badge>
                      </Group>
                      <Text size="xs" c="dimmed">
                        {t('live:inspector.level', { level: selectedMonster.level })}
                      </Text>
                    </Group>
                    <Text size="xs" c="dimmed">
                      {t('live:inspector.typeId')}: <Code>{selectedMonster.typeID}</Code> •{' '}
                      {t('live:inspector.position')}:{' '}
                      <Code>
                        ({selectedMonster.x}, {selectedMonster.y})
                      </Code>
                    </Text>
                    <div>
                      <Group justify="space-between" mb={2}>
                        <Text size="xs">{t('live:inspector.hp')}</Text>
                        <Text size="xs" ff="monospace">
                          {selectedMonster.health} / {selectedMonster.maxHealth}
                        </Text>
                      </Group>
                      <Progress
                        size="sm"
                        value={hpPercent(selectedMonster.health, selectedMonster.maxHealth)}
                        color={selectedMonster.guard ? 'cyan' : 'orange'}
                      />
                    </div>
                  </Stack>
                )}

                {selectedItem && (
                  <Stack gap={4}>
                    <Text size="sm" fw={700}>
                      {selectedItem.silver > 0
                        ? t('live:map.silver', { amount: selectedItem.silver.toLocaleString() })
                        : selectedItem.itemName ?? t('live:map.itemFallback', { id: selectedItem.typeId })}
                    </Text>
                    <Text size="xs" c="dimmed">
                      {t('live:inspector.typeId')}: <Code>{selectedItem.typeId}</Code> •{' '}
                      {t('live:inspector.position')}:{' '}
                      <Code>
                        ({selectedItem.x}, {selectedItem.y})
                      </Code>
                    </Text>
                    <Text size="xs" c="dimmed">
                      {(() => {
                        const sec = Math.max(0, Math.round((Date.parse(selectedItem.expiresAt) - Date.now()) / 1000))
                        return sec > 0 ? t('live:inspector.expiresIn', { seconds: sec }) : t('live:inspector.expired')
                      })()}
                    </Text>
                  </Stack>
                )}

                {selectedBooth && (
                  <Stack gap={6}>
                    <Text size="sm" fw={700}>
                      {selectedBooth.ownerName}
                    </Text>
                    <Text size="xs" c="dimmed">
                      {t('live:inspector.boothItems')}: {selectedBooth.items} • {t('live:inspector.position')}:{' '}
                      <Code>
                        ({selectedBooth.x}, {selectedBooth.y})
                      </Code>
                    </Text>
                    {selectedBooth.hawk && (
                      <Text size="xs">
                        {t('live:inspector.hawk')}: “{selectedBooth.hawk}”
                      </Text>
                    )}
                    <Button
                      size="xs"
                      variant="default"
                      w="fit-content"
                      component={Link}
                      to={`/admin/characters/${selectedBooth.ownerID}`}
                      leftSection={<IconUser size={14} />}
                    >
                      {t('live:inspector.openCharacter')}
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
