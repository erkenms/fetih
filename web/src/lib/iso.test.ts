import { describe, expect, it } from 'vitest'
import {
  cellDiamondLatLng,
  cellElevation,
  cellToLatLng,
  cellToPixel,
  decodeBase64Bytes,
  elevationColor,
  fallbackMeta,
  isCellWalkable,
  latLngToCell,
  latLngToCellInt,
  latLngToPixel,
  mapBoundsLatLng,
  pixelToCell,
  pixelToCellInt,
  pixelToLatLng,
  spawnCircleLatLng,
  type IsoMeta,
} from './iso'

const sampleMeta: IsoMeta = {
  width: 32768,
  height: 16384,
  tileSize: 256,
  maxZoom: 7,
  cell: { width: 64, height: 32 },
  mapSize: { width: 1024, height: 1024 },
  origin: { x: 32768, y: 16 },
  background: { x: 16384, y: 8192 },
  ground: 'maps/tiles/newplain/ground/{z}/{x}/{y}.webp',
  covers: 'maps/tiles/newplain/covers/{z}/{x}/{y}.webp',
}

describe('iso coordinate math', () => {
  it('round-trips cell <-> pixel exactly', () => {
    const points = [
      [0, 0],
      [430, 380],
      [1023, 0],
      [0, 1023],
      [512, 512],
    ]
    for (const [x, y] of points) {
      const { px, py } = cellToPixel(x, y, sampleMeta)
      const back = pixelToCell(px, py, sampleMeta)
      expect(back.x).toBeCloseTo(x, 8)
      expect(back.y).toBeCloseTo(y, 8)
      expect(pixelToCellInt(px, py, sampleMeta)).toEqual({ x, y })
    }
  })

  it('matches the MapTiles.cs formula', () => {
    const { px, py } = cellToPixel(430, 380, sampleMeta)
    expect(px).toBe(32 * (430 - 380) + 32768 - 16384)
    expect(py).toBe(16 * (430 + 380) + 16 - 8192)
  })

  it('round-trips cell <-> Leaflet LatLng exactly', () => {
    const [lat, lng] = cellToLatLng(430, 380, sampleMeta)
    const back = latLngToCell(lat, lng, sampleMeta)
    expect(back.x).toBeCloseTo(430, 8)
    expect(back.y).toBeCloseTo(380, 8)
    expect(latLngToCellInt(lat, lng, sampleMeta)).toEqual({ x: 430, y: 380 })
  })

  it('maps background bounds to CRS.Simple tile space', () => {
    const [[south, west], [north, east]] = mapBoundsLatLng(sampleMeta)
    expect(west).toBe(0)
    expect(north).toBe(0)
    expect(east).toBe(sampleMeta.width / 2 ** sampleMeta.maxZoom)
    expect(south).toBe(-sampleMeta.height / 2 ** sampleMeta.maxZoom)

    const topLeft = pixelToLatLng(0, 0, sampleMeta)
    expect(topLeft[0]).toBe(-0)
    expect(topLeft[1]).toBe(0)
    expect(latLngToPixel(south, east, sampleMeta)).toEqual({ px: sampleMeta.width, py: sampleMeta.height })
  })

  it('builds a valid fallback projection covering all map corners', () => {
    const meta = fallbackMeta(512, 512)
    expect(meta.tileSize).toBe(256)
    expect(meta.tileSize << meta.maxZoom).toBeGreaterThanOrEqual(Math.max(meta.width, meta.height))
    for (const [x, y] of [[0, 0], [511, 0], [0, 511], [511, 511]]) {
      const { px, py } = cellToPixel(x, y, meta)
      expect(px).toBeGreaterThanOrEqual(0)
      expect(px).toBeLessThanOrEqual(meta.width)
      expect(py).toBeGreaterThanOrEqual(0)
      expect(py).toBeLessThanOrEqual(meta.height)
    }
  })

  it('builds cell diamond and spawn circle polygons', () => {
    expect(cellDiamondLatLng(10, 20, sampleMeta)).toHaveLength(4)
    expect(spawnCircleLatLng(10, 20, 15, sampleMeta, 16)).toHaveLength(16)
  })
})

describe('grid bitmap decoding', () => {
  it('decodes LSB-first walkability bitmap', () => {
    // 3x3 checkerboard: (0,0)=1, (1,0)=0, (2,0)=1, (0,1)=0, (1,1)=1, (2,1)=0, (0,2)=1, (1,2)=0, (2,2)=1
    // bits 0..7: 0b01010101 = 0x55; bit 8: 0b00000001 = 0x01
    const bytes = decodeBase64Bytes(btoa(String.fromCharCode(0x55, 0x01)))
    for (let y = 0; y < 3; y++) {
      for (let x = 0; x < 3; x++) {
        expect(isCellWalkable(bytes, 3, 3, x, y)).toBe((x + y) % 2 === 0)
      }
    }
    expect(isCellWalkable(bytes, 3, 3, -1, 0)).toBe(false)
    expect(isCellWalkable(bytes, 3, 3, 3, 0)).toBe(false)
  })

  it('decodes quantized elevation and color ramp', () => {
    const elev = new Uint8Array([0, 128, 255, 64])
    expect(cellElevation(elev, 2, 2, 0, 0, -100, 100)).toBe(-100)
    expect(cellElevation(elev, 2, 2, 0, 1, -100, 100)).toBe(100)
    expect(cellElevation(elev, 2, 2, 1, 0, -100, 100)).toBe(0)
    expect(cellElevation(elev, 2, 2, 5, 5, -100, 100)).toBeNull()

    for (const v of [0, 64, 128, 192, 255]) {
      const [r, g, b] = elevationColor(v)
      expect(r).toBeGreaterThanOrEqual(0)
      expect(r).toBeLessThanOrEqual(255)
      expect(g).toBeGreaterThanOrEqual(0)
      expect(g).toBeLessThanOrEqual(255)
      expect(b).toBeGreaterThanOrEqual(0)
      expect(b).toBeLessThanOrEqual(255)
    }
  })
})
