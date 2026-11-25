import type { Schemas } from '../../api/client'
import type { CatalogItem, ItemInstance } from '../../components/items'

export type CharacterItemEntry = Schemas['CharacterItemEntry']

export const EQUIPMENT_SLOTS = [1, 2, 3, 4, 5, 6, 7, 8, 9, 12, 18] as const

export const GEM_SOCKET_CODES = [
  1, 2, 3, // Phoenix
  11, 12, 13, // Dragon
  21, 22, 23, // Fury
  31, 32, 33, // Rainbow
  41, 42, 43, // Kylin
  51, 52, 53, // Violet
  61, 62, 63, // Moon
  71, 72, 73, // Tortoise
  101, 102, 103, // Thunder
  121, 122, 123, // Glory
] as const

export function toCatalogItem(entry: CharacterItemEntry): CatalogItem {
  if (entry.catalog) return entry.catalog
  return {
    id: entry.typeId,
    name: `#${entry.typeId}`,
    type: null,
    description: null,
    icon: null,
    groundIcon: null,
    category: 'Other',
    quality: 'None',
    requiredProfession: 0,
    requiredWeaponSkill: 0,
    requiredLevel: 0,
    requiredGender: 0,
    requiredStrength: 0,
    requiredAgility: 0,
    requiredVitality: 0,
    requiredSpirit: 0,
    monopoly: 0,
    price: 0,
    emoneyPrice: 0,
    actionId: 0,
    minAttack: 0,
    maxAttack: 0,
    defense: 0,
    magicAttack: 0,
    magicDefense: 0,
    dexterity: 0,
    dodge: 0,
    life: 0,
    mana: 0,
    amount: entry.amount,
    amountLimit: entry.amountLimit,
    gem1: entry.gem1,
    gem2: entry.gem2,
    attackRange: 0,
    attackSpeed: 0,
    stackLimit: 1,
  }
}

export function toItemInstance(entry: CharacterItemEntry): ItemInstance {
  return {
    plus: entry.plus,
    bless: entry.bless,
    enchant: entry.enchant,
    gem1: entry.gem1,
    gem2: entry.gem2,
    amount: entry.amount,
    amountLimit: entry.amountLimit,
    locked: entry.locked,
    stackCount: entry.stackAmount,
  }
}

export function mapEquipmentSlots(items: CharacterItemEntry[]): Map<number, CharacterItemEntry> {
  const bySlot = new Map<number, CharacterItemEntry>()
  for (const item of items) {
    bySlot.set(item.position, item)
  }
  return bySlot
}

export function pkColor(pkPoints: number): string {
  if (pkPoints >= 100) return 'dark'
  if (pkPoints >= 30) return 'red'
  return 'gray'
}

export function isMuted(mutedUntil?: string | null, now = Date.now()): boolean {
  if (!mutedUntil) return false
  const ms = Date.parse(mutedUntil)
  return !Number.isNaN(ms) && ms > now
}

export const itemHistoryPath = (itemId: number) => `/admin/item-history/${itemId}`
