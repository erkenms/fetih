export type TimeRangeKey = '24h' | '7d' | '30d' | '90d'

export function parseItemIdInput(raw: string): number | null {
  const trimmed = raw.trim()
  if (!/^\d+$/.test(trimmed)) return null
  const num = Number(trimmed)
  if (!Number.isSafeInteger(num) || num <= 0 || num > 0xffffffff) return null
  return num
}

export function rangeToBounds(range: TimeRangeKey, now: Date = new Date()): { from: string; to: string } {
  const hours = range === '24h' ? 24 : range === '7d' ? 24 * 7 : range === '30d' ? 24 * 30 : 24 * 90
  const from = new Date(now.getTime() - hours * 3600 * 1000)
  return { from: from.toISOString(), to: now.toISOString() }
}

export function eventBadgeColor(event: string): string {
  switch (event) {
    case 'item.created':
    case 'item.bought':
      return 'green'
    case 'trade.completed':
    case 'booth.sold':
      return 'blue'
    case 'item.dropped':
    case 'item.picked_up':
      return 'orange'
    case 'item.sold':
    case 'item.expired':
    case 'item.spent':
      return 'red'
    case 'item.upgraded':
    case 'item.composed':
    case 'item.embedded':
    case 'item.enchanted':
    case 'item.refined':
    case 'item.stabilized':
      return 'grape'
    case 'item.stored':
    case 'item.taken_out':
      return 'teal'
    default:
      return 'gray'
  }
}

export function positionCategory(position: number): 'inventory' | 'equipped' | 'warehouse' | 'detained' | 'ground' | 'other' {
  if (position === 0) return 'inventory'
  if (position >= 1 && position <= 19) return 'equipped'
  if (position === 201) return 'warehouse'
  if (position === 250) return 'detained'
  if (position === 254) return 'ground'
  return 'other'
}
