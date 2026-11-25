import { describe, expect, it } from 'vitest'
import { buildPolyline, formatCompact, maxFlowScale } from './economyUtils'

describe('economyUtils', () => {
  it('formats large silver and CP totals compactly', () => {
    expect(formatCompact(0)).toBe('0')
    expect(formatCompact(950)).toBe('950')
    expect(formatCompact(12500)).toBe('12.5K')
    expect(formatCompact(2_450_000)).toBe('2.45M')
    expect(formatCompact(-1_500_000_000)).toBe('-1.50B')
  })

  it('builds SVG polyline points bounded within chart dimensions', () => {
    expect(buildPolyline([], 100, 50)).toBe('')
    expect(buildPolyline([50], 100, 50, 100)).toBe('0,25.0 100,25.0')
    expect(buildPolyline([0, 50, 100], 100, 50, 100)).toBe('0.0,50.0 50.0,25.0 100.0,0.0')
  })

  it('computes flow chart scale from combined inflow vs spent', () => {
    const peak = maxFlowScale([
      { timestamp: '2026-11-25T00:00:00Z', spentAtNpcs: 400, earnedFromNpcs: 200, pickedUpSilver: 350, netFlow: 150 },
      { timestamp: '2026-11-25T01:00:00Z', spentAtNpcs: 900, earnedFromNpcs: 100, pickedUpSilver: 50, netFlow: -750 },
    ])
    expect(peak).toBe(900)
  })
})
