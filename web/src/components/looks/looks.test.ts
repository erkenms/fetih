import { describe, expect, it } from 'vitest'
import { indexLooks, matchesMonsterLook, matchesNpcLook, monsterLook, monsterPicture, npcLook, npcPicture } from './looks'

const catalog = {
  previewDirection: 0,
  npc: [
    {
      lookface: 10,
      image: 'looks/npc/10.png',
      x: 40,
      y: 160,
      name: 'Storekeeper',
    },
    {
      lookface: 16,
      image: 'looks/npc/16.png',
      x: 40,
      y: 160,
      name: 'Storekeeper',
    },
    {
      lookface: 97366,
      image: 'looks/npc/97366.png',
      x: 40,
      y: 160,
      name: null,
    },
    { lookface: 27, image: 'looks/npc/27.png', x: 40, y: 160, name: 'Barber' },
  ],
  monster: [
    {
      look: 104,
      image: 'looks/monster/104.png',
      x: 60,
      y: 120,
      monsters: [
        { id: 1, name: 'Pheasant', zoomPercent: 100 },
        { id: 2, name: 'Big Pheasant', zoomPercent: 150 },
      ],
    },
  ],
}

describe('looks', () => {
  const index = indexLooks(catalog)

  it('lists one entry per NPC type, in the preview direction when there is one', () => {
    expect(index.npcTypes.map((l) => l.lookface)).toEqual([10, 27, 97366])
  })

  it('finds an NPC lookface, else its type in another direction', () => {
    expect(npcLook(index, 16)?.image).toBe('looks/npc/16.png')
    expect(npcLook(index, 13)?.image).toBe('looks/npc/10.png')
    expect(npcLook(index, 21)?.image).toBe('looks/npc/27.png')
    expect(npcLook(index, 55)).toBeNull()
    expect(npcLook(index, null)).toBeNull()
  })

  it('finds monster looks', () => {
    expect(monsterLook(index, 104)?.monsters[0].name).toBe('Pheasant')
    expect(monsterLook(index, 105)).toBeNull()
  })

  it('places a monster by its type, scaled by its ZoomPercent', () => {
    expect(monsterPicture(index, 2)).toEqual({ image: 'looks/monster/104.png', x: 60, y: 120, zoom: 1.5 })
    expect(monsterPicture(index, 1)?.zoom).toBe(1)
    expect(monsterPicture(index, 99, 104)?.zoom).toBe(1)
    expect(monsterPicture(index, 99, 105)).toBeNull()
    expect(npcPicture(index, 16)).toEqual({ image: 'looks/npc/16.png', x: 40, y: 160, zoom: 1 })
    expect(npcPicture(index, 55)).toBeNull()
  })

  it('searches by name, type number or full lookface', () => {
    expect(matchesNpcLook(catalog.npc[0], 'store')).toBe(true)
    expect(matchesNpcLook(catalog.npc[0], '1')).toBe(true)
    expect(matchesNpcLook(catalog.npc[2], '97366')).toBe(true)
    expect(matchesNpcLook(catalog.npc[2], 'store')).toBe(false)
    expect(matchesMonsterLook(catalog.monster[0], 'pheas')).toBe(true)
    expect(matchesMonsterLook(catalog.monster[0], '1')).toBe(true)
    expect(matchesMonsterLook(catalog.monster[0], 'bat')).toBe(false)
  })

  it('is empty before the catalog loads', () => {
    const empty = indexLooks(undefined)
    expect(empty.npcTypes).toEqual([])
    expect(npcLook(empty, 10)).toBeNull()
  })
})
