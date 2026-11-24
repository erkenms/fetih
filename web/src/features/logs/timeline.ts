export const timeRangePresets = ['1h', '6h', '24h', '7d', '30d'] as const
export type TimeRangePreset = (typeof timeRangePresets)[number]

const presetHours: Record<TimeRangePreset, number> = {
  '1h': 1,
  '6h': 6,
  '24h': 24,
  '7d': 24 * 7,
  '30d': 24 * 30,
}

export function rangeWindow(preset: TimeRangePreset, now: Date = new Date()): { from: string; to: string } {
  const hours = presetHours[preset]
  const from = new Date(now.getTime() - hours * 3_600_000)
  return { from: from.toISOString(), to: now.toISOString() }
}

export function levelColor(level: string): string {
  switch (level.toLowerCase()) {
    case 'fatal':
    case 'error':
      return 'red'
    case 'warn':
      return 'yellow'
    case 'info':
      return 'blue'
    case 'debug':
    case 'trace':
      return 'gray'
    default:
      return 'gray'
  }
}

export function parseOptionalId(raw: string): number | undefined {
  const trimmed = raw.trim()
  if (!/^\d+$/.test(trimmed)) return undefined
  const value = Number(trimmed)
  return Number.isSafeInteger(value) && value > 0 ? value : undefined
}
