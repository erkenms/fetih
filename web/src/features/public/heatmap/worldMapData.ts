export interface HeatmapMapItem {
  mapId: number
  name: string
  minimap?: string | null
  players: number
  share: number
}

export type MapKind = 'city' | 'hub' | 'battle' | 'zone' | 'dungeon'

export interface WorldNode {
  mapId: number
  fallbackName: string
  x: number
  y: number
  kind: MapKind
  major?: boolean
}

export interface WorldRoute {
  from: number
  to: number
  sea?: boolean
}

export type HeatTier = 'quiet' | 'warm' | 'busy' | 'crowded'

export const WORLD_WIDTH = 960
export const WORLD_HEIGHT = 560

// Approximate layout of Conquer Online's classic 5619 world on a 960x560 parchment chart.
export const WORLD_NODES: WorldNode[] = [
  { mapId: 1002, fallbackName: 'Twin City', x: 485, y: 285, kind: 'city', major: true },
  { mapId: 1036, fallbackName: 'Market', x: 545, y: 222, kind: 'hub', major: true },
  { mapId: 1011, fallbackName: 'Phoenix Castle', x: 675, y: 405, kind: 'city', major: true },
  { mapId: 1020, fallbackName: 'Ape City', x: 285, y: 410, kind: 'city', major: true },
  { mapId: 1000, fallbackName: 'Desert City', x: 245, y: 155, kind: 'city', major: true },
  { mapId: 1015, fallbackName: 'Bird Island', x: 805, y: 195, kind: 'city', major: true },
  { mapId: 1038, fallbackName: 'Guild War', x: 470, y: 125, kind: 'battle', major: true },

  // Secondary hubs & landmarks around the major cities
  { mapId: 1001, fallbackName: 'Mystic Castle', x: 155, y: 112, kind: 'dungeon' },
  { mapId: 1039, fallbackName: 'Training Ground', x: 405, y: 245, kind: 'zone' },
  { mapId: 1004, fallbackName: 'Job Center', x: 440, y: 338, kind: 'zone' },
  { mapId: 1005, fallbackName: 'PK Arena', x: 575, y: 315, kind: 'battle' },
  { mapId: 1028, fallbackName: 'TC Mine', x: 398, y: 300, kind: 'dungeon' },
  { mapId: 1025, fallbackName: 'PC Mine', x: 745, y: 452, kind: 'dungeon' },
  { mapId: 1026, fallbackName: 'AC Mine', x: 215, y: 460, kind: 'dungeon' },
  { mapId: 1027, fallbackName: 'DC Mine', x: 175, y: 215, kind: 'dungeon' },
  { mapId: 1351, fallbackName: 'Labyrinth 1', x: 350, y: 170, kind: 'dungeon' },
  { mapId: 1352, fallbackName: 'Labyrinth 2', x: 375, y: 152, kind: 'dungeon' },
  { mapId: 1353, fallbackName: 'Labyrinth 3', x: 398, y: 168, kind: 'dungeon' },
  { mapId: 1354, fallbackName: 'Labyrinth 4', x: 374, y: 188, kind: 'dungeon' },
  { mapId: 6000, fallbackName: 'PKer Jail', x: 120, y: 325, kind: 'battle' },
]

export const WORLD_ROUTES: WorldRoute[] = [
  { from: 1002, to: 1036 },
  { from: 1002, to: 1011 },
  { from: 1002, to: 1020 },
  { from: 1002, to: 1000 },
  { from: 1002, to: 1038 },
  { from: 1002, to: 1015, sea: true },
  { from: 1011, to: 1015, sea: true },
  { from: 1000, to: 1001 },
  { from: 1002, to: 1005 },
]

export const WORLD_NODES_BY_ID: ReadonlyMap<number, WorldNode> = new Map(WORLD_NODES.map((n) => [n.mapId, n]))

export function heatIntensity(players: number, maxPlayers: number): number {
  if (players <= 0 || maxPlayers <= 0) return 0
  return Math.min(1, Math.max(0, Math.sqrt(players / maxPlayers)))
}

export function heatRadius(players: number, maxPlayers: number): number {
  const intensity = heatIntensity(players, maxPlayers)
  if (intensity === 0) return 0
  return Math.round(24 + intensity * 40)
}

export function heatTier(players: number, maxPlayers: number): HeatTier {
  if (players <= 0 || maxPlayers <= 0) return 'quiet'
  const ratio = players / maxPlayers
  if (ratio >= 0.7) return 'crowded'
  if (ratio >= 0.35) return 'busy'
  return 'warm'
}

export interface PlacedHeatmapEntry {
  node: WorldNode
  entry: HeatmapMapItem
}

export function partitionWorldMaps(maps: HeatmapMapItem[]): {
  onWorld: PlacedHeatmapEntry[]
  other: HeatmapMapItem[]
} {
  const onWorld: PlacedHeatmapEntry[] = []
  const other: HeatmapMapItem[] = []
  for (const entry of maps) {
    const node = WORLD_NODES_BY_ID.get(entry.mapId)
    if (node) onWorld.push({ node, entry })
    else other.push(entry)
  }
  return { onWorld, other }
}
