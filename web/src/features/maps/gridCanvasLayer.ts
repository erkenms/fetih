import L from 'leaflet'
import { elevationColor, isCellWalkable, pixelToCellInt, type IsoMeta } from '../../lib/iso'

export interface DecodedGrid {
  width: number
  height: number
  walkable: Uint8Array
  elevation: Uint8Array
  minElevation: number
  maxElevation: number
}

export type GridOverlayMode = 'walkability' | 'elevation'

// Canvas GridLayer that samples the isometric cell under each tile pixel and paints
// walkability or elevation directly into 256x256 tiles.
export class MapGridCanvasLayer extends L.GridLayer {
  private meta: IsoMeta
  private grid: DecodedGrid
  private mode: GridOverlayMode

  constructor(meta: IsoMeta, grid: DecodedGrid, mode: GridOverlayMode, options?: L.GridLayerOptions) {
    super({ tileSize: meta.tileSize, noWrap: true, ...options })
    this.meta = meta
    this.grid = grid
    this.mode = mode
  }

  createTile(coords: L.Coords): HTMLElement {
    const size = this.getTileSize()
    const canvas = document.createElement('canvas')
    canvas.width = size.x
    canvas.height = size.y
    const ctx = canvas.getContext('2d')
    if (!ctx) return canvas

    const scale = 2 ** (this.meta.maxZoom - coords.z)
    const originPx = coords.x * size.x * scale
    const originPy = coords.y * size.y * scale

    // Quick reject tiles completely outside the background bounds
    if (
      originPx >= this.meta.width ||
      originPy >= this.meta.height ||
      originPx + size.x * scale <= 0 ||
      originPy + size.y * scale <= 0
    ) {
      return canvas
    }

    const step = scale > 4 ? 2 : 1
    const img = ctx.createImageData(size.x, size.y)
    const data = img.data
    const { width, height, walkable, elevation } = this.grid

    for (let ty = 0; ty < size.y; ty += step) {
      const py = originPy + (ty + step * 0.5) * scale
      for (let tx = 0; tx < size.x; tx += step) {
        const px = originPx + (tx + step * 0.5) * scale
        const cell = pixelToCellInt(px, py, this.meta)
        if (cell.x < 0 || cell.y < 0 || cell.x >= width || cell.y >= height)
          continue

        let r: number
        let g: number
        let b: number
        let a: number

        if (this.mode === 'walkability') {
          if (isCellWalkable(walkable, width, height, cell.x, cell.y)) {
            r = 34
            g = 197
            b = 94
            a = 45
          } else {
            r = 239
            g = 68
            b = 68
            a = 115
          }
        } else {
          const val = elevation[cell.y * width + cell.x] ?? 0
          ;[r, g, b] = elevationColor(val)
          a = 135
        }

        for (let dy = 0; dy < step && ty + dy < size.y; dy++) {
          for (let dx = 0; dx < step && tx + dx < size.x; dx++) {
            const off = ((ty + dy) * size.x + (tx + dx)) * 4
            data[off] = r
            data[off + 1] = g
            data[off + 2] = b
            data[off + 3] = a
          }
        }
      }
    }

    ctx.putImageData(img, 0, 0)
    return canvas
  }
}
