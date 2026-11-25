export interface IsoMeta {
  width: number
  height: number
  tileSize: number
  maxZoom: number
  cell: { width: number; height: number }
  mapSize: { width: number; height: number }
  origin: { x: number; y: number }
  background: { x: number; y: number }
  ground?: string
  covers?: string
}

export interface PixelPoint {
  px: number
  py: number
}

export interface CellPoint {
  x: number
  y: number
}

export type LatLngTuple = [number, number]
export type LatLngBoundsTuple = [LatLngTuple, LatLngTuple]

// Synthetic projection when A2 tiles (maps/<id>/meta.json) have not been built yet.
// Covers the full isometric diamond [0..mapWidth] x [0..mapHeight] in [0..width] x [0..height].
export function fallbackMeta(mapWidth: number, mapHeight: number, tileSize = 256): IsoMeta {
  const w = Math.max(1, Math.round(mapWidth))
  const h = Math.max(1, Math.round(mapHeight))
  const width = 32 * (w + h)
  const height = 16 * (w + h) + 32
  let maxZoom = 0
  while ((tileSize << maxZoom) < Math.max(width, height))
    maxZoom++

  return {
    width,
    height,
    tileSize,
    maxZoom,
    cell: { width: 64, height: 32 },
    mapSize: { width: w, height: h },
    origin: { x: 32 * h, y: 16 },
    background: { x: 0, y: 0 },
  }
}

export function crsScale(meta: IsoMeta): number {
  return 2 ** meta.maxZoom
}

// Cell (x, y) center -> background pixel (px, py).
export function cellToPixel(x: number, y: number, meta: IsoMeta): PixelPoint {
  const halfW = meta.cell.width / 2
  const halfH = meta.cell.height / 2
  return {
    px: halfW * (x - y) + meta.origin.x - meta.background.x,
    py: halfH * (x + y) + meta.origin.y - meta.background.y,
  }
}

// Background pixel (px, py) -> floating-point cell (x, y).
export function pixelToCell(px: number, py: number, meta: IsoMeta): CellPoint {
  const halfW = meta.cell.width / 2
  const halfH = meta.cell.height / 2
  const dx = (px - (meta.origin.x - meta.background.x)) / halfW
  const dy = (py - (meta.origin.y - meta.background.y)) / halfH
  return {
    x: (dx + dy) / 2,
    y: (dy - dx) / 2,
  }
}

export function pixelToCellInt(px: number, py: number, meta: IsoMeta): CellPoint {
  const c = pixelToCell(px, py, meta)
  return { x: Math.round(c.x), y: Math.round(c.y) }
}

// Leaflet CRS.Simple maps lat = -py / 2^maxZoom, lng = px / 2^maxZoom.
export function pixelToLatLng(px: number, py: number, meta: IsoMeta): LatLngTuple {
  const scale = crsScale(meta)
  return [-py / scale, px / scale]
}

export function latLngToPixel(lat: number, lng: number, meta: IsoMeta): PixelPoint {
  const scale = crsScale(meta)
  return { px: lng * scale, py: -lat * scale }
}

export function cellToLatLng(x: number, y: number, meta: IsoMeta): LatLngTuple {
  const { px, py } = cellToPixel(x, y, meta)
  return pixelToLatLng(px, py, meta)
}

export function latLngToCell(lat: number, lng: number, meta: IsoMeta): CellPoint {
  const { px, py } = latLngToPixel(lat, lng, meta)
  return pixelToCell(px, py, meta)
}

export function latLngToCellInt(lat: number, lng: number, meta: IsoMeta): CellPoint {
  const { px, py } = latLngToPixel(lat, lng, meta)
  return pixelToCellInt(px, py, meta)
}

export function mapBoundsLatLng(meta: IsoMeta): LatLngBoundsTuple {
  const scale = crsScale(meta)
  return [
    [-meta.height / scale, 0],
    [0, meta.width / scale],
  ]
}

export function cellDiamondLatLng(x: number, y: number, meta: IsoMeta): LatLngTuple[] {
  return [
    cellToLatLng(x - 0.5, y - 0.5, meta),
    cellToLatLng(x + 0.5, y - 0.5, meta),
    cellToLatLng(x + 0.5, y + 0.5, meta),
    cellToLatLng(x - 0.5, y + 0.5, meta),
  ]
}

// Circle of radius `spread` in cell space, projected to isometric LatLng coordinates.
export function spawnCircleLatLng(x: number, y: number, spread: number, meta: IsoMeta, segments = 24): LatLngTuple[] {
  const r = Math.max(0.5, spread)
  const pts: LatLngTuple[] = []
  for (let i = 0; i < segments; i++) {
    const angle = (i / segments) * Math.PI * 2
    pts.push(cellToLatLng(x + r * Math.cos(angle), y + r * Math.sin(angle), meta))
  }
  return pts
}

export function decodeBase64Bytes(base64: string): Uint8Array {
  const binary = atob(base64)
  const bytes = new Uint8Array(binary.length)
  for (let i = 0; i < binary.length; i++)
    bytes[i] = binary.charCodeAt(i)
  return bytes
}

export function isCellWalkable(walkable: Uint8Array, width: number, height: number, x: number, y: number): boolean {
  if (x < 0 || y < 0 || x >= width || y >= height)
    return false
  const idx = y * width + x
  return ((walkable[idx >> 3] ?? 0) & (1 << (idx & 7))) !== 0
}

export function cellElevation(
  elevBytes: Uint8Array,
  width: number,
  height: number,
  x: number,
  y: number,
  minElev: number,
  maxElev: number,
): number | null {
  if (x < 0 || y < 0 || x >= width || y >= height)
    return null
  const idx = y * width + x
  const raw = elevBytes[idx] ?? 0
  if (maxElev <= minElev)
    return minElev
  return Math.round(minElev + (raw / 255) * (maxElev - minElev))
}

// Maps normalized elevation [0..255] to RGB (blue -> cyan -> green -> yellow -> red).
export function elevationColor(v: number): [number, number, number] {
  const t = Math.max(0, Math.min(255, v)) / 255
  if (t < 0.25) {
    const k = t / 0.25
    return [30, Math.round(80 + 175 * k), 220]
  }
  if (t < 0.5) {
    const k = (t - 0.25) / 0.25
    return [Math.round(30 + 20 * k), 220, Math.round(220 - 160 * k)]
  }
  if (t < 0.75) {
    const k = (t - 0.5) / 0.25
    return [Math.round(50 + 200 * k), 220, 60]
  }
  const k = (t - 0.75) / 0.25
  return [245, Math.round(220 - 170 * k), 45]
}
