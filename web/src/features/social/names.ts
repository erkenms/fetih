import { classKey as sharedClassKey } from '../../lib/gameMeta'

// Keys under social:class for a character's class
export function classKey(currentClass: number): string | null {
  const key = sharedClassKey(currentClass)
  return key === 'unknown' ? null : key
}

// Keys under social:rank for guild (Syndicate) and family member ranks
export function guildRankKey(rank: number): string {
  return rank >= 1000 ? 'leader' : rank >= 990 ? 'deputy' : 'member'
}

export function familyRankKey(rank: number): string {
  return rank >= 100 ? 'leader' : rank === 11 ? 'spouse' : 'member'
}

export const boardChannels = [2201, 2202, 2203, 2204, 2205] as const
