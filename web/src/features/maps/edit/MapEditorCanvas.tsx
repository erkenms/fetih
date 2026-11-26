import { Badge, Box, Group, Paper, Text } from '@mantine/core'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { Schemas } from '../../../api/client'
import { npcPicture, useLooks } from '../../../components/looks'
import { assetUrl } from '../../../components/pickers'
import {
  cellDiamondLatLng,
  cellElevation,
  cellToLatLng,
  decodeBase64Bytes,
  fallbackMeta,
  isCellWalkable,
  latLngToCellInt,
  mapBoundsLatLng,
  spawnCircleLatLng,
  type IsoMeta,
} from '../../../lib/iso'
import { MapGridCanvasLayer, type DecodedGrid } from '../gridCanvasLayer'
import { escapeHtml, lookIcon, lookNameHtml, trackLookScale } from '../lookMarkers'
import { coversLayer as makeCoversLayer, isTerrainRole, terrainNpcLayer, useTerrainNpcs } from '../scene'
import { cellDistance, clampSpread, type MapEditorSnapshot } from './editorState'

export type EditorMode = 'select' | 'addNpc' | 'drawSpawn' | 'pickStart'

export type EditorSelection =
  | { type: 'npc'; id: number }
  | { type: 'spawn'; id: number }
  | { type: 'portal'; index: number }

interface MapEditorCanvasProps {
  map: Schemas['MapDetail']
  grid: Schemas['MapGridResponse'] | null
  snapshot: MapEditorSnapshot
  mode: EditorMode
  selected: EditorSelection | null
  showWalkability: boolean
  focusCell?: { x: number; y: number; seq: number } | null
  fitSeq?: number
  onSelect: (selection: EditorSelection | null) => void
  onMoveNpc: (id: number, x: number, y: number) => void
  onPlaceNpc: (x: number, y: number) => void
  onCreateSpawn: (x: number, y: number, spread: number) => void
  onUpdateSpawnGeometry: (id: number, x: number, y: number, spread: number) => void
  onPickStartPoint: (x: number, y: number) => void
  onRejectCell: (reason: 'unwalkable' | 'outOfBounds', x: number, y: number) => void
}

function makeBadgeIcon(label: string, color: string, selected = false): L.DivIcon {
  return L.divIcon({
    className: 'fetih-map-badge',
    iconSize: [0, 0],
    iconAnchor: [0, 0],
    html: badgeHtml(label, color, selected),
  })
}

function badgeHtml(label: string, color: string, selected = false): string {
  const ring = selected ? '0 0 0 3px rgba(250, 204, 21, 0.9), 0 2px 8px rgba(0,0,0,0.6)' : '0 2px 6px rgba(0,0,0,0.45)'
  return `<div style="
      transform: translate(-50%, -100%);
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 2px 6px;
      border-radius: 999px;
      background: rgba(15, 23, 42, 0.9);
      border: 1.5px solid ${selected ? '#facc15' : color};
      color: #f8fafc;
      font-size: 11px;
      font-weight: 600;
      line-height: 1.2;
      white-space: nowrap;
      box-shadow: ${ring};
      pointer-events: auto;
      cursor: grab;
    "><span style="width:8px;height:8px;border-radius:50%;background:${color};flex-shrink:0;"></span><span>${escapeHtml(label)}</span></div>`
}

function makeHandleIcon(color: string, label: string): L.DivIcon {
  return L.divIcon({
    className: 'fetih-edit-handle',
    iconSize: [14, 14],
    iconAnchor: [7, 7],
    html: `<div title="${escapeHtml(label)}" style="
      width: 14px;
      height: 14px;
      border-radius: 50%;
      background: ${color};
      border: 2px solid #ffffff;
      box-shadow: 0 1px 4px rgba(0,0,0,0.6);
      cursor: move;
    "></div>`,
  })
}

