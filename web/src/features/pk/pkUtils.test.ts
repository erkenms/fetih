import { describe, expect, it } from 'vitest'
import {
  classKey,
  detainedStatusColor,
  durationBetween,
  pkBadgeColor,
  pkNameColor,
  timeRemaining,
} from './pkUtils'

describe('pkNameColor', () => {
  it('classifies white, red, black and flashing names like the game', () => {
    expect(pkNameColor(0)).toBe('white')
    expect(pkNameColor(29)).toBe('white')
    expect(pkNameColor(30)).toBe('red')
    expect(pkNameColor(99)).toBe('red')
    expect(pkNameColor(100)).toBe('black')
    expect(pkNameColor(0, true)).toBe('blue')
    expect(pkNameColor(120, true)).toBe('blue')
  })
})

describe('badge colors', () => {
  it('maps name colors and detained statuses to Mantine colors', () => {
    expect(pkBadgeColor('blue')).toBe('blue')
    expect(pkBadgeColor('red')).toBe('red')
    expect(pkBadgeColor('black')).toBe('dark')
    expect(pkBadgeColor('white')).toBe('gray')

    expect(detainedStatusColor('redeemable')).toBe('yellow')
    expect(detainedStatusColor('expired')).toBe('red')
    expect(detainedStatusColor('redeemed')).toBe('green')
  })
})

describe('duration helpers', () => {
  it('computes elapsed days, hours and minutes', () => {
    const start = '2026-11-24T10:00:00Z'
    const now = Date.parse('2026-11-25T13:45:00Z')
    expect(durationBetween(start, now)).toEqual({ days: 1, hours: 3, minutes: 45 })
    expect(durationBetween(null, now)).toBeNull()
  })

  it('computes remaining time and expired state', () => {
    const expires = '2026-11-30T12:00:00Z'
    expect(timeRemaining(expires, Date.parse('2026-11-29T09:30:00Z'))).toEqual({
      expired: false,
      days: 1,
      hours: 2,
      minutes: 30,
    })
    expect(timeRemaining(expires, Date.parse('2026-12-01T00:00:00Z'))).toEqual({
      expired: true,
      days: 0,
      hours: 0,
      minutes: 0,
    })
  })
})

describe('classKey', () => {
  it('maps game class ids to locale keys', () => {
    expect(classKey(15)).toBe('trojan')
    expect(classKey(45)).toBe('archer')
    expect(classKey(135)).toBe('waterTaoist')
    expect(classKey(145)).toBe('fireTaoist')
    expect(classKey(0)).toBeNull()
  })
})
