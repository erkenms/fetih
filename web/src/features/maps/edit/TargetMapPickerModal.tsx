import { Badge, Box, Button, Group, Loader, Modal, Paper, Stack, Switch, Text } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useQuery } from '@tanstack/react-query'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap } from '../../../api/client'
import { assetUrl, MapPicker } from '../../../components/pickers'
import {
  cellDiamondLatLng,
  cellToLatLng,
  decodeBase64Bytes,
  fallbackMeta,
  isCellWalkable,
  latLngToCellInt,
  mapBoundsLatLng,
  type IsoMeta,
} from '../../../lib/iso'
import { MapGridCanvasLayer, type DecodedGrid } from '../gridCanvasLayer'

interface TargetMapPickerModalProps {
  opened: boolean
  initialMapId: number
  initialX: number | null
  initialY: number | null
  onClose: () => void
  onConfirm: (toMap: number, toMapName: string | null, toX: number, toY: number) => void
}

export function TargetMapPickerModal({
  opened,
  initialMapId,
  initialX,
  initialY,
  onClose,
  onConfirm,
}: TargetMapPickerModalProps) {
  const { t } = useTranslation('mapEdit')
  const { t: tMaps } = useTranslation('maps')
  const [targetMapId, setTargetMapId] = useState<number>(initialMapId)
  const [picked, setPicked] = useState<{ x: number; y: number } | null>(
    initialX != null && initialY != null ? { x: initialX, y: initialY } : null,
  )
  const [showWalkability, setShowWalkability] = useState(true)
  const [cursor, setCursor] = useState<{ x: number; y: number; walkable: boolean | null } | null>(null)

  useEffect(() => {
    if (opened) {
      setTargetMapId(initialMapId)
      setPicked(initialX != null && initialY != null ? { x: initialX, y: initialY } : null)
    }
  }, [opened, initialMapId, initialX, initialY])

  const detail = useQuery({
    queryKey: ['maps', 'detail', targetMapId],
    queryFn: () => unwrap(api.GET('/api/maps/{id}', { params: { path: { id: targetMapId } } })),
    enabled: opened && targetMapId > 0,
  })

  const gridQuery = useQuery({
    queryKey: ['maps', 'grid', targetMapId],
    queryFn: () => unwrap(api.GET('/api/maps/{id}/grid', { params: { path: { id: targetMapId } } })),
    enabled: opened && Boolean(detail.data?.hasGrid),
  })

  const map = detail.data
  const meta: IsoMeta | null = useMemo(() => {
    if (!map) return null
    return map.meta ?? fallbackMeta(map.width ?? 512, map.height ?? 512)
  }, [map])

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

  const decodedGridRef = useRef(decodedGrid)
  decodedGridRef.current = decodedGrid

  const containerRef = useRef<HTMLDivElement | null>(null)
  const leafletRef = useRef<L.Map | null>(null)
  const markerLayerRef = useRef<L.LayerGroup | null>(null)
  const walkLayerRef = useRef<L.GridLayer | null>(null)

  useEffect(() => {
    const el = containerRef.current
    if (!opened || !el || !map || !meta) return

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
    } else if (map.minimap && assetUrl(map.minimap)) {
      L.imageOverlay(assetUrl(map.minimap)!, bounds).addTo(lmap)
    } else {
      const w = map.width ?? meta.mapSize.width
      const h = map.height ?? meta.mapSize.height
      L.polygon(
        [cellToLatLng(0, 0, meta), cellToLatLng(w, 0, meta), cellToLatLng(w, h, meta), cellToLatLng(0, h, meta)],
        { color: '#475569', weight: 1.5, fillColor: '#0f172a', fillOpacity: 0.75, interactive: false },
      ).addTo(lmap)
    }

    const markerLayer = L.layerGroup().addTo(lmap)
    markerLayerRef.current = markerLayer

    if (picked) {
      lmap.setView(cellToLatLng(picked.x, picked.y, meta), Math.max(1, meta.maxZoom - 2), { animate: false })
    } else if (map.portal) {
      lmap.setView(cellToLatLng(map.portal.x, map.portal.y, meta), Math.max(1, meta.maxZoom - 2), { animate: false })
    } else {
      lmap.fitBounds(bounds, { padding: [20, 20], animate: false })
    }

    const mapW = map.width ?? meta.mapSize.width
    const mapH = map.height ?? meta.mapSize.height

    const onMove = (e: L.LeafletMouseEvent) => {
      const c = latLngToCellInt(e.latlng.lat, e.latlng.lng, meta)
      const g = decodedGridRef.current
      const w = g?.width ?? mapW
      const h = g?.height ?? mapH
      const inside = c.x >= 0 && c.y >= 0 && c.x < w && c.y < h
      setCursor({
        x: c.x,
        y: c.y,
        walkable: g && inside ? isCellWalkable(g.walkable, g.width, g.height, c.x, c.y) : null,
      })
    }

    const onClick = (e: L.LeafletMouseEvent) => {
      const c = latLngToCellInt(e.latlng.lat, e.latlng.lng, meta)
      const g = decodedGridRef.current
      const w = g?.width ?? mapW
      const h = g?.height ?? mapH
      if (c.x < 0 || c.y < 0 || c.x >= w || c.y >= h) {
        notifications.show({ color: 'red', message: t('errors.outOfBounds', { x: c.x, y: c.y }) })
        return
      }
      if (g && !isCellWalkable(g.walkable, g.width, g.height, c.x, c.y)) {
        notifications.show({ color: 'red', message: t('errors.unwalkableCell', { x: c.x, y: c.y }) })
        return
      }
      setPicked({ x: c.x, y: c.y })
    }

    lmap.on('mousemove', onMove)
    lmap.on('click', onClick)

    // Ensure Leaflet measures the modal container after open transition
    const timer = window.setTimeout(() => lmap.invalidateSize(), 120)

    return () => {
      window.clearTimeout(timer)
      lmap.off('mousemove', onMove)
      lmap.off('click', onClick)
      lmap.remove()
      leafletRef.current = null
      markerLayerRef.current = null
      walkLayerRef.current = null
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [opened, map?.id, meta])

  // Walkability overlay inside modal
  useEffect(() => {
    const lmap = leafletRef.current
    if (!lmap || !meta) return
    if (walkLayerRef.current) {
      lmap.removeLayer(walkLayerRef.current)
      walkLayerRef.current = null
    }
    if (decodedGrid && showWalkability) {
      walkLayerRef.current = new MapGridCanvasLayer(meta, decodedGrid, 'walkability', { zIndex: 200 })
      walkLayerRef.current.addTo(lmap)
    }
  }, [decodedGrid, meta, showWalkability])

  // Render picked cell marker
  useEffect(() => {
    const layer = markerLayerRef.current
    if (!layer || !meta) return
    layer.clearLayers()
    if (!picked) return
    L.polygon(cellDiamondLatLng(picked.x, picked.y, meta), {
      color: '#10b981',
      weight: 2.5,
      fillColor: '#34d399',
      fillOpacity: 0.55,
      interactive: false,
    }).addTo(layer)
    L.circleMarker(cellToLatLng(picked.x, picked.y, meta), {
      radius: 6,
      color: '#ffffff',
      fillColor: '#10b981',
      fillOpacity: 1,
      weight: 2,
      interactive: false,
    }).addTo(layer)
  }, [picked, meta])

  const mapLabel = map ? (map.name ?? map.serverName ?? `#${map.id}`) : `#${targetMapId}`

  return (
    <Modal opened={opened} onClose={onClose} title={t('targetPicker.title')} size="xl">
      <Stack gap="sm">
        <Group align="flex-end" justify="space-between" wrap="wrap">
          <Box style={{ flex: 1, minWidth: 260 }}>
            <MapPicker
              label={t('targetPicker.selectMap')}
              value={targetMapId}
              clearable={false}
              onChange={(id) => {
                if (id != null) {
                  setTargetMapId(id)
                  setPicked(null)
                }
              }}
            />
          </Box>
          <Switch
            label={t('toolbar.walkability')}
            checked={showWalkability}
            disabled={!map?.hasGrid}
            onChange={(e) => setShowWalkability(e.currentTarget.checked)}
          />
        </Group>

        <Text size="xs" c="dimmed">
          {t('targetPicker.hint')}
        </Text>

        <Box
          pos="relative"
          h={440}
          style={{ background: '#0b1120', borderRadius: 'var(--mantine-radius-sm)', overflow: 'hidden', cursor: 'crosshair' }}
        >
          {detail.isLoading ? (
            <Group justify="center" align="center" h="100%">
              <Loader />
            </Group>
          ) : (
            <Box ref={containerRef} w="100%" h="100%" style={{ cursor: 'crosshair' }} />
          )}

          {cursor && (
            <Paper
              pos="absolute"
              bottom={8}
              left={8}
              px="xs"
              py={4}
              radius="sm"
              style={{
                zIndex: 500,
                background: 'rgba(15, 23, 42, 0.88)',
                color: '#f8fafc',
                pointerEvents: 'none',
              }}
            >
              <Group gap="xs">
                <Text size="xs" ff="monospace">
                  {tMaps('viewer.cursor.cell', { x: cursor.x, y: cursor.y })}
                </Text>
                {cursor.walkable != null && (
                  <Badge size="xs" color={cursor.walkable ? 'green' : 'red'}>
                    {cursor.walkable ? tMaps('viewer.cursor.walkable') : tMaps('viewer.cursor.blocked')}
                  </Badge>
                )}
              </Group>
            </Paper>
          )}
        </Box>

        <Group justify="space-between">
          <Text size="sm" fw={500}>
            {picked
              ? t('targetPicker.selected', { map: mapLabel, x: picked.x, y: picked.y })
              : t('targetPicker.noneSelected')}
          </Text>
          <Group gap="xs">
            <Button variant="default" onClick={onClose}>
              {t('targetPicker.cancel')}
            </Button>
            <Button
              disabled={!picked}
              onClick={() => {
                if (!picked) return
                onConfirm(targetMapId, map?.name ?? map?.serverName ?? null, picked.x, picked.y)
              }}
            >
              {t('targetPicker.confirm')}
            </Button>
          </Group>
        </Group>
      </Stack>
    </Modal>
  )
}
