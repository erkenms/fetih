import { describe, expect, it } from 'vitest'
import { fallbackMeta } from '../../lib/iso'
import { frameAt, isTerrainRole, terrainNpcSprites, type TerrainNpcCatalog } from './sceneSprites'

describe('frameAt', () => {
  it('loops the frames every interval', () => {
    expect(frameAt(0, 200, 3)).toBe(0)
    expect(frameAt(199, 200, 3)).toBe(0)
    expect(frameAt(200, 200, 3)).toBe(1)
    expect(frameAt(600, 200, 3)).toBe(0)
  })

  it('stays on the first frame without an interval or a second frame', () => {
    expect(frameAt(5000, 0, 3)).toBe(0)
    expect(frameAt(5000, 100, 1)).toBe(0)
  })
})

describe('terrain NPCs', () => {
  const catalog: TerrainNpcCatalog = {
    looks: { '241': 'wall-l1', '230': 'windmilla' },
    scenes: {
      'wall-l1': [{ x: -12, y: -219, width: 256, height: 512, frames: ['terrain/mapobj/wall/wall12.png'] }],
      windmilla: [{ x: -67, y: -86, width: 128, height: 128, interval: 200, frames: ['a.png', 'b.png', 'c.png'] }],
    },
  }

  it('knows the roles the client draws as scenery', () => {
    expect(isTerrainRole(26)).toBe(true)
    expect(isTerrainRole(102)).toBe(true)
    expect(isTerrainRole(2)).toBe(false)
    expect(isTerrainRole(10)).toBe(false)
  })

  it('places a look\'s scene parts from its cell', () => {
    const meta = { ...fallbackMeta(10, 10), origin: { x: 320, y: 16 }, background: { x: 0, y: 0 } }
    // cell (5, 3) is at 32·(5 − 3) + 320, 16·(5 + 3) + 16
    expect(terrainNpcSprites(catalog, 241, 5, 3, meta)).toEqual([
      { x: 384 - 12, y: 144 - 219, width: 256, height: 512, interval: 0, frames: ['terrain/mapobj/wall/wall12.png'] },
    ])
    expect(terrainNpcSprites(catalog, 230, 0, 0, meta)[0].interval).toBe(200)
    expect(terrainNpcSprites(catalog, 999, 0, 0, meta)).toEqual([])
  })
})
