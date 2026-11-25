import {
  ActionIcon,
  Alert,
  Badge,
  Box,
  Button,
  Code,
  Group,
  Loader,
  SegmentedControl,
  Stack,
  Switch,
  Text,
  Title,
  Tooltip,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import {
  IconAlertTriangle,
  IconArrowBackUp,
  IconArrowForwardUp,
  IconArrowLeft,
  IconCheck,
  IconDeviceFloppy,
  IconFocusCentered,
  IconRefresh,
} from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useParams } from 'react-router'
import { api, ApiError, unwrap, type Schemas } from '../../../api/client'
import { ErrorAlert } from '../../../app/ErrorAlert'
import { decodeBase64Bytes } from '../../../lib/iso'
import type { DecodedGrid } from '../gridCanvasLayer'
import { MapEditorCanvas, type EditorMode, type EditorSelection } from './MapEditorCanvas'
import { MapEditorSidebar } from './MapEditorSidebar'
import { TargetMapPickerModal } from './TargetMapPickerModal'
import {
  canRedo,
  canUndo,
  createHistory,
  findDuplicateIds,
  isDirty,
  markSaved,
  nextAvailableId,
  pushHistory,
  redoHistory,
  undoHistory,
  type EditableMapSettingsState,
  type EditableNpcState,
  type EditablePortalState,
  type EditableSpawnState,
  type EditorHistory,
  type MapEditorSnapshot,
} from './editorState'

function snapshotFromResponse(data: Schemas['MapEditResponse']): MapEditorSnapshot {
  return {
    settings: {
      inMapsJson: data.settings.inMapsJson,
      name: data.settings.name ?? null,
      aliases: [...data.settings.aliases],
      flags: [...data.settings.flags],
      portal: data.settings.portal ? { x: data.settings.portal.x, y: data.settings.portal.y } : null,
      rebornMap: data.settings.rebornMap ?? null,
      scrollTypeId: data.settings.scrollTypeId ?? null,
      ridePlus: data.settings.ridePlus,
    },
    npcs: data.npcs.map((n) => ({
      id: n.id,
      lookface: n.lookface,
      role: n.role,
      x: n.x,
      y: n.y,
      life: n.life,
      level: n.level,
      name: n.name ?? null,
    })),
    spawns: data.spawns.map((s) => ({
      id: s.id,
      x: s.x,
      y: s.y,
      spread: s.spread,
      amount: s.amount,
      respawn: s.respawn,
      monsterId: s.monsterId,
      monsterName: s.monsterName ?? null,
      monsterLevel: s.monsterLevel ?? null,
      monsterBoss: s.monsterBoss,
    })),
    portals: data.portals.map((p) => ({
      index: p.index,
      x: p.x ?? null,
      y: p.y ?? null,
      toMap: p.toMap ?? null,
      toMapName: p.toMapName ?? null,
      toX: p.toX ?? null,
      toY: p.toY ?? null,
    })),
  }
}

