import { describe, expect, it } from 'vitest'
import { classKey, nobilityColors } from './gameMeta'

describe('classKey', () => {
  it('maps exact class ID ranges including monk and pirate', () => {
    expect(classKey(15)).toBe('trojan')
    expect(classKey(20)).toBe('warrior')
    expect(classKey(45)).toBe('archer')
    expect(classKey(55)).toBe('ninja')
    expect(classKey(60)).toBe('monk')
    expect(classKey(75)).toBe('pirate')
    expect(classKey(100)).toBe('taoist')
    expect(classKey(101)).toBe('taoist')
    expect(classKey(132)).toBe('waterTaoist')
    expect(classKey(145)).toBe('fireTaoist')
    expect(classKey(130)).toBe('unknown')
    expect(classKey(0)).toBe('unknown')
  })
})

describe('nobilityColors', () => {
  it('maps all six nobility ranks', () => {
    expect(nobilityColors.King).toBe('yellow')
    expect(nobilityColors.Earl).toBe('cyan')
    expect(nobilityColors.Baron).toBe('teal')
  })
})
