import { describe, expect, it } from 'vitest'
import { byteSize, durationParts, grafanaPanel, since } from './format'

describe('durationParts', () => {
  it('splits seconds into days, hours and minutes', () => {
    expect(durationParts(0)).toEqual({ days: 0, hours: 0, minutes: 0 })
    expect(durationParts(59)).toEqual({ days: 0, hours: 0, minutes: 0 })
    expect(durationParts(3 * 86400 + 4 * 3600 + 5 * 60 + 7)).toEqual({ days: 3, hours: 4, minutes: 5 })
  })
})

describe('byteSize', () => {
  it('picks the largest unit', () => {
    expect(byteSize(512)).toEqual({ value: 512, unit: 'B' })
    expect(byteSize(1536)).toEqual({ value: 1.5, unit: 'KB' })
    expect(byteSize(734_003_200)).toEqual({ value: 700, unit: 'MB' })
    expect(byteSize(3 * 1024 ** 4)).toEqual({ value: 3072, unit: 'GB' })
  })
})

describe('since', () => {
  const now = new Date('2026-11-24T12:00:00Z')
  it('uses the largest whole unit', () => {
    expect(since(new Date('2026-11-24T11:59:30Z'), now)).toEqual({ value: -30, unit: 'second' })
    expect(since(new Date('2026-11-24T11:15:00Z'), now)).toEqual({ value: -45, unit: 'minute' })
    expect(since(new Date('2026-11-24T07:00:00Z'), now)).toEqual({ value: -5, unit: 'hour' })
    expect(since(new Date('2026-11-21T12:00:00Z'), now)).toEqual({ value: -3, unit: 'day' })
  })
})

describe('grafanaPanel', () => {
  it('builds a solo panel URL of the overview dashboard', () => {
    const url = new URL(grafanaPanel('http://localhost:3000', 7, 'dark'))
    expect(url.pathname).toBe('/d-solo/fetih-overview/overview')
    expect(url.searchParams.get('panelId')).toBe('7')
    expect(url.searchParams.get('theme')).toBe('dark')
    expect(url.searchParams.get('from')).toBe('now-6h')
  })
})
