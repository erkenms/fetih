export const baseClassOptions = [10, 20, 40, 50, 60, 70, 100] as const

export function classKey(classId: number): string {
  if (classId >= 10 && classId <= 15) return 'trojan'
  if (classId >= 20 && classId <= 25) return 'warrior'
  if (classId >= 40 && classId <= 45) return 'archer'
  if (classId >= 50 && classId <= 55) return 'ninja'
  if (classId >= 60 && classId <= 65) return 'monk'
  if (classId >= 70 && classId <= 75) return 'pirate'
  if (classId >= 132 && classId <= 135) return 'waterTaoist'
  if (classId >= 142 && classId <= 145) return 'fireTaoist'
  if (classId >= 100 && classId <= 101) return 'taoist'
  return 'unknown'
}

export function guildRankKey(rank: number): string {
  if (rank >= 1000) return 'leader'
  if (rank >= 990) return 'deputyLeader'
  if (rank >= 200) return 'member'
  return 'none'
}

export function clanRankKey(rank: number): string {
  if (rank >= 100) return 'leader'
  if (rank === 11) return 'spouse'
  if (rank >= 10) return 'member'
  return 'none'
}

export function subClassKey(id: number): string {
  switch (id) {
    case 1:
      return 'martialArtist'
    case 2:
      return 'warlock'
    case 3:
      return 'chiMaster'
    case 4:
      return 'sage'
    case 5:
      return 'apothecary'
    case 6:
      return 'performer'
    case 9:
      return 'wrangler'
    default:
      return 'unknown'
  }
}

export function playerAvatarUrl(avatar: number): string {
  return `/assets/faces/player/${avatar}.png`
}

export function formatNumber(value: number | string, locale: string): string {
  const num = typeof value === 'string' ? Number(value) : value
  if (!Number.isFinite(num)) return String(value)
  return new Intl.NumberFormat(locale).format(num)
}

export function formatUnixDate(unixSeconds: number, locale: string): string {
  if (!unixSeconds) return '—'
  return new Date(unixSeconds * 1000).toLocaleDateString(locale)
}
