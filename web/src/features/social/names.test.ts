import { describe, expect, it } from 'vitest'
import { classKey, familyRankKey, guildRankKey } from './names'

describe('classKey', () => {
  it('groups classes by their base', () => {
    expect(classKey(15)).toBe('trojan')
    expect(classKey(21)).toBe('warrior')
    expect(classKey(45)).toBe('archer')
    expect(classKey(55)).toBe('ninja')
    expect(classKey(60)).toBe('monk')
    expect(classKey(75)).toBe('pirate')
    expect(classKey(100)).toBe('taoist')
    expect(classKey(101)).toBe('taoist')
    expect(classKey(135)).toBe('waterTaoist')
    expect(classKey(142)).toBe('fireTaoist')
    expect(classKey(0)).toBeNull()
  })
})

describe('ranks', () => {
  it('names guild and family ranks', () => {
    expect(guildRankKey(1000)).toBe('leader')
    expect(guildRankKey(990)).toBe('deputy')
    expect(guildRankKey(200)).toBe('member')
    expect(familyRankKey(100)).toBe('leader')
    expect(familyRankKey(11)).toBe('spouse')
    expect(familyRankKey(10)).toBe('member')
  })
})
