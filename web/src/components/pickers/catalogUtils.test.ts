import { describe, expect, it } from 'vitest'
import { assetUrl, combineLookface, formatMapLabel, formatSpellPower, splitLookface } from './catalogUtils'

describe('assetUrl', () => {
  it('returns null for empty paths', () => {
    expect(assetUrl(null)).toBeNull()
    expect(assetUrl(undefined)).toBeNull()
    expect(assetUrl('   ')).toBeNull()
  })

  it('prefixes /assets/ once', () => {
    expect(assetUrl('icons/magic/1000.png')).toBe('/assets/icons/magic/1000.png')
    expect(assetUrl('/icons/magic/1000.png')).toBe('/assets/icons/magic/1000.png')
    expect(assetUrl('/assets/icons/magic/1000.png')).toBe('/assets/icons/magic/1000.png')
  })
})

describe('formatSpellPower', () => {
  it('formats flat and percent power', () => {
    expect(formatSpellPower(250)).toBe('250')
    expect(formatSpellPower(30110)).toBe('110%')
    expect(formatSpellPower(30000)).toBe('0%')
  })
})

describe('formatMapLabel', () => {
  it('prefers client name, then serverName, then id', () => {
    expect(formatMapLabel({ id: 1002, name: 'Twin City', serverName: 'twin_city' })).toBe('Twin City (#1002)')
    expect(formatMapLabel({ id: 1002, name: null, serverName: 'twin_city' })).toBe('twin_city (#1002)')
    expect(formatMapLabel({ id: 1002 })).toBe('#1002')
  })
})

describe('lookface helpers', () => {
  it('splits and combines look and direction', () => {
    expect(splitLookface(5247)).toEqual({ look: 524, direction: 7 })
    expect(splitLookface(20)).toEqual({ look: 2, direction: 0 })
    expect(combineLookface(524, 7)).toBe(5247)
    expect(combineLookface(524, 9)).toBe(5241)
  })
})
