// Where things sit in the client's window art (web/assets/ui), in the art's own pixels.
// Measured from the pictures; the dialog templates in Conquer.exe give the same order.

export interface Box {
  x: number
  y: number
  w: number
  h: number
}

// The status window (DialogQuery.dds), its pages and its round tab buttons
export const STATUS = {
  width: 264,
  height: 435,
  page: { x: 43, y: 47, w: 204, h: 322 },
  tabs: { stats: 111, equipment: 170, proficiencies: 229, skills: 288 } as const,
  tabSize: { w: 40, h: 39 },
  // Battle power digits: 64×64 pictures drawn at 0.3, 14 apart, centred on the globe
  globe: { x: 31, y: 32 },
  digit: { size: 19, advance: 14 },
}

export type StatusPage = keyof typeof STATUS.tabs

// DialogQuery_Role.dds: the value after each label baked into the art
export const STATS_ROWS = {
  name: { x: 70, y: 9 },
  level: { x: 70, y: 32 },
  class: { x: 70, y: 55 },
  pk: { x: 70, y: 78 },
  spouse: { x: 70, y: 104 },
  guild: { x: 70, y: 124 },
  position: { x: 70, y: 147 },
  title: { x: 70, y: 170 },
  strength: { x: 60, y: 201 },
  agility: { x: 153, y: 201 },
  vitality: { x: 60, y: 231 },
  spirit: { x: 153, y: 231 },
  attributePoints: { x: 92, y: 260 },
} as const

// DialogQuery_Mission.dds: the equipment slots by ItemPosition. The left hand has no frame
// in the art; its place comes from the dialog template.
export const EQUIPMENT_BOXES: Record<number, Box> = {
  1: { x: 153, y: 13, w: 41, h: 40 }, // headwear
  2: { x: 153, y: 64, w: 41, h: 40 }, // necklace
  6: { x: 153, y: 116, w: 41, h: 40 }, // ring
  4: { x: 130, y: 169, w: 65, h: 64 }, // right hand
  3: { x: 130, y: 244, w: 65, h: 64 }, // armor
  5: { x: 57, y: 243, w: 65, h: 64 }, // left hand
  9: { x: 7, y: 164, w: 41, h: 40 }, // garment
  7: { x: 7, y: 216, w: 41, h: 40 }, // bottle
  8: { x: 7, y: 268, w: 41, h: 40 }, // boots
}

export const FRAMED_SLOTS = new Set([1, 2, 6, 4, 3, 9, 7, 8])

// DialogQuery_Weapon.dds: the info box on top, six rows of icon and text, and the scroll
// buttons right of them
export const SKILLS = {
  info: { x: 22, y: 6, w: 157, h: 52 },
  rowTop: 66,
  rowPitch: 38,
  rows: 6,
  icon: { x: 19, w: 34, h: 34 },
  text: { x: 76, w: 105, h: 34 },
  up: { x: 184, y: 67 },
  down: { x: 184, y: 275 },
}

// DialogMpcGoods.dds: 5×8 slots and the three money fields
export const INVENTORY = {
  width: 268,
  height: 492,
  slot: { x: 27, y: 27, pitch: 43, size: 42 },
  columns: 5,
  rows: 8,
  silver: { x: 115, y: 374, w: 85, h: 19 },
  cps: { x: 115, y: 402, w: 85, h: 19 },
  silverIcon: { x: 70, y: 374 },
  cpsIcon: { x: 74, y: 402 },
}