export function MapEditorCanvas({
  map,
  grid,
  snapshot,
  mode,
  selected,
  showWalkability,
  focusCell,
  fitSeq,
  onSelect,
  onMoveNpc,
  onPlaceNpc,
  onCreateSpawn,
  onUpdateSpawnGeometry,
  onPickStartPoint,
  onRejectCell,
}: MapEditorCanvasProps) {
  const { t } = useTranslation('maps')
  const containerRef = useRef<HTMLDivElement | null>(null)
  const leafletRef = useRef<L.Map | null>(null)
  const layersRef = useRef<{
    walkability?: L.GridLayer
    portals: L.LayerGroup
    npcs: L.LayerGroup
    spawns: L.LayerGroup
    startPoint: L.LayerGroup
    handles: L.LayerGroup
    preview: L.LayerGroup
  } | null>(null)

  const modeRef = useRef(mode)
  modeRef.current = mode
  const onSelectRef = useRef(onSelect)
  onSelectRef.current = onSelect
  const onMoveNpcRef = useRef(onMoveNpc)
  onMoveNpcRef.current = onMoveNpc
  const onPlaceNpcRef = useRef(onPlaceNpc)
  onPlaceNpcRef.current = onPlaceNpc
  const onCreateSpawnRef = useRef(onCreateSpawn)
  onCreateSpawnRef.current = onCreateSpawn
  const onUpdateSpawnGeometryRef = useRef(onUpdateSpawnGeometry)
  onUpdateSpawnGeometryRef.current = onUpdateSpawnGeometry
  const onPickStartPointRef = useRef(onPickStartPoint)
  onPickStartPointRef.current = onPickStartPoint
  const onRejectCellRef = useRef(onRejectCell)
  onRejectCellRef.current = onRejectCell

  const meta: IsoMeta = useMemo(() => {
    if (map.meta) return map.meta
    return fallbackMeta(map.width ?? 512, map.height ?? 512)
  }, [map.meta, map.width, map.height])

  const decodedGrid: DecodedGrid | null = useMemo(() => {
    if (!grid) return null
    return {
      width: grid.width,
      height: grid.height,
      walkable: decodeBase64Bytes(grid.walkableBase64),
      elevation: decodeBase64Bytes(grid.elevationBase64),
      minElevation: grid.minElevation,
      maxElevation: grid.maxElevation,
    }
  }, [grid])

  const decodedGridRef = useRef(decodedGrid)
  decodedGridRef.current = decodedGrid

  const mapW = decodedGrid?.width ?? map.width ?? meta.mapSize.width
  const mapH = decodedGrid?.height ?? map.height ?? meta.mapSize.height
  const mapBoundsRef = useRef({ w: mapW, h: mapH })
  mapBoundsRef.current = { w: mapW, h: mapH }

  const [cursor, setCursor] = useState<{
    x: number
    y: number
    inside: boolean
    walkable: boolean | null
    elevation: number | null
  } | null>(null)

  const checkWalkableCell = (x: number, y: number): 'ok' | 'outOfBounds' | 'unwalkable' => {
    const { w, h } = mapBoundsRef.current
    if (x < 0 || y < 0 || x >= w || y >= h) return 'outOfBounds'
    const g = decodedGridRef.current
    if (g && !isCellWalkable(g.walkable, g.width, g.height, x, y)) return 'unwalkable'
    return 'ok'
  }

  // Initialize Leaflet map once per map.id / meta
  useEffect(() => {
    const el = containerRef.current
    if (!el) return

    const bounds = mapBoundsLatLng(meta)
    const lmap = L.map(el, {
      crs: L.CRS.Simple,
      minZoom: 0,
      maxZoom: meta.maxZoom + 2,
      zoomSnap: 0.5,
      zoomDelta: 0.5,
      attributionControl: false,
      preferCanvas: true,
    })
    leafletRef.current = lmap

    if (map.meta && meta.ground) {
      L.tileLayer(`/assets/${meta.ground}`, {
        tileSize: meta.tileSize,
        minZoom: 0,
        maxNativeZoom: meta.maxZoom,
        maxZoom: meta.maxZoom + 2,
        noWrap: true,
        bounds,
      }).addTo(lmap)

      if (meta.covers) makeCoversLayer(map.meta, bounds).layer.addTo(lmap)
    } else if (map.minimap && assetUrl(map.minimap)) {
      L.imageOverlay(assetUrl(map.minimap)!, bounds).addTo(lmap)
    }

    if (!map.meta) {
      L.polygon(
        [
          cellToLatLng(0, 0, meta),
          cellToLatLng(mapW, 0, meta),
          cellToLatLng(mapW, mapH, meta),
          cellToLatLng(0, mapH, meta),
        ],
        {
          color: '#475569',
          weight: 1.5,
          dashArray: '6 4',
          fillColor: '#0f172a',
          fillOpacity: map.minimap ? 0.08 : 0.75,
          interactive: false,
        },
      ).addTo(lmap)
    }

    const groups = {
      portals: L.layerGroup().addTo(lmap),
      spawns: L.layerGroup().addTo(lmap),
      npcs: L.layerGroup().addTo(lmap),
      startPoint: L.layerGroup().addTo(lmap),
      handles: L.layerGroup().addTo(lmap),
      preview: L.layerGroup().addTo(lmap),
    }
    layersRef.current = groups

    if (map.portal) {
      lmap.setView(cellToLatLng(map.portal.x, map.portal.y, meta), Math.max(1, meta.maxZoom - 2), { animate: false })
    } else {
      lmap.fitBounds(bounds, { padding: [24, 24], animate: false })
    }
    trackLookScale(lmap, meta.maxZoom)

    let drawingOrigin: { x: number; y: number } | null = null

    const handleMouseMove = (e: L.LeafletMouseEvent) => {
      const cell = latLngToCellInt(e.latlng.lat, e.latlng.lng, meta)
      const g = decodedGridRef.current
      const { w, h } = mapBoundsRef.current
      const inside = cell.x >= 0 && cell.y >= 0 && cell.x < w && cell.y < h
      setCursor({
        x: cell.x,
        y: cell.y,
        inside,
        walkable: g && inside ? isCellWalkable(g.walkable, g.width, g.height, cell.x, cell.y) : null,
        elevation:
          g && inside
            ? cellElevation(g.elevation, g.width, g.height, cell.x, cell.y, g.minElevation, g.maxElevation)
            : null,
      })

      if (drawingOrigin && modeRef.current === 'drawSpawn') {
        const spread = clampSpread(cellDistance(drawingOrigin.x, drawingOrigin.y, cell.x, cell.y))
        groups.preview.clearLayers()
        L.polygon(spawnCircleLatLng(drawingOrigin.x, drawingOrigin.y, spread, meta), {
          color: '#f59e0b',
          weight: 2,
          dashArray: '4 4',
          fillColor: '#f59e0b',
          fillOpacity: 0.25,
          interactive: false,
        })
          .bindTooltip(`(${drawingOrigin.x}, ${drawingOrigin.y}) r=${spread}`, { permanent: true, direction: 'center' })
          .addTo(groups.preview)
      }
    }

    const handleMouseDown = (e: L.LeafletMouseEvent) => {
      if (modeRef.current !== 'drawSpawn') return
      const cell = latLngToCellInt(e.latlng.lat, e.latlng.lng, meta)
      const { w, h } = mapBoundsRef.current
      if (cell.x < 0 || cell.y < 0 || cell.x >= w || cell.y >= h) {
        onRejectCellRef.current('outOfBounds', cell.x, cell.y)
        return
      }
      drawingOrigin = cell
      groups.preview.clearLayers()
      L.polygon(spawnCircleLatLng(cell.x, cell.y, 1, meta), {
        color: '#f59e0b',
        weight: 2,
        dashArray: '4 4',
        fillColor: '#f59e0b',
        fillOpacity: 0.25,
        interactive: false,
      }).addTo(groups.preview)
    }

    const handleMouseUp = (e: L.LeafletMouseEvent) => {
      if (!drawingOrigin || modeRef.current !== 'drawSpawn') return
      const cell = latLngToCellInt(e.latlng.lat, e.latlng.lng, meta)
      const spread = clampSpread(cellDistance(drawingOrigin.x, drawingOrigin.y, cell.x, cell.y))
      const origin = drawingOrigin
      drawingOrigin = null
      groups.preview.clearLayers()
      onCreateSpawnRef.current(origin.x, origin.y, spread)
    }

    const handleClick = (e: L.LeafletMouseEvent) => {
      const m = modeRef.current
      if (m === 'addNpc' || m === 'pickStart') {
        const cell = latLngToCellInt(e.latlng.lat, e.latlng.lng, meta)
        const verdict = checkWalkableCell(cell.x, cell.y)
        if (verdict !== 'ok') {
          onRejectCellRef.current(verdict, cell.x, cell.y)
          return
        }
        if (m === 'addNpc') onPlaceNpcRef.current(cell.x, cell.y)
        else onPickStartPointRef.current(cell.x, cell.y)
      }
    }

    const handleMouseOut = () => setCursor(null)

    lmap.on('mousemove', handleMouseMove)
    lmap.on('mousedown', handleMouseDown)
    lmap.on('mouseup', handleMouseUp)
    lmap.on('click', handleClick)
    lmap.on('mouseout', handleMouseOut)

    return () => {
      lmap.off('mousemove', handleMouseMove)
      lmap.off('mousedown', handleMouseDown)
      lmap.off('mouseup', handleMouseUp)
      lmap.off('click', handleClick)
      lmap.off('mouseout', handleMouseOut)
      lmap.remove()
      leafletRef.current = null
      layersRef.current = null
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [map.id, meta])

  // Toggle Leaflet map dragging when entering/leaving drawSpawn mode
  useEffect(() => {
    const lmap = leafletRef.current
    if (!lmap) return
    if (mode === 'drawSpawn') lmap.dragging.disable()
    else lmap.dragging.enable()
  }, [mode])

  // Scenery of the terrain NPCs, following them as they're edited
  const terrainCatalog = useTerrainNpcs().data
  useEffect(() => {
    const lmap = leafletRef.current
    if (!lmap) return
    const layer = terrainNpcLayer(terrainCatalog, snapshot.npcs, meta).addTo(lmap)
    return () => {
      layer.remove()
    }
  }, [terrainCatalog, snapshot.npcs, meta])

  // Manage walkability canvas overlay
  useEffect(() => {
    const lmap = leafletRef.current
    const layers = layersRef.current
    if (!lmap || !layers) return

    if (layers.walkability) {
      lmap.removeLayer(layers.walkability)
      layers.walkability = undefined
    }
    if (decodedGrid && showWalkability) {
      layers.walkability = new MapGridCanvasLayer(meta, decodedGrid, 'walkability', { zIndex: 200 })
      layers.walkability.addTo(lmap)
    }
  }, [decodedGrid, meta, showWalkability])

  const looks = useLooks().index

  // Re-render editable entities & handles whenever snapshot or selection changes
  useEffect(() => {
    const layers = layersRef.current
    if (!layers) return

    layers.portals.clearLayers()
    layers.spawns.clearLayers()
    layers.npcs.clearLayers()
    layers.startPoint.clearLayers()
    layers.handles.clearLayers()

    // 1. Portals
    for (const p of snapshot.portals) {
      if (p.x == null || p.y == null) continue
      const isSelected = selected?.type === 'portal' && selected.index === p.index
      const pos = cellToLatLng(p.x, p.y, meta)
      const diamond = L.polygon(cellDiamondLatLng(p.x, p.y, meta), {
        color: isSelected ? '#facc15' : '#06b6d4',
        weight: isSelected ? 3 : 2,
        fillColor: '#22d3ee',
        fillOpacity: isSelected ? 0.65 : 0.4,
      })
      const targetText = p.toMap != null ? `→ ${p.toMapName ?? `#${p.toMap}`} (${p.toX}, ${p.toY})` : '(unlinked)'
      diamond.bindTooltip(`Portal #${p.index} (${p.x}, ${p.y}) ${targetText}`, { direction: 'top' })
      diamond.on('click', (e) => {
        if (modeRef.current !== 'select') return
        L.DomEvent.stopPropagation(e)
        onSelectRef.current({ type: 'portal', index: p.index })
      })
      diamond.addTo(layers.portals)

      if (p.toMap === map.id && p.toX != null && p.toY != null) {
        L.polyline([pos, cellToLatLng(p.toX, p.toY, meta)], {
          color: isSelected ? '#facc15' : '#22d3ee',
          weight: isSelected ? 2.5 : 1.5,
          dashArray: '6 4',
          interactive: false,
        }).addTo(layers.portals)
      }
    }

    // 2. Spawns
    for (const s of snapshot.spawns) {
      const isSelected = selected?.type === 'spawn' && selected.id === s.id
      const color = isSelected ? '#facc15' : s.monsterBoss ? '#ef4444' : '#f59e0b'
      const poly = L.polygon(spawnCircleLatLng(s.x, s.y, s.spread, meta), {
        color,
        weight: isSelected ? 3 : 1.5,
        fillColor: s.monsterBoss ? '#ef4444' : '#f59e0b',
        fillOpacity: isSelected ? 0.28 : 0.16,
      })
      const name = s.monsterName ?? `#${s.monsterId}`
      poly.bindTooltip(`#${s.id} ${name} ×${s.amount} (r=${s.spread}, ${s.respawn}s)`, { sticky: true })
      poly.on('click', (e) => {
        if (modeRef.current !== 'select') return
        L.DomEvent.stopPropagation(e)
        onSelectRef.current({ type: 'spawn', id: s.id })
      })
      poly.addTo(layers.spawns)

      // Center move handle & edge resize handle for selected spawn
      if (isSelected) {
        const centerMarker = L.marker(cellToLatLng(s.x, s.y, meta), {
          icon: makeHandleIcon('#3b82f6', 'Move spawn'),
          draggable: true,
          zIndexOffset: 600,
        })
        centerMarker.on('drag', (ev) => {
          const ll = (ev.target as L.Marker).getLatLng()
          const c = latLngToCellInt(ll.lat, ll.lng, meta)
          layers.preview.clearLayers()
          L.polygon(spawnCircleLatLng(c.x, c.y, s.spread, meta), {
            color: '#3b82f6',
            weight: 2,
            dashArray: '4 4',
            fillOpacity: 0.15,
            interactive: false,
          }).addTo(layers.preview)
        })
        centerMarker.on('dragend', (ev) => {
          layers.preview.clearLayers()
          const ll = (ev.target as L.Marker).getLatLng()
          const c = latLngToCellInt(ll.lat, ll.lng, meta)
          const { w, h } = mapBoundsRef.current
          if (c.x < 0 || c.y < 0 || c.x >= w || c.y >= h) {
            centerMarker.setLatLng(cellToLatLng(s.x, s.y, meta))
            onRejectCellRef.current('outOfBounds', c.x, c.y)
            return
          }
          onUpdateSpawnGeometryRef.current(s.id, c.x, c.y, s.spread)
        })
        centerMarker.addTo(layers.handles)

        const edgeOffset = Math.max(1, s.spread)
        const edgeMarker = L.marker(cellToLatLng(s.x + edgeOffset, s.y, meta), {
          icon: makeHandleIcon('#f59e0b', 'Resize spread'),
          draggable: true,
          zIndexOffset: 610,
        })
        edgeMarker.on('drag', (ev) => {
          const ll = (ev.target as L.Marker).getLatLng()
          const c = latLngToCellInt(ll.lat, ll.lng, meta)
          const r = clampSpread(cellDistance(s.x, s.y, c.x, c.y))
          layers.preview.clearLayers()
          L.polygon(spawnCircleLatLng(s.x, s.y, r, meta), {
            color: '#facc15',
            weight: 2,
            dashArray: '4 4',
            fillOpacity: 0.2,
            interactive: false,
          })
            .bindTooltip(`r=${r}`, { permanent: true, direction: 'center' })
            .addTo(layers.preview)
        })
        edgeMarker.on('dragend', (ev) => {
          layers.preview.clearLayers()
          const ll = (ev.target as L.Marker).getLatLng()
          const c = latLngToCellInt(ll.lat, ll.lng, meta)
          const r = clampSpread(cellDistance(s.x, s.y, c.x, c.y))
          onUpdateSpawnGeometryRef.current(s.id, s.x, s.y, r)
        })
        edgeMarker.addTo(layers.handles)
      }
    }

    // 3. NPCs (draggable with cell snapping & walkability check)
    for (const npc of snapshot.npcs) {
      const isSelected = selected?.type === 'npc' && selected.id === npc.id
      const pos = cellToLatLng(npc.x, npc.y, meta)
      const label = npc.name ? `${npc.name} (#${npc.id})` : `#${npc.id}`
      const picture = isTerrainRole(npc.role) ? null : npcPicture(looks, npc.lookface)
      const marker = L.marker(pos, {
        icon: picture
          ? lookIcon(picture, badgeHtml(label, '#3b82f6', isSelected), {
              below: lookNameHtml(label),
              className: isSelected ? 'fetih-look-selected' : undefined,
            })
          : makeBadgeIcon(label, '#3b82f6', isSelected),
        draggable: true,
        zIndexOffset: isSelected ? 500 : 100,
      })

      marker.on('click', (e) => {
        if (modeRef.current !== 'select') return
        L.DomEvent.stopPropagation(e)
        onSelectRef.current({ type: 'npc', id: npc.id })
      })

      marker.on('drag', (ev) => {
        const ll = (ev.target as L.Marker).getLatLng()
        const c = latLngToCellInt(ll.lat, ll.lng, meta)
        const ok = checkWalkableCell(c.x, c.y) === 'ok'
        layers.preview.clearLayers()
        L.polygon(cellDiamondLatLng(c.x, c.y, meta), {
          color: ok ? '#22c55e' : '#ef4444',
          weight: 2,
          fillColor: ok ? '#22c55e' : '#ef4444',
          fillOpacity: 0.45,
          interactive: false,
        }).addTo(layers.preview)
      })

      marker.on('dragend', (ev) => {
        layers.preview.clearLayers()
        const ll = (ev.target as L.Marker).getLatLng()
        const c = latLngToCellInt(ll.lat, ll.lng, meta)
        const verdict = checkWalkableCell(c.x, c.y)
        if (verdict !== 'ok') {
          marker.setLatLng(cellToLatLng(npc.x, npc.y, meta))
          onRejectCellRef.current(verdict, c.x, c.y)
          return
        }
        marker.setLatLng(cellToLatLng(c.x, c.y, meta))
        onSelectRef.current({ type: 'npc', id: npc.id })
        if (c.x !== npc.x || c.y !== npc.y) {
          onMoveNpcRef.current(npc.id, c.x, c.y)
        }
      })

      marker.addTo(layers.npcs)
    }

    // 4. Start point marker
    if (snapshot.settings.portal) {
      const sp = snapshot.settings.portal
      L.marker(cellToLatLng(sp.x, sp.y, meta), {
        icon: makeBadgeIcon(`★ Start (${sp.x}, ${sp.y})`, '#10b981'),
        interactive: false,
      }).addTo(layers.startPoint)
    }
  }, [snapshot, selected, meta, map.id, looks])

  // Focus cell when requested
  useEffect(() => {
    const lmap = leafletRef.current
    if (!lmap || !focusCell) return
    const targetZoom = Math.max(lmap.getZoom(), Math.max(2, meta.maxZoom - 1))
    lmap.setView(cellToLatLng(focusCell.x, focusCell.y, meta), targetZoom, { animate: true })
  }, [focusCell, meta])

  // Fit bounds when requested
  useEffect(() => {
    const lmap = leafletRef.current
    if (!lmap || !fitSeq) return
    lmap.fitBounds(mapBoundsLatLng(meta), { padding: [24, 24], animate: true })
  }, [fitSeq, meta])

  const cursorStyle = mode === 'select' ? undefined : 'crosshair'

  return (
    <Box
      pos="relative"
      w="100%"
      h="100%"
      style={{
        minHeight: 520,
        background: '#0b1120',
        borderRadius: 'var(--mantine-radius-sm)',
        overflow: 'hidden',
        cursor: cursorStyle,
      }}
    >
      <Box ref={containerRef} w="100%" h="100%" style={{ cursor: cursorStyle }} />

      <Paper
        pos="absolute"
        bottom={10}
        left={10}
        px="sm"
        py={6}
        radius="sm"
        style={{
          zIndex: 500,
          background: 'rgba(15, 23, 42, 0.88)',
          color: '#f8fafc',
          border: '1px solid rgba(148, 163, 184, 0.3)',
          pointerEvents: 'none',
        }}
      >
        <Group gap="xs">
          {cursor ? (
            <>
              <Text size="xs" ff="monospace" fw={600}>
                {t('viewer.cursor.cell', { x: cursor.x, y: cursor.y })}
              </Text>
              {!cursor.inside && (
                <Badge size="xs" color="gray" variant="light">
                  {t('viewer.cursor.outside')}
                </Badge>
              )}
              {cursor.walkable != null && (
                <Badge size="xs" color={cursor.walkable ? 'green' : 'red'} variant="filled">
                  {cursor.walkable ? t('viewer.cursor.walkable') : t('viewer.cursor.blocked')}
                </Badge>
              )}
              {cursor.elevation != null && (
                <Text size="xs" ff="monospace" c="gray.3">
                  {t('viewer.cursor.elevation', { value: cursor.elevation })}
                </Text>
              )}
            </>
          ) : (
            <Text size="xs" c="gray.4">
              {mapW} × {mapH}
            </Text>
          )}
        </Group>
      </Paper>
    </Box>
  )
}
