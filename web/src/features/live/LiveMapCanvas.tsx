import { Badge, Box, Group, Paper, Text } from '@mantine/core'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { Schemas } from '../../api/client'
import { assetUrl } from '../../components/pickers'
import {
  cellDiamondLatLng,
  cellToLatLng,
  decodeBase64Bytes,
  fallbackMeta,
  isCellWalkable,
  latLngToCellInt,
  mapBoundsLatLng,
  type IsoMeta,
} from '../../lib/iso'
import { MapGridCanvasLayer, type DecodedGrid } from '../maps/gridCanvasLayer'
import {
  hpPercent,
  pkNameStyle,
  type LiveGroundItem,
  type LiveLayersState,
  type LiveMapBooth,
  type LiveMapMonster,
  type LiveMapPlayer,
  type LiveMapState,
  type SelectedLiveEntity,
} from './liveState'

interface LiveMapCanvasProps {
  map: Schemas['MapDetail'] | null
  mapId: number
  grid: Schemas['MapGridResponse'] | null
  live: LiveMapState
  layers: LiveLayersState
  followedCharacterId: number | null
  selected: SelectedLiveEntity | null
  focusCell: { x: number; y: number; seq: number } | null
  fitSeq: number
  onSelect: (entity: SelectedLiveEntity | null) => void
}

function escapeHtml(text: string): string {
  return text
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
}

function makePlayerIcon(p: LiveMapPlayer, followed: boolean, selected: boolean): L.DivIcon {
  const style = pkNameStyle(p.nameColor)
  const pct = hpPercent(p.health, p.maxHealth)
  const ring = followed
    ? 'box-shadow: 0 0 0 3px #38bdf8, 0 2px 8px rgba(0,0,0,0.7);'
    : selected
      ? 'box-shadow: 0 0 0 2px #facc15, 0 2px 6px rgba(0,0,0,0.6);'
      : 'box-shadow: 0 2px 6px rgba(0,0,0,0.55);'
  const opacity = p.dead ? 'opacity: 0.65;' : ''
  const deadMark = p.dead ? '☠ ' : ''
  const hpColor = pct > 50 ? '#22c55e' : pct > 25 ? '#f59e0b' : '#ef4444'

  return L.divIcon({
    className: 'fetih-live-player',
    iconSize: [0, 0],
    iconAnchor: [0, 0],
    html: `<div style="
      transform: translate(-50%, -100%);
      display: inline-flex;
      flex-direction: column;
      align-items: center;
      gap: 2px;
      pointer-events: auto;
      cursor: pointer;
      ${opacity}
    ">
      <div style="
        display: inline-flex;
        align-items: center;
        gap: 4px;
        padding: 2px 7px;
        border-radius: 999px;
        background: rgba(15, 23, 42, 0.92);
        border: 2px solid ${style.border};
        color: ${style.text};
        font-size: 11px;
        font-weight: 700;
        line-height: 1.2;
        white-space: nowrap;
        ${ring}
      ">
        <span>${deadMark}${escapeHtml(p.name)}</span>
        <span style="font-size:10px;opacity:0.8;font-weight:500;">Lv.${p.level}</span>
      </div>
      <div style="width:44px;height:3px;background:rgba(15,23,42,0.85);border-radius:2px;overflow:hidden;">
        <div style="width:${pct}%;height:100%;background:${hpColor};"></div>
      </div>
    </div>`,
  })
}

function makeMonsterIcon(m: LiveMapMonster, selected: boolean): L.DivIcon {
  const pct = hpPercent(m.health, m.maxHealth)
  const color = !m.alive ? '#64748b' : m.guard ? '#38bdf8' : '#f97316'
  const ring = selected ? '0 0 0 2px #facc15' : '0 1px 3px rgba(0,0,0,0.6)'
  const showHp = m.alive && m.maxHealth > 0 && m.health < m.maxHealth

  return L.divIcon({
    className: 'fetih-live-monster',
    iconSize: [0, 0],
    iconAnchor: [0, 0],
    html: `<div style="
      transform: translate(-50%, -50%);
      display: inline-flex;
      flex-direction: column;
      align-items: center;
      gap: 1px;
      pointer-events: auto;
      cursor: pointer;
      opacity: ${m.alive ? 1 : 0.45};
    ">
      ${
        showHp
          ? `<div style="width:26px;height:3px;background:rgba(15,23,42,0.85);border-radius:2px;overflow:hidden;">
              <div style="width:${pct}%;height:100%;background:#ef4444;"></div>
            </div>`
          : ''
      }
      <div style="
        width: ${m.guard ? 10 : 8}px;
        height: ${m.guard ? 10 : 8}px;
        border-radius: ${m.guard ? '2px' : '50%'};
        background: ${color};
        border: 1.5px solid #0f172a;
        box-shadow: ${ring};
      "></div>
    </div>`,
  })
}

