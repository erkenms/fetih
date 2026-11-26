import type { Schemas } from '../../api/client'

export type NpcLook = Schemas['NpcLook']
export type MonsterLook = Schemas['MonsterLook']
export type LooksCatalog = Schemas['LooksCatalogResponse']

export interface LookIndex {
  previewDirection: number
  npc: Map<number, NpcLook>
  monster: Map<number, MonsterLook>
  // One entry per NPC type (lookface / 10), the one in the preview direction when rendered
  npcTypes: NpcLook[]
}

export function indexLooks(catalog: LooksCatalog | undefined): LookIndex {
  const npc = new Map<number, NpcLook>()
  const monster = new Map<number, MonsterLook>()
  const types = new Map<number, NpcLook>()
  const previewDirection = catalog?.previewDirection ?? 0
  for (const look of catalog?.npc ?? []) {
    npc.set(look.lookface, look)
    const type = Math.floor(look.lookface / 10)
    const current = types.get(type)
    if (!current || look.lookface % 10 === previewDirection) types.set(type, look)
  }
  for (const look of catalog?.monster ?? []) monster.set(look.look, look)
  const npcTypes = [...types.values()].sort((a, b) => a.lookface - b.lookface)
  return { previewDirection, npc, monster, npcTypes }
}

// An NPC's lookface as rendered, else the same type in another direction
export function npcLook(index: LookIndex, lookface: number | null | undefined): NpcLook | null {
  if (lookface == null || !Number.isFinite(lookface) || lookface < 0) return null
  const exact = index.npc.get(lookface)
  if (exact) return exact
  const type = Math.floor(lookface / 10)
  return (
    index.npc.get(type * 10 + index.previewDirection) ??
    index.npcTypes.find((l) => Math.floor(l.lookface / 10) === type) ??
    null
  )
}

export function monsterLook(index: LookIndex, look: number | null | undefined): MonsterLook | null {
  if (look == null) return null
  return index.monster.get(look) ?? null
}

// Matches an NPC type by its name or number, or a lookface typed in full
export function matchesNpcLook(look: NpcLook, term: string): boolean {
  const needle = term.trim().toLowerCase()
  if (!needle) return true
  if (/^\d+$/.test(needle)) {
    return String(Math.floor(look.lookface / 10)).startsWith(needle) || String(look.lookface) === needle
  }
  return (look.name ?? '').toLowerCase().includes(needle)
}

export function matchesMonsterLook(look: MonsterLook, term: string): boolean {
  const needle = term.trim().toLowerCase()
  if (!needle) return true
  if (/^\d+$/.test(needle))
    return String(look.look).startsWith(needle) || look.monsters.some((m) => String(m.id) === needle)
  return look.monsters.some((m) => m.name.toLowerCase().includes(needle))
}