export default function MapEditorPage() {
  const { t } = useTranslation('mapEdit')
  const { id } = useParams()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const mapId = Number(id)

  const [history, setHistory] = useState<EditorHistory<MapEditorSnapshot> | null>(null)
  const [hashes, setHashes] = useState<Schemas['MapEditHashes'] | null>(null)
  const [problems, setProblems] = useState<Schemas['MapEditProblemDto'][]>([])
  const [changedFilesToReload, setChangedFilesToReload] = useState<string[]>([])
  const [conflict, setConflict] = useState(false)

  const [mode, setMode] = useState<EditorMode>('select')
  const [selected, setSelected] = useState<EditorSelection | null>(null)
  const [sidebarTab, setSidebarTab] = useState<string>('inspector')
  const [showWalkability, setShowWalkability] = useState(true)
  const [focusCell, setFocusCell] = useState<{ x: number; y: number; seq: number } | null>(null)
  const [fitSeq, setFitSeq] = useState(0)
  const [portalPickerFor, setPortalPickerFor] = useState<EditablePortalState | null>(null)

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

  const editQuery = useQuery({
    queryKey: ['maps', 'edit', mapId],
    queryFn: () => unwrap(api.GET('/api/maps/{id}/edit', { params: { path: { id: mapId } } })),
    enabled: Number.isFinite(mapId) && mapId >= 0,
  })

  useEffect(() => {
    if (!editQuery.data) return
    setHistory(createHistory(snapshotFromResponse(editQuery.data)))
    setHashes(editQuery.data.hashes)
    setProblems(editQuery.data.problems)
    setConflict(false)
    setSelected(null)
    setChangedFilesToReload([])
  }, [editQuery.data])

  const decodedGrid: DecodedGrid | null = useMemo(() => {
    const g = gridQuery.data
    if (!g) return null
    return {
      width: g.width,
      height: g.height,
      walkable: decodeBase64Bytes(g.walkableBase64),
      elevation: decodeBase64Bytes(g.elevationBase64),
      minElevation: g.minElevation,
      maxElevation: g.maxElevation,
    }
  }, [gridQuery.data])

  const commit = useCallback((updater: (prev: MapEditorSnapshot) => MapEditorSnapshot) => {
    setHistory((h) => (h ? pushHistory(h, updater(h.present)) : h))
  }, [])

  const handleUndo = useCallback(() => setHistory((h) => (h ? undoHistory(h) : h)), [])
  const handleRedo = useCallback(() => setHistory((h) => (h ? redoHistory(h) : h)), [])

  // Keyboard shortcuts: Cmd/Ctrl+Z and Cmd/Ctrl+Shift+Z / Ctrl+Y
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null
      if (target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.isContentEditable)) {
        return
      }
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'z') {
        e.preventDefault()
        if (e.shiftKey) handleRedo()
        else handleUndo()
      } else if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'y') {
        e.preventDefault()
        handleRedo()
      } else if (e.key === 'Escape') {
        setMode('select')
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [handleUndo, handleRedo])

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!history || !hashes) throw new Error('Editor not initialized')
      const snap = history.present
      return unwrap(
        api.PUT('/api/maps/{id}/edit', {
          params: { path: { id: mapId } },
          body: {
            hashes,
            settings: {
              inMapsJson: snap.settings.inMapsJson,
              name: snap.settings.name,
              aliases: snap.settings.aliases,
              flags: snap.settings.flags,
              portal: snap.settings.portal,
              rebornMap: snap.settings.rebornMap,
              scrollTypeId: snap.settings.scrollTypeId,
              ridePlus: snap.settings.ridePlus,
            },
            npcs: snap.npcs.map((n) => ({
              id: n.id,
              lookface: n.lookface,
              role: n.role,
              x: n.x,
              y: n.y,
              life: n.life,
              level: n.level,
              name: n.name ?? null,
            })),
            spawns: snap.spawns.map((s) => ({
              id: s.id,
              x: s.x,
              y: s.y,
              spread: s.spread,
              amount: s.amount,
              respawn: s.respawn,
              monsterId: s.monsterId,
              monsterName: s.monsterName ?? null,
              monsterLevel: s.monsterLevel ?? null,
              monsterBoss: s.monsterBoss ?? false,
            })),
            portals: snap.portals.map((p) => ({
              index: p.index,
              x: p.x,
              y: p.y,
              toMap: p.toMap,
              toMapName: p.toMapName,
              toX: p.toX,
              toY: p.toY,
            })),
          },
        }),
      )
    },
    onSuccess: (res) => {
      setHashes(res.hashes)
      setProblems(res.problems)
      setConflict(false)
      if (!res.saved) {
        notifications.show({ color: 'red', message: t('saveFailedValidation') })
        return
      }
      setHistory((h) => (h ? markSaved(h) : h))
      if (res.changedFiles.length > 0) {
        setChangedFilesToReload((prev) => Array.from(new Set([...prev, ...res.changedFiles])))
        notifications.show({ color: 'teal', icon: <IconCheck size={16} />, message: t('saveSuccess') })
        void queryClient.invalidateQueries({ queryKey: ['maps', 'detail', mapId] })
      } else {
        notifications.show({ color: 'gray', message: t('saveNoChanges') })
      }
    },
    onError: (err) => {
      if (err instanceof ApiError && err.status === 409) {
        setConflict(true)
        return
      }
      notifications.show({ color: 'red', message: err instanceof Error ? err.message : String(err) })
    },
  })

  const reloadMutation = useMutation({
    mutationFn: (files: string[]) =>
      unwrap(
        api.POST('/api/maps/{id}/edit/reload', {
          params: { path: { id: mapId } },
          body: { files },
        }),
      ),
    onSuccess: (res) => {
      const failed = res.results.filter((r) => !r.applied)
      if (failed.length > 0) {
        for (const f of failed) {
          notifications.show({
            color: 'red',
            message: t('reloadFailed', { file: f.file, errors: f.errors.join('; ') }),
          })
        }
      } else {
        const appliedFiles = res.results.map((r) => r.file).join(', ')
        notifications.show({
          color: 'teal',
          icon: <IconCheck size={16} />,
          message: t('reloadedSuccess', { files: appliedFiles }),
        })
        setChangedFilesToReload([])
      }
    },
    onError: (err) => {
      notifications.show({ color: 'red', message: err instanceof Error ? err.message : String(err) })
    },
  })

  const snapshot = history?.present ?? null
  const dirty = history ? isDirty(history) : false

  // Lightweight live client-side hints (server D1 validation runs on save)
  const localHints = useMemo(() => {
    if (!snapshot) return []
    const hints: string[] = []
    const dupNpcs = findDuplicateIds(snapshot.npcs.map((n) => n.id))
    if (dupNpcs.length > 0) hints.push(t('errors.duplicateNpcId', { ids: dupNpcs.join(', ') }))
    const dupSpawns = findDuplicateIds(snapshot.spawns.map((s) => s.id))
    if (dupSpawns.length > 0) hints.push(t('errors.duplicateSpawnId', { ids: dupSpawns.join(', ') }))
    if (snapshot.settings.flags.includes('RecordDisable') && snapshot.settings.rebornMap == null) {
      hints.push(t('errors.recordDisableNeedsReborn'))
    }
    return hints
  }, [snapshot, t])

  const handleRejectCell = useCallback(
    (reason: 'unwalkable' | 'outOfBounds', x: number, y: number) => {
      notifications.show({
        color: 'red',
        message: reason === 'unwalkable' ? t('errors.unwalkableCell', { x, y }) : t('errors.outOfBounds', { x, y }),
      })
    },
    [t],
  )

  const handleSelect = useCallback((sel: EditorSelection | null) => {
    setSelected(sel)
    if (sel) setSidebarTab('inspector')
  }, [])

  const handleMoveNpc = useCallback(
    (npcId: number, x: number, y: number) => {
      commit((prev) => ({
        ...prev,
        npcs: prev.npcs.map((n) => (n.id === npcId ? { ...n, x, y } : n)),
      }))
    },
    [commit],
  )

  const handlePlaceNpc = useCallback(
    (x: number, y: number) => {
      if (!editQuery.data || !snapshot) return
      const newId = nextAvailableId(
        snapshot.npcs.map((n) => n.id),
        editQuery.data.nextNpcId,
      )
      const npc: EditableNpcState = { id: newId, lookface: 20, role: 2, x, y, life: 0, level: 0, name: null }
      commit((prev) => ({ ...prev, npcs: [...prev.npcs, npc] }))
      setSelected({ type: 'npc', id: newId })
      setSidebarTab('inspector')
      setMode('select')
    },
    [commit, editQuery.data, snapshot],
  )

  const handleCreateSpawn = useCallback(
    (x: number, y: number, spread: number) => {
      if (!editQuery.data || !snapshot) return
      const newId = nextAvailableId(
        snapshot.spawns.map((s) => s.id),
        editQuery.data.nextSpawnId,
      )
      const spawn: EditableSpawnState = {
        id: newId,
        x,
        y,
        spread,
        amount: 10,
        respawn: 10,
        monsterId: snapshot.spawns[0]?.monsterId ?? 1,
        monsterName: snapshot.spawns[0]?.monsterName ?? null,
      }
      commit((prev) => ({ ...prev, spawns: [...prev.spawns, spawn] }))
      setSelected({ type: 'spawn', id: newId })
      setSidebarTab('inspector')
      setMode('select')
    },
    [commit, editQuery.data, snapshot],
  )

  const handleUpdateSpawnGeometry = useCallback(
    (spawnId: number, x: number, y: number, spread: number) => {
      commit((prev) => ({
        ...prev,
        spawns: prev.spawns.map((s) => (s.id === spawnId ? { ...s, x, y, spread } : s)),
      }))
    },
    [commit],
  )

  const handlePickStartPoint = useCallback(
    (x: number, y: number) => {
      commit((prev) => ({
        ...prev,
        settings: { ...prev.settings, portal: { x, y } },
      }))
      setSidebarTab('settings')
      setMode('select')
    },
    [commit],
  )

  const handleUpdateNpc = useCallback(
    (npcId: number, patch: Partial<EditableNpcState>) => {
      commit((prev) => ({
        ...prev,
        npcs: prev.npcs.map((n) => (n.id === npcId ? { ...n, ...patch } : n)),
      }))
      if (patch.id != null && patch.id !== npcId) {
        setSelected({ type: 'npc', id: patch.id })
      }
    },
    [commit],
  )

  const handleDeleteNpc = useCallback(
    (npcId: number) => {
      commit((prev) => ({ ...prev, npcs: prev.npcs.filter((n) => n.id !== npcId) }))
      setSelected(null)
    },
    [commit],
  )

  const handleUpdateSpawn = useCallback(
    (spawnId: number, patch: Partial<EditableSpawnState>) => {
      commit((prev) => ({
        ...prev,
        spawns: prev.spawns.map((s) => (s.id === spawnId ? { ...s, ...patch } : s)),
      }))
      if (patch.id != null && patch.id !== spawnId) {
        setSelected({ type: 'spawn', id: patch.id })
      }
    },
    [commit],
  )

  const handleDeleteSpawn = useCallback(
    (spawnId: number) => {
      commit((prev) => ({ ...prev, spawns: prev.spawns.filter((s) => s.id !== spawnId) }))
      setSelected(null)
    },
    [commit],
  )

  const handleUpdatePortal = useCallback(
    (index: number, patch: Partial<EditablePortalState>) => {
      commit((prev) => ({
        ...prev,
        portals: prev.portals.map((p) => (p.index === index ? { ...p, ...patch } : p)),
      }))
    },
    [commit],
  )

  const handleUpdateSettings = useCallback(
    (patch: Partial<EditableMapSettingsState>) => {
      commit((prev) => ({ ...prev, settings: { ...prev.settings, ...patch } }))
    },
    [commit],
  )

  if (detail.isLoading || editQuery.isLoading || !snapshot) {
    return (
      <Group justify="center" py="xl">
        <Loader />
      </Group>
    )
  }

  if (detail.error || editQuery.error) {
    return (
      <Stack>
        <Button variant="subtle" leftSection={<IconArrowLeft size={16} />} onClick={() => navigate(`/admin/maps/${mapId}`)} w="fit-content">
          {t('backToViewer')}
        </Button>
        <ErrorAlert error={detail.error ?? editQuery.error} />
      </Stack>
    )
  }

  const map = detail.data!
  const mapTitle = map.name ?? map.serverName ?? `#${map.id}`
  const errorProblems = problems.filter((p) => p.level === 'error')
  const warningProblems = problems.filter((p) => p.level === 'warning')

  return (
    <Stack gap="xs" h="calc(100vh - 96px)" style={{ minHeight: 620 }}>
      {/* Top toolbar */}
      <Group justify="space-between" wrap="wrap" gap="xs">
        <Group gap="xs" wrap="wrap">
          <Button
            size="xs"
            variant="subtle"
            leftSection={<IconArrowLeft size={14} />}
            onClick={() => navigate(`/admin/maps/${map.id}`)}
          >
            {t('backToViewer')}
          </Button>
          <Title order={4}>{t('title', { map: mapTitle })}</Title>
          <Code>#{map.id}</Code>
          {dirty ? (
            <Badge size="xs" color="yellow" variant="light">
              {t('unsavedChanges')}
            </Badge>
          ) : (
            <Badge size="xs" color="teal" variant="light">
              {t('saved')}
            </Badge>
          )}
        </Group>

        <Group gap="xs" wrap="wrap">
          <SegmentedControl
            size="xs"
            value={mode}
            onChange={(val) => setMode(val as EditorMode)}
            data={[
              { value: 'select', label: t('toolbar.select') },
              { value: 'addNpc', label: t('toolbar.addNpc') },
              { value: 'drawSpawn', label: t('toolbar.drawSpawn') },
              { value: 'pickStart', label: t('toolbar.pickStart') },
            ]}
          />

          <Switch
            size="xs"
            label={t('toolbar.walkability')}
            checked={showWalkability}
            disabled={!map.hasGrid}
            onChange={(e) => setShowWalkability(e.currentTarget.checked)}
          />

          <Tooltip label={t('toolbar.undo')}>
            <ActionIcon variant="default" size="md" disabled={!history || !canUndo(history)} onClick={handleUndo}>
              <IconArrowBackUp size={16} />
            </ActionIcon>
          </Tooltip>
          <Tooltip label={t('toolbar.redo')}>
            <ActionIcon variant="default" size="md" disabled={!history || !canRedo(history)} onClick={handleRedo}>
              <IconArrowForwardUp size={16} />
            </ActionIcon>
          </Tooltip>
          <Tooltip label={t('toolbar.fit')}>
            <ActionIcon variant="default" size="md" onClick={() => setFitSeq((n) => n + 1)}>
              <IconFocusCentered size={16} />
            </ActionIcon>
          </Tooltip>

          <Button
            size="xs"
            leftSection={<IconDeviceFloppy size={14} />}
            loading={saveMutation.isPending}
            disabled={!dirty}
            onClick={() => saveMutation.mutate()}
          >
            {t('toolbar.save')}
          </Button>

          {changedFilesToReload.length > 0 && (
            <Button
              size="xs"
              color="teal"
              variant="light"
              leftSection={<IconRefresh size={14} />}
              loading={reloadMutation.isPending}
              onClick={() => reloadMutation.mutate(changedFilesToReload)}
            >
              {t('reloadInGame')}
            </Button>
          )}
        </Group>
      </Group>

      {/* Mode hint bar */}
      {mode !== 'select' && (
        <Alert color="blue" py={6} px="sm">
          <Text size="xs">
            {mode === 'addNpc' && t('modeHints.addNpc')}
            {mode === 'drawSpawn' && t('modeHints.drawSpawn')}
            {mode === 'pickStart' && t('modeHints.pickStart')}
          </Text>
        </Alert>
      )}

      {/* Conflict alert */}
      {conflict && (
        <Alert color="red" title={t('conflictTitle')} icon={<IconAlertTriangle size={16} />} py="xs">
          <Group justify="space-between" wrap="wrap">
            <Text size="xs">{t('conflictBody')}</Text>
            <Button size="xs" color="red" variant="light" onClick={() => void editQuery.refetch()}>
              {t('reloadFromDisk')}
            </Button>
          </Group>
        </Alert>
      )}

      {/* Inline validation problems */}
      {(problems.length > 0 || localHints.length > 0) && (
        <Alert
          color={errorProblems.length > 0 || localHints.length > 0 ? 'red' : 'yellow'}
          icon={<IconAlertTriangle size={16} />}
          py={6}
          px="sm"
        >
          <Stack gap={2}>
            <Group gap="xs">
              <Text size="xs" fw={700}>
                {t('validation.title')}
              </Text>
              {(errorProblems.length > 0 || localHints.length > 0) && (
                <Badge size="xs" color="red">
                  {t('validation.errorsCount', { count: errorProblems.length + localHints.length })}
                </Badge>
              )}
              {warningProblems.length > 0 && (
                <Badge size="xs" color="yellow">
                  {t('validation.warningsCount', { count: warningProblems.length })}
                </Badge>
              )}
            </Group>
            {localHints.map((hint, i) => (
              <Text key={`local-${i}`} size="xs">
                • {hint}
              </Text>
            ))}
            {problems.slice(0, 8).map((p, i) => (
              <Text key={`${p.file}-${p.line}-${i}`} size="xs">
                • <Code fz="xs">{p.file}</Code>
                {p.line != null ? ` (${t('validation.line', { line: p.line })})` : ''}: {p.message}
              </Text>
            ))}
          </Stack>
        </Alert>
      )}

      {/* Main editor body: Canvas + Sidebar */}
      <Box style={{ display: 'flex', gap: 12, flex: 1, minHeight: 0 }}>
        <Box style={{ flex: 1, minWidth: 0, height: '100%' }}>
          <MapEditorCanvas
            key={map.id}
            map={map}
            grid={gridQuery.data ?? null}
            snapshot={snapshot}
            mode={mode}
            selected={selected}
            showWalkability={showWalkability}
            focusCell={focusCell}
            fitSeq={fitSeq}
            onSelect={handleSelect}
            onMoveNpc={handleMoveNpc}
            onPlaceNpc={handlePlaceNpc}
            onCreateSpawn={handleCreateSpawn}
            onUpdateSpawnGeometry={handleUpdateSpawnGeometry}
            onPickStartPoint={handlePickStartPoint}
            onRejectCell={handleRejectCell}
          />
        </Box>

        <MapEditorSidebar
          snapshot={snapshot}
          availableFlags={editQuery.data?.availableFlags ?? []}
          selected={selected}
          activeTab={sidebarTab}
          decodedGrid={decodedGrid}
          onTabChange={setSidebarTab}
          onSelect={handleSelect}
          onFocusCell={(x, y) => setFocusCell((prev) => ({ x, y, seq: (prev?.seq ?? 0) + 1 }))}
          onUpdateNpc={handleUpdateNpc}
          onDeleteNpc={handleDeleteNpc}
          onQuickAddNpc={() => {
            const sp = snapshot.settings.portal ?? { x: Math.floor((map.width ?? 100) / 2), y: Math.floor((map.height ?? 100) / 2) }
            handlePlaceNpc(sp.x, sp.y)
          }}
          onUpdateSpawn={handleUpdateSpawn}
          onDeleteSpawn={handleDeleteSpawn}
          onQuickAddSpawn={() => {
            const sp = snapshot.settings.portal ?? { x: Math.floor((map.width ?? 100) / 2), y: Math.floor((map.height ?? 100) / 2) }
            handleCreateSpawn(sp.x, sp.y, 10)
          }}
          onUpdatePortal={handleUpdatePortal}
          onOpenPortalTargetPicker={(p) => setPortalPickerFor(p)}
          onUpdateSettings={handleUpdateSettings}
          onStartPickStartPoint={() => setMode('pickStart')}
        />
      </Box>

      {/* Portal destination picker modal */}
      <TargetMapPickerModal
        opened={portalPickerFor != null}
        initialMapId={portalPickerFor?.toMap ?? map.id}
        initialX={portalPickerFor?.toX ?? null}
        initialY={portalPickerFor?.toY ?? null}
        onClose={() => setPortalPickerFor(null)}
        onConfirm={(toMap, toMapName, toX, toY) => {
          if (portalPickerFor) {
            handleUpdatePortal(portalPickerFor.index, { toMap, toMapName, toX, toY })
          }
          setPortalPickerFor(null)
        }}
      />
    </Stack>
  )
}
