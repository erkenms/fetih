import { describe, expect, it } from 'vitest'
import { heatIntensity, heatRadius, heatTier, partitionWorldMaps, WORLD_NODES_BY_ID } from './worldMapData'

describe('worldMapData', () => {
  it('computes bounded heat intensity and radius', () => {
    expect(heatIntensity(0, 10)).toBe(0)
    expect(heatIntensity(10, 0)).toBe(0)
    expect(heatIntensity(10, 10)).toBe(1)
    expect(heatIntensity(25, 100)).toBe(0.5)

    expect(heatRadius(0, 10)).toBe(0)
    expect(heatRadius(10, 10)).toBe(64)
    expect(heatRadius(1, 100)).toBeGreaterThan(24)
  })

  it('classifies heat tiers relative to the busiest map', () => {
    expect(heatTier(0, 50)).toBe('quiet')
    expect(heatTier(5, 50)).toBe('warm')
    expect(heatTier(20, 50)).toBe('busy')
    expect(heatTier(40, 50)).toBe('crowded')
  })

  it('partitions placed world maps from unplaced dungeon/event maps', () => {
    expect(WORLD_NODES_BY_ID.has(1002)).toBe(true)
    const { onWorld, other } = partitionWorldMaps([
      { mapId: 1002, name: 'CentralPlain', players: 30, share: 0.6 },
      { mapId: 1011, name: 'Forest', players: 12, share: 0.24 },
      { mapId: 8822, name: 'FiendLairFloor1', players: 8, share: 0.16 },
    ])

    expect(onWorld.map((x) => x.entry.mapId)).toEqual([1002, 1011])
    expect(other.map((x) => x.mapId)).toEqual([8822])
  })
})
