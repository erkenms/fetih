import { describe, expect, it } from 'vitest'
import { clanRankKey, classKey, formatNumber, formatUnixDate, guildRankKey, playerAvatarUrl, subClassKey } from './format'

describe('classKey', () => {
  it('maps Conquer profession IDs to class keys', () => {
    expect(classKey(10)).toBe('trojan')
    expect(classKey(15)).toBe('trojan')
    expect(classKey(25)).toBe('warrior')
    expect(classKey(45)).toBe('archer')
    expect(classKey(55)).toBe('ninja')
    expect(classKey(65)).toBe('monk')
    expect(classKey(75)).toBe('pirate')
    expect(classKey(100)).toBe('taoist')
    expect(classKey(135)).toBe('waterTaoist')
    expect(classKey(145)).toBe('fireTaoist')
    expect(classKey(0)).toBe('unknown')
  })
})

describe('rank and subclass keys', () => {
  it('maps guild ranks', () => {
    expect(guildRankKey(1000)).toBe('leader')
    expect(guildRankKey(990)).toBe('deputyLeader')
    expect(guildRankKey(200)).toBe('member')
    expect(guildRankKey(0)).toBe('none')
  })

  it('maps clan ranks', () => {
    expect(clanRankKey(100)).toBe('leader')
    expect(clanRankKey(11)).toBe('spouse')
    expect(clanRankKey(10)).toBe('member')
    expect(clanRankKey(0)).toBe('none')
  })

  it('maps subclass IDs', () => {
    expect(subClassKey(1)).toBe('martialArtist')
    expect(subClassKey(9)).toBe('wrangler')
    expect(subClassKey(99)).toBe('unknown')
  })
})

describe('formatting helpers', () => {
  it('builds player avatar path', () => {
    expect(playerAvatarUrl(42)).toBe('/assets/faces/player/42.png')
  })

  it('formats numbers and unix dates', () => {
    expect(formatNumber(123456, 'en')).toBe('123,456')
    expect(formatUnixDate(0, 'en')).toBe('—')
  })
})
