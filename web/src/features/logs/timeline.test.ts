import { describe, expect, it } from 'vitest'
import { levelColor, parseOptionalId, rangeWindow } from './timeline'

describe('timeline helpers', () => {
  it('computes ISO range windows from presets', () => {
    const now = new Date('2026-11-24T12:00:00.000Z')
    expect(rangeWindow('1h', now)).toEqual({
      from: '2026-11-24T11:00:00.000Z',
      to: '2026-11-24T12:00:00.000Z',
    })
    expect(rangeWindow('7d', now)).toEqual({
      from: '2026-11-17T12:00:00.000Z',
      to: '2026-11-24T12:00:00.000Z',
    })
  })

  it('maps log levels to badge colors', () => {
    expect(levelColor('error')).toBe('red')
    expect(levelColor('fatal')).toBe('red')
    expect(levelColor('warn')).toBe('yellow')
    expect(levelColor('info')).toBe('blue')
    expect(levelColor('debug')).toBe('gray')
  })

  it('parses positive integer IDs and rejects invalid input', () => {
    expect(parseOptionalId('1000001')).toBe(1000001)
    expect(parseOptionalId(' 42 ')).toBe(42)
    expect(parseOptionalId('0')).toBeUndefined()
    expect(parseOptionalId('-5')).toBeUndefined()
    expect(parseOptionalId('abc')).toBeUndefined()
    expect(parseOptionalId('')).toBeUndefined()
  })
})
