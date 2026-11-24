export function assetUrl(path: string | null | undefined): string | null {
  if (!path) return null
  const trimmed = path.trim()
  if (!trimmed) return null
  if (trimmed.startsWith('/assets/') || trimmed.startsWith('http://') || trimmed.startsWith('https://')) {
    return trimmed
  }
  return `/assets/${trimmed.replace(/^\/+/, '')}`
}

export function formatSpellPower(power: number): string {
  if (power >= 30000) return `${power - 30000}%`
  return String(power)
}

export function formatMapLabel(map: {
  id: number
  name?: string | null
  serverName?: string | null
}): string {
  const title = map.name || map.serverName
  return title ? `${title} (#${map.id})` : `#${map.id}`
}

export function splitLookface(lookface: number): { look: number; direction: number } {
  if (!Number.isFinite(lookface) || lookface < 0) return { look: 0, direction: 0 }
  const direction = Math.trunc(lookface) % 10
  const look = Math.floor(Math.trunc(lookface) / 10)
  return { look, direction }
}

export function combineLookface(look: number, direction: number): number {
  const safeLook = Math.max(0, Math.trunc(look))
  const safeDir = ((Math.trunc(direction) % 8) + 8) % 8
  return safeLook * 10 + safeDir
}
