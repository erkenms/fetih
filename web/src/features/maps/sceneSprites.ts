import { cellToPixel, type IsoMeta } from '../../lib/iso'

export interface SceneSprite {
  x: number
  y: number
  width: number
  height: number
  interval: number
  frames: string[]
}

export interface TerrainNpcCatalog {
  looks: Record<string, string>
  scenes: Record<string, Partial<SceneSprite>[]>
}

// NPC roles the client draws from ini/terrainnpc.ini instead of a 3D look
const TERRAIN_ROLES = new Set([25, 26, 39, 41, 42, 102, 103])

export function isTerrainRole(role: number): boolean {
  return TERRAIN_ROLES.has(role)
}

// The frame a looping animation shows at `now` ms
export function frameAt(now: number, interval: number, frames: number): number {
  if (frames <= 1 || interval <= 0) return 0
  return Math.floor(now / interval) % frames
}

// The parts of a terrain NPC's scene at cell (x, y), placed in background pixels
export function terrainNpcSprites(
  catalog: TerrainNpcCatalog,
  lookface: number,
  x: number,
  y: number,
  meta: IsoMeta,
): SceneSprite[] {
  const scene = catalog.looks[String(lookface)]
  const parts = scene ? catalog.scenes[scene] : undefined
  if (!parts) return []
  const cell = cellToPixel(x, y, meta)
  return parts
    .filter((p) => p.frames && p.frames.length > 0)
    .map((p) => ({
      x: cell.px + (p.x ?? 0),
      y: cell.py + (p.y ?? 0),
      width: p.width ?? 0,
      height: p.height ?? 0,
      interval: p.interval ?? 0,
      frames: p.frames!,
    }))
}