function makeGroundItemIcon(item: LiveGroundItem, selected: boolean): L.DivIcon {
  const isSilver = item.silver > 0
  const color = isSilver ? '#facc15' : '#10b981'
  const ring = selected ? '0 0 0 2px #ffffff' : '0 1px 4px rgba(0,0,0,0.6)'
  const iconUrl = !isSilver && item.icon ? assetUrl(item.icon) : null

  if (iconUrl) {
    return L.divIcon({
      className: 'fetih-live-item',
      iconSize: [0, 0],
      iconAnchor: [0, 0],
      html: `<div style="
        transform: translate(-50%, -50%);
        width: 20px;
        height: 20px;
        border-radius: 4px;
        background: rgba(15, 23, 42, 0.85);
        border: 1.5px solid ${color};
        box-shadow: ${ring};
        display: flex;
        align-items: center;
        justify-content: center;
        pointer-events: auto;
        cursor: pointer;
      "><img src="${escapeHtml(iconUrl)}" alt="" style="width:16px;height:16px;object-fit:contain;" /></div>`,
    })
  }

  return L.divIcon({
    className: 'fetih-live-item',
    iconSize: [0, 0],
    iconAnchor: [0, 0],
    html: `<div style="
      transform: translate(-50%, -50%) rotate(45deg);
      width: 9px;
      height: 9px;
      background: ${color};
      border: 1.5px solid #0f172a;
      box-shadow: ${ring};
      pointer-events: auto;
      cursor: pointer;
    "></div>`,
  })
}

function makeBoothIcon(booth: LiveMapBooth, selected: boolean): L.DivIcon {
  const ring = selected ? 'box-shadow: 0 0 0 2px #facc15;' : 'box-shadow: 0 2px 5px rgba(0,0,0,0.5);'
  return L.divIcon({
    className: 'fetih-live-booth',
    iconSize: [0, 0],
    iconAnchor: [0, 0],
    html: `<div style="
      transform: translate(-50%, -100%);
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 2px 6px;
      border-radius: 6px;
      background: rgba(88, 28, 135, 0.9);
      border: 1.5px solid #c084fc;
      color: #f3e8ff;
      font-size: 10px;
      font-weight: 600;
      white-space: nowrap;
      pointer-events: auto;
      cursor: pointer;
      ${ring}
    "><span>⚑ ${escapeHtml(booth.ownerName)}</span><span style="opacity:0.8">(${booth.items})</span></div>`,
  })
}

