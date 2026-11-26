import { Badge, Box, Group, Paper, Text } from '@mantine/core'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { Schemas } from '../../api/client'
import { monsterPicture, npcPicture, useLooks } from '../../components/looks'
import { assetUrl } from '../../components/pickers'
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
} from '../../lib/iso'
import { MapGridCanvasLayer, type DecodedGrid } from './gridCanvasLayer'
import { escapeHtml, lookIcon, lookNameHtml, trackLookScale } from './lookMarkers'
import { coversLayer as makeCoversLayer, isTerrainRole, terrainNpcLayer, useTerrainNpcs, type SpriteLayer } from './scene'

export interface MapOverlaysState {
  covers: boolean
  animations: boolean
  terrainNpcs: boolean
  walkability: boolean
  elevation: boolean
  portals: boolean
  portalDestinations: boolean
  npcs: boolean
  goals: boolean
  spawns: boolean
  startAndReborn: boolean
  teleporters: boolean
  guildWar: boolean
}

export const defaultOverlays: MapOverlaysState = {
  covers: true,
  animations: true,
  terrainNpcs: true,
  walkability: false,
  elevation: false,
  portals: true,
  portalDestinations: true,
  npcs: true,
  goals: false,
  spawns: true,
  startAndReborn: true,
  teleporters: true,
  guildWar: true,
}

export type SelectedEntity =
  | { type: 'npc'; data: Schemas['MapNpcEntry'] }
  | { type: 'spawn'; data: Schemas['MapSpawnEntry'] }
  | { type: 'portal'; data: Schemas['MapPortalEntry'] }
  | { type: 'teleporter'; data: Schemas['MapTeleporterEntry'] }
  | { type: 'guild_object'; data: Schemas['MapGuildWarObject'] }
  | { type: 'guild_conductor'; data: Schemas['MapGuildWarConductor'] }

export interface MapViewPosition {
  x: number
  y: number
  z: number
}

interface MapViewerProps {
  map: Schemas['MapDetail']
  grid: Schemas['MapGridResponse'] | null
  overlays: MapOverlaysState
  initialView?: Partial<MapViewPosition>
  focusCell?: { x: number; y: number; seq: number } | null
  fitSeq?: number
  onViewChange?: (view: MapViewPosition) => void
  onNavigateMap?: (mapId: number, x?: number, y?: number) => void
  onSelectEntity?: (entity: SelectedEntity | null) => void
}

function makeBadgeIcon(label: string, color: string, facePath?: string | null): L.DivIcon {
  return L.divIcon({
    className: 'fetih-map-badge',
    iconSize: [0, 0],
    iconAnchor: [0, 0],
    html: badgeHtml(label, color, facePath),
  })
}

function badgeHtml(label: string, color: string, facePath?: string | null): string {
  const faceUrl = facePath ? assetUrl(facePath) : null
  const avatar = faceUrl
    ? `<img src="${escapeHtml(faceUrl)}" alt="" style="width:18px;height:18px;border-radius:50%;object-fit:cover;flex-shrink:0;border:1px solid rgba(255,255,255,0.6);" />`
    : `<span style="width:8px;height:8px;border-radius:50%;background:${color};flex-shrink:0;"></span>`

  return `<div style="
      transform: translate(-50%, -100%);
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 2px 6px;
      border-radius: 999px;
      background: rgba(15, 23, 42, 0.86);
      border: 1.5px solid ${color};
      color: #f8fafc;
      font-size: 11px;
      font-weight: 600;
      line-height: 1.2;
      white-space: nowrap;
      box-shadow: 0 2px 6px rgba(0,0,0,0.45);
      pointer-events: auto;
      cursor: pointer;
    ">${avatar}<span>${escapeHtml(label)}</span></div>`
}

