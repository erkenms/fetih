import { describe, expect, it } from 'vitest'
import {
  buildTooltipModel,
  decodeSocket,
  formatDurability,
  itemIconUrl,
  professionKey,
  qualityColor,
} from './itemMeta'

describe('itemIconUrl', () => {
  it('prefixes assets path and falls back to default', () => {
    expect(itemIconUrl('icons/items/Item1088000.png')).toBe('/assets/icons/items/Item1088000.png')
    expect(itemIconUrl(null)).toBe('/assets/icons/items/default.png')
    expect(itemIconUrl(undefined)).toBe('/assets/icons/items/default.png')
  })
})

describe('qualityColor', () => {
  it('uses the client colors for item names', () => {
    expect(qualityColor('Super')).toBe('#ebe57f')
    expect(qualityColor('Elite')).toBe('#e804fd')
    expect(qualityColor('Unique')).toBe('#00ff00')
    expect(qualityColor('Refined')).toBe('#5e87ca')
    expect(qualityColor('Normal')).toBe('#ffffff')
  })
})

describe('decodeSocket', () => {
  it('decodes empty, gem, and zero sockets', () => {
    expect(decodeSocket(0)).toBeNull()
    expect(decodeSocket(255)).toEqual({ type: 'empty', raw: 255 })
    expect(decodeSocket(13)).toEqual({
      type: 'gem',
      raw: 13,
      kind: 1,
      grade: 3,
      percent: 15,
    })
    expect(decodeSocket(72)).toEqual({
      type: 'gem',
      raw: 72,
      kind: 7,
      grade: 2,
      percent: 4,
    })
  })
})

describe('formatDurability', () => {
  it('divides gear durability by 100 and keeps raw count for arrows', () => {
    expect(formatDurability(4599, 6000, 'Weapon')).toEqual({
      current: 45,
      max: 60,
      isArrow: false,
    })
    expect(formatDurability(50, 50, 'Arrow')).toEqual({
      current: 50,
      max: 50,
      isArrow: true,
    })
    expect(formatDurability(0, 0, 'Consumable')).toBeNull()
  })
})

describe('buildTooltipModel', () => {
  it('builds weapon tooltip with instance bonuses and sockets', () => {
    const model = buildTooltipModel(
      {
        id: 410009,
        name: 'PhoenixBlade',
        type: 'Blade',
        category: 'Weapon',
        quality: 'Super',
        minAttack: 100,
        maxAttack: 200,
        attackRange: 2,
        attackSpeed: 500,
        requiredLevel: 15,
        requiredProfession: 10,
        requiredStrength: 25,
        amount: 4000,
        amountLimit: 4000,
        price: 3200,
      },
      { plus: 12, bless: 7, enchant: 255, gem1: 13, gem2: 255, locked: true },
    )

    expect(model.plus).toBe(12)
    expect(model.bless).toBe(7)
    expect(model.enchant).toBe(255)
    expect(model.locked).toBe(true)
    expect(model.stats).toEqual([
      { key: 'attack', value: '100–200' },
      { key: 'attackRange', value: '2' },
      { key: 'attackSpeed', value: '500' },
    ])
    expect(model.durability).toEqual({ current: 40, max: 40, isArrow: false })
    expect(model.sockets).toHaveLength(2)
    expect(model.requirements.map((r) => r.key)).toEqual(['level', 'profession', 'strength'])
    expect(professionKey(10)).toBe('profession.10')
  })

  it('builds armor and gem tooltips', () => {
    const armor = buildTooltipModel({
      id: 130008,
      name: 'DragonArmor',
      category: 'Armor',
      quality: 'Elite',
      defense: 180,
      magicDefense: 30,
      requiredLevel: 70,
    })
    expect(armor.stats).toEqual([
      { key: 'defense', value: '+180' },
      { key: 'magicDefense', value: '+30' },
    ])

    const gem = buildTooltipModel({
      id: 700013,
      name: 'SuperDragonGem',
      category: 'Gem',
      quality: 'None',
      description: 'Increases physical attack by 15%.',
    })
    expect(gem.description).toBe('Increases physical attack by 15%.')
    expect(gem.stats).toEqual([])
  })
})
