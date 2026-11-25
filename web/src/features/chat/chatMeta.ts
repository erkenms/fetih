export const chatChannels = [
  'Talk',
  'Whisper',
  'Team',
  'Guild',
  'Clan',
  'Friend',
  'World',
  'Ghost',
  'Vendor',
  'Command',
] as const

export type ChatChannel = (typeof chatChannels)[number]

export const timeRangePresets = ['1h', '6h', '24h', '7d', '30d'] as const
export type TimeRangePreset = (typeof timeRangePresets)[number]

export const muteDurationPresets = [5, 15, 60, 1440] as const

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

export function channelColor(channel: string): string {
  switch (channel) {
    case 'Whisper':
      return 'grape'
    case 'World':
      return 'orange'
    case 'Guild':
      return 'teal'
    case 'Team':
      return 'blue'
    case 'Clan':
      return 'cyan'
    case 'Friend':
      return 'pink'
    case 'Vendor':
      return 'yellow'
    case 'Ghost':
      return 'gray'
    case 'Command':
      return 'indigo'
    case 'GmCommand':
      return 'red'
    case 'Talk':
    default:
      return 'gray'
  }
}

export function commandResultColor(result: string | null | undefined): string {
  switch (result?.toLowerCase()) {
    case 'ok':
      return 'green'
    case 'failed':
      return 'orange'
    case 'unknown':
      return 'red'
    default:
      return 'gray'
  }
}

export function prettyJson(json: string | null | undefined): string {
  if (!json) return '—'
  try {
    return JSON.stringify(JSON.parse(json), null, 2)
  } catch {
    return json
  }
}