export function MapViewer({
  map,
  grid,
  overlays,
  initialView,
  focusCell,
  fitSeq,
  onViewChange,
  onNavigateMap,
  onSelectEntity,
}: MapViewerProps) {
  const { t } = useTranslation('maps')
  const containerRef = useRef<HTMLDivElement | null>(null)
  const leafletRef = useRef<L.Map | null>(null)
  const layersRef = useRef<{
    covers?: L.LayerGroup
    coverSprites?: SpriteLayer
    terrainNpcs?: SpriteLayer
    walkability?: L.GridLayer
    elevation?: L.GridLayer
    portals: L.LayerGroup
    portalDestinations: L.LayerGroup
    npcs: L.LayerGroup
    goals: L.LayerGroup
    spawns: L.LayerGroup
    startAndReborn: L.LayerGroup
    teleporters: L.LayerGroup
    guildWar: L.LayerGroup
  } | null>(null)

  const onViewChangeRef = useRef(onViewChange)
  onViewChangeRef.current = onViewChange
  const onNavigateMapRef = useRef(onNavigateMap)
  onNavigateMapRef.current = onNavigateMap
  const onSelectEntityRef = useRef(onSelectEntity)
  onSelectEntityRef.current = onSelectEntity

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

  const [cursor, setCursor] = useState<{
    x: number
    y: number
    inside: boolean
    walkable: boolean | null
    elevation: number | null
  } | null>(null)

  // Create and destroy Leaflet map per map.id
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

    // Base background: A2 ground tiles, minimap image fallback, or isometric diamond wireframe
    let coversLayer: L.LayerGroup | undefined
    let coverSprites: SpriteLayer | undefined
    if (map.meta && meta.ground) {
      L.tileLayer(`/assets/${meta.ground}`, {
        tileSize: meta.tileSize,
        minZoom: 0,
        maxNativeZoom: meta.maxZoom,
        maxZoom: meta.maxZoom + 2,
        noWrap: true,
        bounds,
      }).addTo(lmap)

      if (meta.covers) {
        const covers = makeCoversLayer(map.meta, bounds)
        coversLayer = covers.layer
        coverSprites = covers.sprites
      }
    } else if (map.minimap && assetUrl(map.minimap)) {
      L.imageOverlay(assetUrl(map.minimap)!, bounds).addTo(lmap)
    }

    // Subtle map boundary diamond (always helpful in fallback mode, unobtrusive on tiled maps)
    const mapW = map.width ?? meta.mapSize.width
    const mapH = map.height ?? meta.mapSize.height
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
      covers: coversLayer,
      coverSprites,
      terrainNpcs: undefined as SpriteLayer | undefined,
      portals: L.layerGroup(),
      portalDestinations: L.layerGroup(),
      npcs: L.layerGroup(),
      goals: L.layerGroup(),
      spawns: L.layerGroup(),
      startAndReborn: L.layerGroup(),
      teleporters: L.layerGroup(),
      guildWar: L.layerGroup(),
    }
    layersRef.current = groups

    // Initial view from URL params, or map portal/start point, or fit bounds
    const hasInitialPos = typeof initialView?.x === 'number' && typeof initialView?.y === 'number'
    if (hasInitialPos) {
      const z = typeof initialView?.z === 'number' ? initialView.z : Math.max(1, meta.maxZoom - 2)
      lmap.setView(cellToLatLng(initialView.x!, initialView.y!, meta), z, { animate: false })
    } else if (map.portal) {
      const z = typeof initialView?.z === 'number' ? initialView.z : Math.max(1, meta.maxZoom - 2)
      lmap.setView(cellToLatLng(map.portal.x, map.portal.y, meta), z, { animate: false })
    } else {
      lmap.fitBounds(bounds, { padding: [24, 24], animate: false })
    }
    trackLookScale(lmap, meta.maxZoom)

    const handleMoveEnd = () => {
      const center = lmap.getCenter()
      const cell = latLngToCellInt(center.lat, center.lng, meta)
      onViewChangeRef.current?.({
        x: cell.x,
        y: cell.y,
        z: Math.round(lmap.getZoom() * 2) / 2,
      })
    }

    const handleMouseMove = (e: L.LeafletMouseEvent) => {
      const cell = latLngToCellInt(e.latlng.lat, e.latlng.lng, meta)
      const g = decodedGridRef.current
      const w = g?.width ?? mapW
      const h = g?.height ?? mapH
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
    }

    const handleMouseOut = () => setCursor(null)

    lmap.on('moveend', handleMoveEnd)
    lmap.on('zoomend', handleMoveEnd)
    lmap.on('mousemove', handleMouseMove)
    lmap.on('mouseout', handleMouseOut)

    return () => {
      lmap.off('moveend', handleMoveEnd)
      lmap.off('zoomend', handleMoveEnd)
      lmap.off('mousemove', handleMouseMove)
      lmap.off('mouseout', handleMouseOut)
      lmap.remove()
      leafletRef.current = null
      layersRef.current = null
    }
    // Re-initialize only when the map ID or its projection changes
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [map.id, meta])

  const looks = useLooks().index

  // Populate vector overlay layers whenever map data changes
  useEffect(() => {
    const lmap = leafletRef.current
    const layers = layersRef.current
    if (!lmap || !layers) return

    layers.portals.clearLayers()
    layers.portalDestinations.clearLayers()
    layers.npcs.clearLayers()
    layers.goals.clearLayers()
    layers.spawns.clearLayers()
    layers.startAndReborn.clearLayers()
    layers.teleporters.clearLayers()
    layers.guildWar.clearLayers()

    // 1. Portal cells & destination arrows
    for (const p of map.portals) {
      if (p.x == null || p.y == null) continue
      const pos = cellToLatLng(p.x, p.y, meta)

      const diamond = L.polygon(cellDiamondLatLng(p.x, p.y, meta), {
        color: '#06b6d4',
        weight: 2,
        fillColor: '#22d3ee',
        fillOpacity: 0.45,
      })
      diamond.bindTooltip(`Portal #${p.index} (${p.x}, ${p.y})`, { direction: 'top' })
      diamond.on('click', () => onSelectEntityRef.current?.({ type: 'portal', data: p }))
      diamond.addTo(layers.portals)

      if (p.toMap != null && p.toX != null && p.toY != null) {
        const destLabel = `${p.toMapName ?? `#${p.toMap}`} (${p.toX}, ${p.toY})`
        if (p.toMap === map.id) {
          const destPos = cellToLatLng(p.toX, p.toY, meta)
          const line = L.polyline([pos, destPos], {
            color: '#22d3ee',
            weight: 2,
            dashArray: '6 4',
            opacity: 0.85,
          })
          line.bindTooltip(`#${p.index} → (${p.toX}, ${p.toY})`, { sticky: true })
          line.on('click', () => {
            onSelectEntityRef.current?.({ type: 'portal', data: p })
            onNavigateMapRef.current?.(p.toMap!, p.toX!, p.toY!)
          })
          line.addTo(layers.portalDestinations)

          const targetDot = L.circleMarker(destPos, {
            radius: 5,
            color: '#0891b2',
            fillColor: '#67e8f9',
            fillOpacity: 0.9,
            weight: 2,
          })
          targetDot.on('click', () => onNavigateMapRef.current?.(p.toMap!, p.toX!, p.toY!))
          targetDot.addTo(layers.portalDestinations)
        }

        const arrow = L.marker(pos, {
          icon: makeBadgeIcon(`↗ ${destLabel}`, '#06b6d4'),
        })
        arrow.on('click', () => {
          onSelectEntityRef.current?.({ type: 'portal', data: p })
          onNavigateMapRef.current?.(p.toMap!, p.toX!, p.toY!)
        })
        arrow.addTo(layers.portalDestinations)
      }
    }

    // 2. NPCs & Training Goals
    for (const npc of map.npcs) {
      const pos = cellToLatLng(npc.x, npc.y, meta)
      if (npc.isGoal) {
        const dot = L.circleMarker(pos, {
          radius: 3.5,
          color: '#94a3b8',
          fillColor: '#cbd5e1',
          fillOpacity: 0.75,
          weight: 1,
        })
        dot.bindTooltip(`Goal #${npc.id} (${npc.x}, ${npc.y})`)
        dot.on('click', () => onSelectEntityRef.current?.({ type: 'npc', data: npc }))
        dot.addTo(layers.goals)
        continue
      }

      if (npc.kind === 'teleporter' || npc.kind === 'guild_pole' || npc.kind === 'guild_gate' || npc.kind === 'guild_conductor') {
        continue
      }

      const color = npc.kind === 'talker' ? '#60a5fa' : '#3b82f6'
      const label = npc.name ? `${npc.name}` : `#${npc.id}`
      const picture = isTerrainRole(npc.role) ? null : npcPicture(looks, npc.lookface)
      const marker = L.marker(pos, {
        icon: picture
          ? lookIcon(picture, badgeHtml(label, color, npc.facePath), { below: lookNameHtml(label) })
          : makeBadgeIcon(label, color, npc.facePath),
      })
      marker.bindTooltip(`${npc.name ?? 'NPC'} #${npc.id} (${npc.x}, ${npc.y})`, { direction: 'top', offset: [0, -24] })
      marker.on('click', () => onSelectEntityRef.current?.({ type: 'npc', data: npc }))
      marker.addTo(layers.npcs)
    }

    // 3. Teleporter NPCs
    for (const tp of map.teleporters) {
      const pos = cellToLatLng(tp.x, tp.y, meta)
      const label = `${tp.npcName ?? `#${tp.npcId}`} (${tp.destinations.length})`
      const npc = map.npcs.find((n) => n.id === tp.npcId)
      const picture = npc && !isTerrainRole(npc.role) ? npcPicture(looks, npc.lookface) : null
      const marker = L.marker(pos, {
        icon: picture
          ? lookIcon(picture, badgeHtml(label, '#a855f7', tp.facePath), { below: lookNameHtml(label) })
          : makeBadgeIcon(label, '#a855f7', tp.facePath),
      })
      const destList = tp.destinations.map((d) => d.name).join(', ')
      marker.bindTooltip(`${tp.npcName ?? 'Teleporter'} #${tp.npcId}: ${destList}`, { direction: 'top', offset: [0, -24] })
      marker.on('click', () => onSelectEntityRef.current?.({ type: 'teleporter', data: tp }))
      marker.addTo(layers.teleporters)
    }

    // 4. Spawn areas
    for (const s of map.spawns) {
      const color = s.monsterBoss ? '#ef4444' : '#f59e0b'
      const poly = L.polygon(spawnCircleLatLng(s.x, s.y, s.spread, meta), {
        color,
        weight: s.monsterBoss ? 2.5 : 1.5,
        fillColor: color,
        fillOpacity: s.monsterBoss ? 0.25 : 0.16,
      })
      const name = s.monsterName ?? `#${s.monsterId}`
      poly.bindTooltip(`${name} ×${s.amount} (r=${s.spread}, ${s.respawn}s)`, { sticky: true })
      poly.on('click', () => onSelectEntityRef.current?.({ type: 'spawn', data: s }))
      poly.addTo(layers.spawns)

      const picture = monsterPicture(looks, s.monsterId)
      if (picture) {
        const monster = L.marker(cellToLatLng(s.x, s.y, meta), { icon: lookIcon(picture, '') })
        monster.bindTooltip(`${name} ×${s.amount}`, { direction: 'top' })
        monster.on('click', () => onSelectEntityRef.current?.({ type: 'spawn', data: s }))
        monster.addTo(layers.spawns)
      }
    }

    // 5. Start / Portal point & Reborn indicator
    if (map.portal) {
      const pos = cellToLatLng(map.portal.x, map.portal.y, meta)
      const label = map.rebornMap
        ? `★ Start (${map.portal.x}, ${map.portal.y}) • Reborn → ${map.rebornMapName ?? `#${map.rebornMap}`}`
        : `★ Start (${map.portal.x}, ${map.portal.y})`
      const marker = L.marker(pos, {
        icon: makeBadgeIcon(label, '#10b981'),
      })
      if (map.rebornMap && map.rebornMap !== map.id) {
        marker.on('click', () => onNavigateMapRef.current?.(map.rebornMap!))
      }
      marker.addTo(layers.startAndReborn)
    }

    // 6. Guild War pole, gates & conductors
    if (map.guildWar) {
      const objects = [
        { obj: map.guildWar.pole, color: '#eab308', prefix: '♛ ' },
        { obj: map.guildWar.leftGate, color: '#ef4444', prefix: '⛨ ' },
        { obj: map.guildWar.rightGate, color: '#ef4444', prefix: '⛨ ' },
      ]
      for (const { obj, color, prefix } of objects) {
        if (!obj) continue
        const pos = cellToLatLng(obj.x, obj.y, meta)
        const marker = L.marker(pos, {
          icon: makeBadgeIcon(`${prefix}${obj.name}`, color),
        })
        marker.on('click', () => onSelectEntityRef.current?.({ type: 'guild_object', data: obj }))
        marker.addTo(layers.guildWar)

        if (obj.insidePoint) {
          const insidePos = cellToLatLng(obj.insidePoint.x, obj.insidePoint.y, meta)
          L.polyline([pos, insidePos], { color, weight: 2, dashArray: '4 4' }).addTo(layers.guildWar)
        }
      }

      for (const c of map.guildWar.conductors) {
        const pos = cellToLatLng(c.x, c.y, meta)
        const marker = L.marker(pos, {
          icon: makeBadgeIcon(`↗ ${c.city}`, '#c084fc'),
        })
        marker.on('click', () => {
          onSelectEntityRef.current?.({ type: 'guild_conductor', data: c })
          onNavigateMapRef.current?.(c.toMap, c.toX, c.toY)
        })
        marker.addTo(layers.guildWar)
      }
    }
  }, [map, meta, looks])

  // Scenery of the terrain NPCs (statues, gates, furniture), redrawn when the NPCs change
  const terrainCatalog = useTerrainNpcs().data
  useEffect(() => {
    const lmap = leafletRef.current
    const layers = layersRef.current
    if (!lmap || !layers) return
    if (layers.terrainNpcs) lmap.removeLayer(layers.terrainNpcs)
    layers.terrainNpcs = terrainNpcLayer(terrainCatalog, map.npcs, meta).setPlaying(overlays.animations)
    if (overlays.terrainNpcs) layers.terrainNpcs.addTo(lmap)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [terrainCatalog, map.npcs, meta])

  // Manage walkability & elevation canvas layers when grid arrives or changes
  useEffect(() => {
    const lmap = leafletRef.current
    const layers = layersRef.current
    if (!lmap || !layers) return

    if (layers.walkability) {
      lmap.removeLayer(layers.walkability)
      layers.walkability = undefined
    }
    if (layers.elevation) {
      lmap.removeLayer(layers.elevation)
      layers.elevation = undefined
    }

    if (decodedGrid) {
      layers.walkability = new MapGridCanvasLayer(meta, decodedGrid, 'walkability', { zIndex: 200 })
      layers.elevation = new MapGridCanvasLayer(meta, decodedGrid, 'elevation', { zIndex: 205 })
      if (overlays.walkability) layers.walkability.addTo(lmap)
      if (overlays.elevation) layers.elevation.addTo(lmap)
    }
  }, [decodedGrid, meta, overlays.walkability, overlays.elevation])

  // Sync overlay toggles
  useEffect(() => {
    const lmap = leafletRef.current
    const layers = layersRef.current
    if (!lmap || !layers) return

    const sync = (layer: L.Layer | undefined, visible: boolean) => {
      if (!layer) return
      if (visible && !lmap.hasLayer(layer)) layer.addTo(lmap)
      else if (!visible && lmap.hasLayer(layer)) lmap.removeLayer(layer)
    }

    sync(layers.covers, overlays.covers)
    sync(layers.terrainNpcs, overlays.terrainNpcs)
    layers.coverSprites?.setPlaying(overlays.animations)
    layers.terrainNpcs?.setPlaying(overlays.animations)
    sync(layers.walkability, overlays.walkability)
    sync(layers.elevation, overlays.elevation)
    sync(layers.portals, overlays.portals)
    sync(layers.portalDestinations, overlays.portalDestinations)
    sync(layers.npcs, overlays.npcs)
    sync(layers.goals, overlays.goals)
    sync(layers.spawns, overlays.spawns)
    sync(layers.startAndReborn, overlays.startAndReborn)
    sync(layers.teleporters, overlays.teleporters)
    sync(layers.guildWar, overlays.guildWar)
  }, [overlays])

  // Pan to requested cell
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

  return (
    <Box pos="relative" w="100%" h="100%" style={{ minHeight: 520, background: '#0b1120', borderRadius: 'var(--mantine-radius-sm)', overflow: 'hidden' }}>
      <Box ref={containerRef} w="100%" h="100%" />

      {!map.hasTiles && (
        <Badge
          pos="absolute"
          top={10}
          right={10}
          variant="filled"
          color="dark"
          style={{ zIndex: 500, pointerEvents: 'none', opacity: 0.9 }}
        >
          {map.minimap ? t('viewer.fallbackMinimap') : t('viewer.fallbackWireframe')}
        </Badge>
      )}

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
              {map.width && map.height ? `${map.width} × ${map.height}` : '—'}
            </Text>
          )}
        </Group>
      </Paper>
    </Box>
  )
}
