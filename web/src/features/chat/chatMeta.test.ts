import { describe, expect, it } from 'vitest'
import { channelColor, commandResultColor, prettyJson, rangeWindow } from './chatMeta'

describe('chatMeta', () => {
  it('computes ISO range windows from presets', () => {
    const now = new Date('2026-11-25T12:00:00.000Z')
    expect(rangeWindow('1h', now)).toEqual({
      from: '2026-11-25T11:00:00.000Z',
      to: '2026-11-25T12:00:00.000Z',
    })
    expect(rangeWindow('7d', now)).toEqual({
      from: '2026-11-18T12:00:00.000Z',
      to: '2026-11-25T12:00:00.000Z',
    })
  })

  it('maps chat channels and command results to colors', () => {
    expect(channelColor('Whisper')).toBe('grape')
    expect(channelColor('World')).toBe('orange')
    expect(channelColor('Command')).toBe('indigo')
    expect(channelColor('GmCommand')).toBe('red')
    expect(channelColor('Talk')).toBe('gray')

    expect(commandResultColor('ok')).toBe('green')
    expect(commandResultColor('failed')).toBe('orange')
    expect(commandResultColor('unknown')).toBe('red')
    expect(commandResultColor(null)).toBe('gray')
  })

  it('pretty-prints JSON audit payloads safely', () => {
    expect(prettyJson(null)).toBe('—')
    expect(prettyJson('{"minutes":15}')).toBe('{\n  "minutes": 15\n}')
    expect(prettyJson('plain text')).toBe('plain text')
  })
})