export function LiveMapCanvas({
  map,
  mapId,
  grid,
  live,
  layers,
  followedCharacterId,
  selected,
  focusCell,
  fitSeq,
  onSelect,
}: LiveMapCanvasProps) {
  const { t } = useTranslation('live')
  const containerRef = useRef<HTMLDivElement | null>(null)
  const leafletRef = useRef<L.Map | null>(null)
  const onSelectRef = useRef(onSelect)
  onSelectRef.current = onSelect

  const layerGroupsRef = useRef<{
    covers?: L.TileLayer
    walkability?: L.GridLayer
    portals: L.LayerGroup
    npcs: L.LayerGroup
    players: L.LayerGroup
    monsters: L.LayerGroup
    groundItems: L.LayerGroup
    booths: L.LayerGroup
  } | null>(null)

  const markersRef = useRef<{
    players: Map<number, { marker: L.Marker; sig: string }>
    monsters: Map<number, { marker: L.Marker; sig: string }>
    items: Map<number, { marker: L.Marker; sig: string }>
    booths: Map<number, { marker: L.Marker; sig: string }>
  }>({
    players: new Map(),
    monsters: new Map(),
    items: new Map(),
    booths: new Map(),
  })

  const meta: IsoMeta = useMemo(() => {
    if (map?.meta) return map.meta
    const w = map?.width ?? (live.width > 0 ? live.width : 512)
    const h = map?.height ?? (live.height > 0 ? live.height : 512)
    return fallbackMeta(w, h)
  }, [map?.meta, map?.width, map?.height, live.width, live.height])

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
  } | null>(null)

  // Initialize Leaflet map when mapId or projection changes
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

    let coversLayer: L.TileLayer | undefined
    if (map?.meta && meta.ground) {
      L.tileLayer(`/assets/${meta.ground}`, {
        tileSize: meta.tileSize,
        minZoom: 0,
        maxNativeZoom: meta.maxZoom,
        maxZoom: meta.maxZoom + 2,
        noWrap: true,
        bounds,
      }).addTo(lmap)

      if (meta.covers) {
        coversLayer = L.tileLayer(`/assets/${meta.covers}`, {
          tileSize: meta.tileSize,
          minZoom: 0,
          maxNativeZoom: meta.maxZoom,
          maxZoom: meta.maxZoom + 2,
          noWrap: true,
          bounds,
          zIndex: 250,
        })
      }
    } else if (map?.minimap && assetUrl(map.minimap)) {
      L.imageOverlay(assetUrl(map.minimap)!, bounds).addTo(lmap)
    }

    const mapW = map?.width ?? meta.mapSize.width
    const mapH = map?.height ?? meta.mapSize.height
    if (!map?.meta) {
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
          fillOpacity: map?.minimap ? 0.08 : 0.75,
          interactive: false,
        },
      ).addTo(lmap)
    }

    const groups = {
      covers: coversLayer,
      portals: L.layerGroup(),
      npcs: L.layerGroup(),
      monsters: L.layerGroup(),
      groundItems: L.layerGroup(),
      booths: L.layerGroup(),
      players: L.layerGroup(),
    }
    layerGroupsRef.current = groups
    markersRef.current = {
      players: new Map(),
      monsters: new Map(),
      items: new Map(),
      booths: new Map(),
    }

    if (map?.portal) {
      lmap.setView(cellToLatLng(map.portal.x, map.portal.y, meta), Math.max(1, meta.maxZoom - 2), { animate: false })
    } else {
      lmap.fitBounds(bounds, { padding: [24, 24], animate: false })
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
      })
    }
    const handleMouseOut = () => setCursor(null)

    lmap.on('mousemove', handleMouseMove)
    lmap.on('mouseout', handleMouseOut)

    return () => {
      lmap.off('mousemove', handleMouseMove)
      lmap.off('mouseout', handleMouseOut)
      lmap.remove()
      leafletRef.current = null
      layerGroupsRef.current = null
      markersRef.current = {
        players: new Map(),
        monsters: new Map(),
        items: new Map(),
        booths: new Map(),
      }
    }
    // Re-initialize only when mapId or tile projection changes
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mapId, meta])

  // Static context layers (portals & NPCs)
  useEffect(() => {
    const groups = layerGroupsRef.current
    if (!groups) return
    groups.portals.clearLayers()
    groups.npcs.clearLayers()
    if (!map) return

    for (const p of map.portals) {
      if (p.x == null || p.y == null) continue
      const diamond = L.polygon(cellDiamondLatLng(p.x, p.y, meta), {
        color: '#06b6d4',
        weight: 1.5,
        fillColor: '#22d3ee',
        fillOpacity: 0.3,
      })
      diamond.bindTooltip(`Portal #${p.index} → ${p.toMapName ?? (p.toMap != null ? `#${p.toMap}` : '?')}`, {
        direction: 'top',
      })
      diamond.addTo(groups.portals)
    }

    for (const npc of map.npcs) {
      if (npc.isGoal) continue
      const pos = cellToLatLng(npc.x, npc.y, meta)
      const dot = L.circleMarker(pos, {
        radius: 4,
        color: '#3b82f6',
        fillColor: '#60a5fa',
        fillOpacity: 0.75,
        weight: 1.5,
      })
      dot.bindTooltip(`${npc.name ?? 'NPC'} #${npc.id} (${npc.x}, ${npc.y})`, { direction: 'top' })
      dot.addTo(groups.npcs)
    }
  }, [map, meta])

  // Walkability canvas layer
  useEffect(() => {
    const lmap = leafletRef.current
    const groups = layerGroupsRef.current
    if (!lmap || !groups) return

    if (groups.walkability) {
      lmap.removeLayer(groups.walkability)
      groups.walkability = undefined
    }
    if (decodedGrid) {
      groups.walkability = new MapGridCanvasLayer(meta, decodedGrid, 'walkability', { zIndex: 200 })
      if (layers.walkability) groups.walkability.addTo(lmap)
    }
  }, [decodedGrid, meta, layers.walkability])

  // Sync layer visibility toggles
  useEffect(() => {
    const lmap = leafletRef.current
    const groups = layerGroupsRef.current
    if (!lmap || !groups) return

    const sync = (layer: L.Layer | undefined, visible: boolean) => {
      if (!layer) return
      if (visible && !lmap.hasLayer(layer)) layer.addTo(lmap)
      else if (!visible && lmap.hasLayer(layer)) lmap.removeLayer(layer)
    }

    sync(groups.covers, layers.covers)
    sync(groups.walkability, layers.walkability)
    sync(groups.portals, layers.portals)
    sync(groups.npcs, layers.npcs)
    sync(groups.monsters, layers.monsters)
    sync(groups.groundItems, layers.groundItems)
    sync(groups.booths, layers.booths)
    sync(groups.players, layers.players)
  }, [layers])

  // Reconcile live entity markers in-place for smooth 1s updates
  useEffect(() => {
    const groups = layerGroupsRef.current
    if (!groups) return
    const cache = markersRef.current

    // 1. Players
    const activePlayers = new Set<number>()
    for (const p of Object.values(live.players)) {
      activePlayers.add(p.characterID)
      const isFollowed = followedCharacterId === p.characterID
      const isSelected = selected?.kind === 'player' && selected.id === p.characterID
      const sig = `${p.name}|${p.level}|${p.nameColor}|${p.dead}|${p.health}|${p.maxHealth}|${isFollowed}|${isSelected}`
      const pos = cellToLatLng(p.x, p.y, meta)
      const existing = cache.players.get(p.characterID)
      if (existing) {
        existing.marker.setLatLng(pos)
        if (existing.sig !== sig) {
          existing.marker.setIcon(makePlayerIcon(p, isFollowed, isSelected))
          existing.marker.setZIndexOffset(isFollowed ? 2000 : isSelected ? 1500 : 1000)
          existing.sig = sig
        }
      } else {
        const marker = L.marker(pos, {
          icon: makePlayerIcon(p, isFollowed, isSelected),
          zIndexOffset: isFollowed ? 2000 : isSelected ? 1500 : 1000,
        })
        marker.on('click', () => onSelectRef.current({ kind: 'player', id: p.characterID }))
        marker.addTo(groups.players)
        cache.players.set(p.characterID, { marker, sig })
      }
    }
    for (const [id, entry] of cache.players) {
      if (!activePlayers.has(id)) {
        groups.players.removeLayer(entry.marker)
        cache.players.delete(id)
      }
    }

    // 2. Monsters
    const activeMonsters = new Set<number>()
    for (const m of Object.values(live.monsters)) {
      if (!m.alive && !layers.deadMonsters) continue
      activeMonsters.add(m.id)
      const isSelected = selected?.kind === 'monster' && selected.id === m.id
      const sig = `${m.alive}|${m.health}|${m.maxHealth}|${m.guard}|${isSelected}`
      const pos = cellToLatLng(m.x, m.y, meta)
      const existing = cache.monsters.get(m.id)
      if (existing) {
        existing.marker.setLatLng(pos)
        if (existing.sig !== sig) {
          existing.marker.setIcon(makeMonsterIcon(m, isSelected))
          existing.marker.setTooltipContent(
            `${m.name} Lv.${m.level} (${m.health}/${m.maxHealth}) (${m.x}, ${m.y})`,
          )
          existing.sig = sig
        }
      } else {
        const marker = L.marker(pos, {
          icon: makeMonsterIcon(m, isSelected),
          zIndexOffset: m.guard ? 350 : 300,
        })
        marker.bindTooltip(`${m.name} Lv.${m.level} (${m.health}/${m.maxHealth}) (${m.x}, ${m.y})`, {
          direction: 'top',
        })
        marker.on('click', () => onSelectRef.current({ kind: 'monster', id: m.id }))
        marker.addTo(groups.monsters)
        cache.monsters.set(m.id, { marker, sig })
      }
    }
    for (const [id, entry] of cache.monsters) {
      if (!activeMonsters.has(id)) {
        groups.monsters.removeLayer(entry.marker)
        cache.monsters.delete(id)
      }
    }

    // 3. Ground items
    const activeItems = new Set<number>()
    for (const item of Object.values(live.groundItems)) {
      activeItems.add(item.id)
      const isSelected = selected?.kind === 'item' && selected.id === item.id
      const sig = `${item.silver}|${item.itemName ?? ''}|${item.icon ?? ''}|${isSelected}`
      const pos = cellToLatLng(item.x, item.y, meta)
      const label =
        item.silver > 0
          ? `${item.silver.toLocaleString()} Silver`
          : item.itemName ?? `#${item.typeId}`
      const existing = cache.items.get(item.id)
      if (existing) {
        existing.marker.setLatLng(pos)
        if (existing.sig !== sig) {
          existing.marker.setIcon(makeGroundItemIcon(item, isSelected))
          existing.marker.setTooltipContent(`${label} (${item.x}, ${item.y})`)
          existing.sig = sig
        }
      } else {
        const marker = L.marker(pos, {
          icon: makeGroundItemIcon(item, isSelected),
          zIndexOffset: 500,
        })
        marker.bindTooltip(`${label} (${item.x}, ${item.y})`, { direction: 'top' })
        marker.on('click', () => onSelectRef.current({ kind: 'item', id: item.id }))
        marker.addTo(groups.groundItems)
        cache.items.set(item.id, { marker, sig })
      }
    }
    for (const [id, entry] of cache.items) {
      if (!activeItems.has(id)) {
        groups.groundItems.removeLayer(entry.marker)
        cache.items.delete(id)
      }
    }

    // 4. Booths
    const activeBooths = new Set<number>()
    for (const booth of Object.values(live.booths)) {
      activeBooths.add(booth.id)
      const isSelected = selected?.kind === 'booth' && selected.id === booth.id
      const sig = `${booth.ownerName}|${booth.items}|${booth.hawk ?? ''}|${isSelected}`
      const pos = cellToLatLng(booth.x, booth.y, meta)
      const existing = cache.booths.get(booth.id)
      if (existing) {
        existing.marker.setLatLng(pos)
        if (existing.sig !== sig) {
          existing.marker.setIcon(makeBoothIcon(booth, isSelected))
          existing.sig = sig
        }
      } else {
        const marker = L.marker(pos, {
          icon: makeBoothIcon(booth, isSelected),
          zIndexOffset: 700,
        })
        if (booth.hawk) marker.bindTooltip(booth.hawk, { direction: 'top', offset: [0, -24] })
        marker.on('click', () => onSelectRef.current({ kind: 'booth', id: booth.id }))
        marker.addTo(groups.booths)
        cache.booths.set(booth.id, { marker, sig })
      }
    }
    for (const [id, entry] of cache.booths) {
      if (!activeBooths.has(id)) {
        groups.booths.removeLayer(entry.marker)
        cache.booths.delete(id)
      }
    }
  }, [live, layers.deadMonsters, followedCharacterId, selected, meta])

  // Focus / pan to requested cell
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
    <Box
      pos="relative"
      w="100%"
      h="100%"
      style={{
        minHeight: 520,
        background: '#0b1120',
        borderRadius: 'var(--mantine-radius-sm)',
        overflow: 'hidden',
      }}
    >
      <Box ref={containerRef} w="100%" h="100%" />

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
                {t('cursor.cell', { x: cursor.x, y: cursor.y })}
              </Text>
              {!cursor.inside && (
                <Badge size="xs" color="gray" variant="light">
                  {t('cursor.outside')}
                </Badge>
              )}
              {cursor.walkable != null && (
                <Badge size="xs" color={cursor.walkable ? 'green' : 'red'} variant="filled">
                  {cursor.walkable ? t('cursor.walkable') : t('cursor.blocked')}
                </Badge>
              )}
            </>
          ) : (
            <Text size="xs" c="gray.4">
              {map?.width && map?.height ? `${map.width} × ${map.height}` : `#${mapId}`}
            </Text>
          )}
        </Group>
      </Paper>
    </Box>
  )
}
