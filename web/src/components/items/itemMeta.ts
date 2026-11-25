import type { Schemas } from '../../api/client'

export type CatalogItem = Schemas['CatalogItem']
export type ItemCategory = Schemas['ItemCategory']
export type ItemQuality = Schemas['ItemQuality']

export interface ItemInstance {
  plus?: number
  bless?: number
  enchant?: number
  gem1?: number
  gem2?: number
  amount?: number
  amountLimit?: number
  locked?: boolean
  stackCount?: number
}

export interface DecodedSocket {
  type: 'empty' | 'gem' | 'unknown'
  raw: number
  kind?: number
  grade?: number
  percent?: number
}

export interface TooltipRequirement {
  key: 'level' | 'profession' | 'gender' | 'weaponSkill' | 'strength' | 'agility' | 'vitality' | 'spirit'
  value: number | string
}

export interface TooltipStat {
  key:
    | 'attack'
    | 'magicAttack'
    | 'defense'
    | 'magicDefense'
    | 'life'
    | 'mana'
    | 'dexterity'
    | 'dodge'
    | 'attackRange'
    | 'attackSpeed'
  value: string
}

export interface TooltipModel {
  id: number
  name: string
  type: string | null
  quality: ItemQuality
  category: ItemCategory
  plus: number
  bless: number
  enchant: number
  locked: boolean
  stats: TooltipStat[]
  durability: { current: number; max: number; isArrow: boolean } | null
  sockets: DecodedSocket[]
  requirements: TooltipRequirement[]
  price: number
  emoneyPrice: number
  stackLimit: number
  bound: boolean
  description: string | null
}

export function itemIconUrl(icon?: string | null): string {
  return icon ? `/assets/${icon}` : '/assets/icons/items/default.png'
}

// The client's item name colors (CItem::GetColorOfItemName), by the type ID's last digit
export function qualityColor(quality?: ItemQuality): string {
  switch (quality) {
    case 'Refined':
      return '#5e87ca'
    case 'Unique':
      return '#00ff00'
    case 'Elite':
      return '#e804fd'
    case 'Super':
      return '#ebe57f'
    default:
      return '#ffffff'
  }
}

export function hasQualityColor(quality?: ItemQuality): boolean {
  return quality === 'Refined' || quality === 'Unique' || quality === 'Elite' || quality === 'Super'
}

// The client draws its text with a black edge; on parchment it keeps the bright colors legible
export const GAME_TEXT_EDGE = '1px 0 0 #000, -1px 0 0 #000, 0 1px 0 #000, 0 -1px 0 #000'

const KNOWN_PROFESSIONS = new Set([
  10, 11, 12, 13, 14, 15,
  20, 21, 22, 23, 24, 25,
  40, 41, 42, 43, 44, 45,
  50, 51, 52, 53, 54, 55,
  60, 61, 62, 63, 64, 65,
  70, 71, 72, 73, 74, 75,
  100, 101, 132, 133, 134, 135, 142, 143, 144, 145,
])

export function professionKey(requiredProfession?: number): string | null {
  if (!requiredProfession) return null
  if (KNOWN_PROFESSIONS.has(requiredProfession)) return `profession.${requiredProfession}`
  const line = requiredProfession >= 100 ? 10 : Math.floor(requiredProfession / 10)
  return `profession.line.${line}`
}

const KNOWN_GEM_KINDS = new Set([0, 1, 2, 3, 4, 5, 6, 7, 10, 12])

export function gemPercent(kind: number, grade: number): number {
  if (grade < 1 || grade > 3) return 0
  if (kind === 0 || kind === 1 || kind === 2) return 5 * grade
  if (kind === 3) return [10, 15, 25][grade - 1]
  if (kind === 4) return [50, 100, 200][grade - 1]
  if (kind === 5) return [30, 50, 100][grade - 1]
  if (kind === 6) return [15, 30, 50][grade - 1]
  if (kind === 7) return 2 * grade
  return 0
}

export function decodeSocket(socket?: number): DecodedSocket | null {
  if (!socket) return null
  if (socket === 255) return { type: 'empty', raw: socket }
  const grade = socket % 10
  const kind = Math.floor(socket / 10)
  if (grade < 1 || grade > 3 || !KNOWN_GEM_KINDS.has(kind)) {
    return { type: 'unknown', raw: socket }
  }
  return {
    type: 'gem',
    raw: socket,
    kind,
    grade,
    percent: gemPercent(kind, grade),
  }
}

const WEARABLE_DURABILITY_CATEGORIES = new Set<ItemCategory>([
  'Weapon',
  'Armor',
  'Headwear',
  'Necklace',
  'Ring',
  'Boots',
  'Shield',
  'Garment',
  'Steed',
  'Crop',
  'Arrow',
])

