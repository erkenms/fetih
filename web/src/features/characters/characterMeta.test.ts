import { describe, expect, it } from 'vitest'
import {
  EQUIPMENT_SLOTS,
  isMuted,
  mapEquipmentSlots,
  pkColor,
  toCatalogItem,
  toItemInstance,
  type CharacterItemEntry,
} from './characterMeta'

const sampleEntry: CharacterItemEntry = {
  itemId: 500,
  typeId: 130009,
  position: 3,
  amount: 1000,
  amountLimit: 1000,
  stackAmount: 1,
  plus: 6,
  bless: 5,
  enchant: 120,
  gem1: 13,
  gem2: 255,
  color: 3,
  locked: true,
  package: 0,
  catalog: null,
}

describe('characterMeta', () => {
  it('builds a fallback catalog item when assets are missing', () => {
    const cat = toCatalogItem(sampleEntry)
    expect(cat.id).toBe(130009)
    expect(cat.name).toBe('#130009')
    expect(cat.gem1).toBe(13)
  })

  it('maps character item fields to ItemInstance', () => {
    const inst = toItemInstance(sampleEntry)
    expect(inst.plus).toBe(6)
    expect(inst.bless).toBe(5)
    expect(inst.locked).toBe(true)
    expect(inst.stackCount).toBe(1)
  })

  it('indexes equipment by slot position', () => {
    const map = mapEquipmentSlots([sampleEntry])
    expect(map.get(3)?.itemId).toBe(500)
    expect(map.get(1)).toBeUndefined()
    expect(EQUIPMENT_SLOTS).toContain(3)
    expect(EQUIPMENT_SLOTS).toContain(18)
  })

  it('computes PK color and mute status', () => {
    expect(pkColor(0)).toBe('gray')
    expect(pkColor(30)).toBe('red')
    expect(pkColor(100)).toBe('dark')
    expect(isMuted(null, 1000)).toBe(false)
    expect(isMuted('2026-11-25T10:00:00Z', Date.parse('2026-11-25T09:00:00Z'))).toBe(true)
    expect(isMuted('2026-11-25T10:00:00Z', Date.parse('2026-11-25T11:00:00Z'))).toBe(false)
  })
})
