import { describe, expect, it } from 'vitest'
import { announcementState, nextRunAt } from './schedule'

describe('announcementState', () => {
  const now = new Date('2026-11-24T12:00:00Z')

  it('returns disabled when enabled is false', () => {
    expect(announcementState({ enabled: false, intervalMinutes: 10 }, now)).toBe('disabled')
  })

  it('returns scheduled before startAt', () => {
    expect(
      announcementState({ enabled: true, intervalMinutes: 10, startAt: '2026-11-24T13:00:00Z' }, now),
    ).toBe('scheduled')
  })

  it('returns expired at or after endAt', () => {
    expect(
      announcementState({ enabled: true, intervalMinutes: 10, endAt: '2026-11-24T12:00:00Z' }, now),
    ).toBe('expired')
  })

  it('returns active inside window', () => {
    expect(
      announcementState(
        {
          enabled: true,
          intervalMinutes: 10,
          startAt: '2026-11-24T11:00:00Z',
          endAt: '2026-11-24T13:00:00Z',
        },
        now,
      ),
    ).toBe('active')
  })
})

describe('nextRunAt', () => {
  const now = new Date('2026-11-24T12:00:00Z')

  it('returns null when disabled or expired', () => {
    expect(nextRunAt({ enabled: false, intervalMinutes: 10 }, now)).toBeNull()
    expect(
      nextRunAt({ enabled: true, intervalMinutes: 10, endAt: '2026-11-24T11:59:00Z' }, now),
    ).toBeNull()
  })

  it('returns startAt when never sent and startAt is in the future', () => {
    const next = nextRunAt(
      { enabled: true, intervalMinutes: 10, startAt: '2026-11-24T12:30:00Z' },
      now,
    )
    expect(next?.toISOString()).toBe('2026-11-24T12:30:00.000Z')
  })

  it('adds intervalMinutes to lastSentAt', () => {
    const next = nextRunAt(
      { enabled: true, intervalMinutes: 15, lastSentAt: '2026-11-24T11:50:00Z' },
      now,
    )
    expect(next?.toISOString()).toBe('2026-11-24T12:05:00.000Z')
  })

  it('returns null if next run falls at or after endAt', () => {
    const next = nextRunAt(
      {
        enabled: true,
        intervalMinutes: 30,
        lastSentAt: '2026-11-24T11:50:00Z',
        endAt: '2026-11-24T12:15:00Z',
      },
      now,
    )
    expect(next).toBeNull()
  })
})