export function formatDurability(
  amount: number | undefined,
  amountLimit: number | undefined,
  category: ItemCategory | undefined,
): { current: number; max: number; isArrow: boolean } | null {
  if (!category || !WEARABLE_DURABILITY_CATEGORIES.has(category)) return null
  const maxRaw = amountLimit ?? 0
  if (maxRaw <= 0) return null
  const currentRaw = amount ?? maxRaw
  if (category === 'Arrow') {
    return { current: currentRaw, max: maxRaw, isArrow: true }
  }
  const max = Math.floor(maxRaw / 100)
  if (max <= 0) return null
  return {
    current: Math.floor(currentRaw / 100),
    max,
    isArrow: false,
  }
}

export function buildTooltipModel(item: CatalogItem, instance?: ItemInstance): TooltipModel {
  const stats: TooltipStat[] = []
  const minAtk = item.minAttack ?? 0
  const maxAtk = item.maxAttack ?? 0
  if (minAtk > 0 || maxAtk > 0) {
    stats.push({ key: 'attack', value: `${minAtk}–${maxAtk}` })
  }
  if ((item.magicAttack ?? 0) > 0) {
    stats.push({ key: 'magicAttack', value: `+${item.magicAttack}` })
  }
  if ((item.defense ?? 0) > 0) {
    stats.push({ key: 'defense', value: `+${item.defense}` })
  }
  if ((item.magicDefense ?? 0) > 0) {
    stats.push({ key: 'magicDefense', value: `+${item.magicDefense}` })
  }
  if ((item.life ?? 0) > 0) {
    stats.push({ key: 'life', value: `+${item.life}` })
  }
  if ((item.mana ?? 0) > 0) {
    stats.push({ key: 'mana', value: `+${item.mana}` })
  }
  if ((item.dexterity ?? 0) > 0) {
    stats.push({ key: 'dexterity', value: `+${item.dexterity}` })
  }
  if ((item.dodge ?? 0) > 0) {
    stats.push({ key: 'dodge', value: `+${item.dodge}` })
  }
  if (item.category === 'Weapon' && (item.attackRange ?? 0) > 0) {
    stats.push({ key: 'attackRange', value: `${item.attackRange}` })
  }
  if (item.category === 'Weapon' && (item.attackSpeed ?? 0) > 0) {
    stats.push({ key: 'attackSpeed', value: `${item.attackSpeed}` })
  }

  const requirements: TooltipRequirement[] = []
  if ((item.requiredLevel ?? 0) > 0) {
    requirements.push({ key: 'level', value: item.requiredLevel! })
  }
  const prof = professionKey(item.requiredProfession)
  if (prof) {
    requirements.push({ key: 'profession', value: prof })
  }
  if ((item.requiredGender ?? 0) > 0) {
    requirements.push({ key: 'gender', value: item.requiredGender! })
  }
  if ((item.requiredWeaponSkill ?? 0) > 0) {
    requirements.push({ key: 'weaponSkill', value: item.requiredWeaponSkill! })
  }
  if ((item.requiredStrength ?? 0) > 0) {
    requirements.push({ key: 'strength', value: item.requiredStrength! })
  }
  if ((item.requiredAgility ?? 0) > 0) {
    requirements.push({ key: 'agility', value: item.requiredAgility! })
  }
  if ((item.requiredVitality ?? 0) > 0) {
    requirements.push({ key: 'vitality', value: item.requiredVitality! })
  }
  if ((item.requiredSpirit ?? 0) > 0) {
    requirements.push({ key: 'spirit', value: item.requiredSpirit! })
  }

  const sockets = [
    decodeSocket(instance?.gem1 ?? item.gem1),
    decodeSocket(instance?.gem2 ?? item.gem2),
  ].filter((s): s is DecodedSocket => s !== null)

  return {
    id: item.id ?? 0,
    name: item.name ?? '',
    type: item.type ?? null,
    quality: item.quality ?? 'None',
    category: item.category ?? 'Other',
    plus: instance?.plus ?? 0,
    bless: instance?.bless ?? 0,
    enchant: instance?.enchant ?? 0,
    locked: instance?.locked ?? false,
    stats,
    durability: formatDurability(
      instance?.amount ?? item.amount,
      instance?.amountLimit ?? item.amountLimit,
      item.category,
    ),
    sockets,
    requirements,
    price: item.price ?? 0,
    emoneyPrice: item.emoneyPrice ?? 0,
    stackLimit: item.stackLimit ?? 0,
    bound: (item.monopoly ?? 0) > 0,
    description: item.description ?? null,
  }
}
