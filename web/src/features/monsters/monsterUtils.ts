export const ATTACK_USER_AGGRESSIVE = 0x02
export const ATTACK_USER_GUARD = 0x08

export interface MonsterDraft {
  id: number
  name: string
  lookface: number
  level: number
  life: number
  attackMin: number
  attackMax: number
  defense: number
  dexterity: number
  dodge: number
  attackRange: number
  viewRange: number
  attackSpeed: number
  moveSpeed: number
  attackUser: number
  dropMoney: number
  dropHp: number
  dropMp: number
  boss: boolean
  magicType: number
  magicHitRate: number
  spells: number[]
}

export function hasAttackUserFlag(attackUser: number, flag: number): boolean {
  return (attackUser & flag) !== 0
}

export function toggleAttackUserFlag(attackUser: number, flag: number, enabled: boolean): number {
  return enabled ? (attackUser | flag) >>> 0 : (attackUser & ~flag) >>> 0
}

export function nextAvailableMonsterId(existingIds: Iterable<number>): number {
  const used = new Set<number>()
  let max = 0
  for (const id of existingIds) {
    used.add(id)
    if (id > max) max = id
  }
  let candidate = max + 1
  while (candidate <= 0 || used.has(candidate)) candidate++
  return candidate
}

export function createEmptyMonster(id: number): MonsterDraft {
  return {
    id,
    name: '',
    lookface: 104,
    level: 1,
    life: 100,
    attackMin: 5,
    attackMax: 10,
    defense: 0,
    dexterity: 0,
    dodge: 30,
    attackRange: 1,
    viewRange: 8,
    attackSpeed: 500,
    moveSpeed: 500,
    attackUser: 3,
    dropMoney: 0,
    dropHp: 0,
    dropMp: 0,
    boss: false,
    magicType: 0,
    magicHitRate: 100,
    spells: [],
  }
}

export type MonsterDraftErrorKey =
  | 'idPositive'
  | 'idDuplicate'
  | 'nameRequired'
  | 'levelRange'
  | 'lifePositive'
  | 'attackRangeInvalid'
  | 'magicHitRateRange'

export function validateMonsterDraft(
  draft: MonsterDraft,
  existingIds: Set<number>,
  originalId: number | null,
): MonsterDraftErrorKey[] {
  const errors: MonsterDraftErrorKey[] = []
  if (!Number.isInteger(draft.id) || draft.id <= 0) {
    errors.push('idPositive')
  } else if (draft.id !== originalId && existingIds.has(draft.id)) {
    errors.push('idDuplicate')
  }
  if (!draft.name.trim()) {
    errors.push('nameRequired')
  }
  if (!Number.isInteger(draft.level) || draft.level < 0 || draft.level > 255) {
    errors.push('levelRange')
  }
  if (!Number.isInteger(draft.life) || draft.life <= 0) {
    errors.push('lifePositive')
  }
  if (draft.attackMin > draft.attackMax) {
    errors.push('attackRangeInvalid')
  }
  if (draft.magicHitRate < 0 || draft.magicHitRate > 100) {
    errors.push('magicHitRateRange')
  }
  return errors
}

export type MonsterFilterRole = 'all' | 'boss' | 'guard' | 'magic'

export interface MonsterFilterRow {
  monster: MonsterDraft
  spawnAreaCount: number
  bossScheduleCount: number
}

export function filterMonsters<T extends MonsterFilterRow>(
  rows: T[],
  options: {
    search: string
    role: MonsterFilterRole
    minLevel?: number
    maxLevel?: number
    spawnedOnly: boolean
  },
): T[] {
  const term = options.search.trim().toLowerCase()
  return rows.filter((row) => {
    const m = row.monster
    if (term) {
      const idMatch = String(m.id).includes(term)
      const nameMatch = m.name.toLowerCase().includes(term)
      if (!idMatch && !nameMatch) return false
    }
    if (options.minLevel != null && m.level < options.minLevel) return false
    if (options.maxLevel != null && m.level > options.maxLevel) return false
    if (options.role === 'boss' && !m.boss) return false
    if (options.role === 'guard' && !hasAttackUserFlag(m.attackUser, ATTACK_USER_GUARD)) return false
    if (options.role === 'magic' && m.magicType === 0 && m.spells.length === 0) return false
    if (options.spawnedOnly && row.spawnAreaCount === 0 && row.bossScheduleCount === 0) return false
    return true
  })
}
