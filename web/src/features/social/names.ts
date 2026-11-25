// Keys under social:class for a character's class: its base class, as the client groups them
// (Trojan 1x, Warrior 2x, Archer 4x, Ninja 5x, Taoist 100-101, Water 13x, Fire 14x)
export function classKey(currentClass: number): string | null {
  if (currentClass === 100 || currentClass === 101) return 'taoist'
  const base = Math.floor(currentClass / 10)
  switch (base) {
    case 1:
      return 'trojan'
    case 2:
      return 'warrior'
    case 4:
      return 'archer'
    case 5:
      return 'ninja'
    case 13:
      return 'water'
    case 14:
      return 'fire'
    default:
      return null
  }
}

// Keys under social:rank for guild (Syndicate) and family member ranks
export function guildRankKey(rank: number): string {
  return rank >= 1000 ? 'leader' : rank >= 990 ? 'deputy' : 'member'
}

export function familyRankKey(rank: number): string {
  return rank >= 100 ? 'leader' : rank === 11 ? 'spouse' : 'member'
}

export const boardChannels = [2201, 2202, 2203, 2204, 2205] as const
