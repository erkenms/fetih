// The parts of a duration, largest first, for the uptime card
export function durationParts(seconds: number): { days: number; hours: number; minutes: number } {
  const total = Math.max(0, Math.floor(seconds / 60))
  return { days: Math.floor(total / 1440), hours: Math.floor((total % 1440) / 60), minutes: total % 60 }
}

// Bytes in the largest binary unit that keeps the number at or above 1
export function byteSize(bytes: number): { value: number; unit: 'B' | 'KB' | 'MB' | 'GB' } {
  const units = ['B', 'KB', 'MB', 'GB'] as const
  let value = Math.max(0, bytes)
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit++
  }
  return { value: Math.round(value * 10) / 10, unit: units[unit] }
}

// How long ago `time` was, in the unit Intl.RelativeTimeFormat should use
export function since(time: Date, now: Date): { value: number; unit: Intl.RelativeTimeFormatUnit } {
  const seconds = Math.round((time.getTime() - now.getTime()) / 1000)
  const abs = Math.abs(seconds)
  if (abs < 60) return { value: seconds, unit: 'second' }
  if (abs < 3600) return { value: Math.round(seconds / 60), unit: 'minute' }
  if (abs < 86400) return { value: Math.round(seconds / 3600), unit: 'hour' }
  return { value: Math.round(seconds / 86400), unit: 'day' }
}

// An embeddable Grafana panel of the provisioned Overview dashboard
export function grafanaPanel(base: string, panelId: number, theme: 'light' | 'dark', range = '6h') {
  const query = new URLSearchParams({
    orgId: '1',
    panelId: String(panelId),
    from: `now-${range}`,
    to: 'now',
    theme,
    refresh: '1m',
  })
  return `${base}/d-solo/fetih-overview/overview?${query}`
}

export function grafanaDashboard(base: string) {
  return `${base}/d/fetih-overview/overview`
}
