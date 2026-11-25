import { describe, expect, it } from 'vitest'
import { formatClock, milestoneLabels, motdLines, remainingSeconds } from './countdown'

describe('server countdown helpers', () => {
  it('computes remaining seconds safely', () => {
    const now = Date.parse('2026-11-25T12:00:00Z')
    expect(remainingSeconds('2026-11-25T12:01:30Z', now)).toBe(90)
    expect(remainingSeconds('2026-11-25T11:59:00Z', now)).toBe(0)
    expect(remainingSeconds(null, now)).toBe(0)
  })

  it('formats mm:ss and hh:mm:ss', () => {
    expect(formatClock(0)).toBe('00:00')
    expect(formatClock(65)).toBe('01:05')
    expect(formatClock(3661)).toBe('1:01:01')
  })

  it('summarizes broadcast milestones cleanly', () => {
    expect(milestoneLabels(600)).toEqual(['10m', '5m', '3m', '2m', '1m', '30s', '10s', '5–1s'])
    expect(milestoneLabels(60)).toEqual(['1m', '30s', '10s', '5–1s'])
  })

  it('splits MOTD into non-empty trimmed lines', () => {
    expect(motdLines('  Line 1 \n\n  Line 2  \r\n')).toEqual(['Line 1', 'Line 2'])
  })
})
