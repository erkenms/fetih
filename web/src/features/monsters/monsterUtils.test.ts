import { describe, expect, it } from 'vitest'
import {
  ATTACK_USER_AGGRESSIVE,
  ATTACK_USER_GUARD,
  createEmptyMonster,
  filterMonsters,
  hasAttackUserFlag,
  nextAvailableMonsterId,
  toggleAttackUserFlag,
  validateMonsterDraft,
} from './monsterUtils'

describe('attackUser flags', () => {
  it('checks and toggles aggressive and guard bits without disturbing other bits', () => {
    expect(hasAttackUserFlag(3, ATTACK_USER_AGGRESSIVE)).toBe(true)
    expect(hasAttackUserFlag(3, ATTACK_USER_GUARD)).toBe(false)
    expect(hasAttackUserFlag(15, ATTACK_USER_GUARD)).toBe(true)

    const withGuard = toggleAttackUserFlag(3, ATTACK_USER_GUARD, true)
    expect(withGuard).toBe(11)
    expect(hasAttackUserFlag(withGuard, ATTACK_USER_GUARD)).toBe(true)
    expect(hasAttackUserFlag(withGuard, ATTACK_USER_AGGRESSIVE)).toBe(true)

    const withoutAggressive = toggleAttackUserFlag(withGuard, ATTACK_USER_AGGRESSIVE, false)
    expect(withoutAggressive).toBe(9)
    expect(hasAttackUserFlag(withoutAggressive, ATTACK_USER_AGGRESSIVE)).toBe(false)
    expect(hasAttackUserFlag(withoutAggressive, ATTACK_USER_GUARD)).toBe(true)
  })
})

describe('nextAvailableMonsterId', () => {
  it('picks one above the highest ID', () => {
    expect(nextAvailableMonsterId([])).toBe(1)
    expect(nextAvailableMonsterId([1, 2, 100])).toBe(101)
  })
})

describe('validateMonsterDraft', () => {
  it('accepts a valid monster and catches invalid fields', () => {
    const existing = new Set([1, 2, 3])
    const valid = { ...createEmptyMonster(4), name: 'Wolf' }
    expect(validateMonsterDraft(valid, existing, null)).toEqual([])

    // Editing an existing ID keeps its own ID valid
    expect(validateMonsterDraft({ ...valid, id: 2 }, existing, 2)).toEqual([])

    const invalid = {
      ...createEmptyMonster(2),
      name: '   ',
      level: 300,
      life: 0,
      attackMin: 50,
      attackMax: 10,
      magicHitRate: 150,
    }
    expect(validateMonsterDraft(invalid, existing, null)).toEqual([
      'idDuplicate',
      'nameRequired',
      'levelRange',
      'lifePositive',
      'attackRangeInvalid',
      'magicHitRateRange',
    ])
  })
})

describe('filterMonsters', () => {
  const rows = [
    {
      monster: { ...createEmptyMonster(1), name: 'Pheasant', level: 1 },
      spawnAreaCount: 3,
      bossScheduleCount: 0,
    },
    {
      monster: { ...createEmptyMonster(900), name: 'Guard1', level: 200, attackUser: 15, magicType: 8036 },
      spawnAreaCount: 12,
      bossScheduleCount: 0,
    },
    {
      monster: { ...createEmptyMonster(3587), name: 'TeratoDragon', level: 140, boss: true, spells: [7012] },
      spawnAreaCount: 0,
      bossScheduleCount: 1,
    },
    {
      monster: { ...createEmptyMonster(9999), name: 'UnusedMob', level: 50 },
      spawnAreaCount: 0,
      bossScheduleCount: 0,
    },
  ]

  it('filters by search, role, level range, and spawned status', () => {
    expect(filterMonsters(rows, { search: 'dragon', role: 'all', spawnedOnly: false }).map((r) => r.monster.id)).toEqual([3587])
    expect(filterMonsters(rows, { search: '900', role: 'all', spawnedOnly: false }).map((r) => r.monster.id)).toEqual([900])
    expect(filterMonsters(rows, { search: '', role: 'boss', spawnedOnly: false }).map((r) => r.monster.id)).toEqual([3587])
    expect(filterMonsters(rows, { search: '', role: 'guard', spawnedOnly: false }).map((r) => r.monster.id)).toEqual([900])
    expect(filterMonsters(rows, { search: '', role: 'magic', spawnedOnly: false }).map((r) => r.monster.id)).toEqual([900, 3587])
    expect(filterMonsters(rows, { search: '', role: 'all', minLevel: 40, maxLevel: 150, spawnedOnly: false }).map((r) => r.monster.id)).toEqual([3587, 9999])
    expect(filterMonsters(rows, { search: '', role: 'all', spawnedOnly: true }).map((r) => r.monster.id)).toEqual([1, 900, 3587])
  })
})
