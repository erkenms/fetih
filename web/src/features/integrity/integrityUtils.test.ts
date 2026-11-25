import { describe, expect, it } from 'vitest'
import { distinctCategories, filterProblems, type IntegrityProblemLike } from './integrityUtils'

const sample: IntegrityProblemLike[] = [
  {
    file: 'spawns.json',
    category: 'missing_monster',
    level: 'error',
    entry: 0,
    line: 12,
    entityId: 'Spawn #1',
    message: "Monster 9999 isn't in monsters.json",
    editorPath: '/admin/maps/1002?x=5&y=5',
    editorKind: 'map',
  },
  {
    file: 'npcs.json',
    category: 'unwalkable_cell',
    level: 'warning',
    entry: 2,
    line: 40,
    entityId: 'NPC 101',
    message: 'NPC 101 (10, 5) on map 1002 is on an unwalkable cell',
    editorPath: '/admin/maps/1002?x=10&y=5',
    editorKind: 'map',
  },
  {
    file: 'talkers.json',
    category: 'missing_npc',
    level: 'error',
    entry: 1,
    line: 18,
    entityId: 'NPC 4444',
    message: 'Talker NPC 4444 does not exist in npcs.json or goals.json',
    editorPath: '/admin/data?tab=talkers',
    editorKind: 'talkers',
  },
]

describe('filterProblems', () => {
  it('filters by level', () => {
    expect(filterProblems(sample, { level: 'errors', file: null, category: null, search: '' })).toHaveLength(2)
    expect(filterProblems(sample, { level: 'warnings', file: null, category: null, search: '' })).toHaveLength(1)
    expect(filterProblems(sample, { level: 'all', file: null, category: null, search: '' })).toHaveLength(3)
  })

  it('filters by file, category, and search text', () => {
    const byFile = filterProblems(sample, { level: 'all', file: 'spawns.json', category: null, search: '' })
    expect(byFile.map((p) => p.entityId)).toEqual(['Spawn #1'])

    const byCategory = filterProblems(sample, { level: 'all', file: null, category: 'missing_npc', search: '' })
    expect(byCategory.map((p) => p.file)).toEqual(['talkers.json'])

    const bySearch = filterProblems(sample, { level: 'all', file: null, category: null, search: '9999' })
    expect(bySearch.map((p) => p.entityId)).toEqual(['Spawn #1'])
  })
})

describe('distinctCategories', () => {
  it('returns sorted unique categories', () => {
    expect(distinctCategories(sample)).toEqual(['missing_monster', 'missing_npc', 'unwalkable_cell'])
  })
})
