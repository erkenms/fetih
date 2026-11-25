import { describe, expect, it } from 'vitest'
import {
  cellToNormalized,
  expBallsForLevel,
  nextGoalId,
  nextLevelNumber,
  normalizedToCell,
  stringToTextLines,
  textLinesToString,
} from './dataUtils'

describe('dataUtils', () => {
  it('computes EXP balls per level from upLevTime', () => {
    expect(expBallsForLevel(0)).toBe(0)
    expect(expBallsForLevel(600)).toBe(1)
    expect(expBallsForLevel(300)).toBe(0.5)
  })

  it('computes next level and goal IDs', () => {
    expect(nextLevelNumber([])).toBe(1)
    expect(nextLevelNumber([{ level: 120, experience: 1, upLevTime: 1 }])).toBe(121)
    expect(nextLevelNumber([{ level: 255, experience: 1, upLevTime: 1 }])).toBe(255)

    expect(nextGoalId([])).toBe(100011)
    expect(nextGoalId([{ id: 100021, lookface: 427, role: 21, level: 20, life: 10000, map: 1039, x: 1, y: 1 }])).toBe(
      100023,
    )
  })

  it('converts between normalized map positions and cell coordinates', () => {
    expect(normalizedToCell(0, 0, 1000, 1000)).toEqual({ x: 0, y: 0 })
    expect(normalizedToCell(1, 1, 1000, 1000)).toEqual({ x: 999, y: 999 })
    expect(normalizedToCell(0.5, 0.25, 1000, 800)).toEqual({ x: 500, y: 200 })

    const norm = cellToNormalized(499, 199, 1000, 800)
    expect(norm.normX).toBeCloseTo(0.4995, 4)
    expect(norm.normY).toBeCloseTo(0.249375, 4)
  })

  it('round-trips dialog text lines', () => {
    expect(textLinesToString(['Line 1', 'Line 2'])).toBe('Line 1\nLine 2')
    expect(stringToTextLines('Line 1\nLine 2')).toEqual(['Line 1', 'Line 2'])
    expect(stringToTextLines('')).toEqual([])
  })
})
