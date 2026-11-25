export interface LiveMapPlayer {
  characterID: number
  name: string
  level: number
  class: number
  x: number
  y: number
  pkPoints: number
  nameColor: string
  dead: boolean
  health: number
  maxHealth: number
  mesh: number
}

export interface LiveMapMonster {
  id: number
  typeID: number
  name: string
  level: number
  lookface: number
  x: number
  y: number
  alive: boolean
  health: number
  maxHealth: number
  guard: boolean
}

export interface LiveGroundItem {
  id: number
  typeId: number
  itemName: string | null
  icon: string | null
  x: number
  y: number
  silver: number
  expiresAt: string
}

export interface LiveMapBooth {
  id: number
  ownerID: number
  ownerName: string
  x: number
  y: number
  items: number
  hawk: string | null
}

export interface LiveOnlinePlayer {
  characterId: number
  accountId: number
  name: string
  level: number
  class: number
  rebirths: number
  mapId: number
  mapName: string | null
  x: number
  y: number
  pkPoints: number
  authority: number
  ipAddress: string
  loggedInAt: string | null
}

export interface OnlinePlayersResponse {
  gameOnline: boolean
  takenAt: string
  players: LiveOnlinePlayer[]
}

export interface LiveMapSnapshot {
  mapId: number
  width: number
  height: number
  takenAt: string
  gameOnline: boolean
  exists: boolean
  players: LiveMapPlayer[]
  monsters: LiveMapMonster[]
  groundItems: LiveGroundItem[]
  booths: LiveMapBooth[]
}

export interface LiveMapDelta {
  mapId: number
  takenAt: string
  gameOnline: boolean
  playersUpsert: LiveMapPlayer[]
  playersRemoved: number[]
  monstersUpsert: LiveMapMonster[]
  monstersRemoved: number[]
  groundItemsUpsert: LiveGroundItem[]
  groundItemsRemoved: number[]
  boothsUpsert: LiveMapBooth[]
  boothsRemoved: number[]
}

export interface LiveMapState {
  mapId: number
  width: number
  height: number
  takenAt: string
  gameOnline: boolean
  exists: boolean
  players: Record<number, LiveMapPlayer>
  monsters: Record<number, LiveMapMonster>
  groundItems: Record<number, LiveGroundItem>
  booths: Record<number, LiveMapBooth>
}

export type SelectedLiveEntity =
  | { kind: 'player'; id: number }
  | { kind: 'monster'; id: number }
  | { kind: 'item'; id: number }
  | { kind: 'booth'; id: number }

export interface LiveLayersState {
  players: boolean
  monsters: boolean
  deadMonsters: boolean
  groundItems: boolean
  booths: boolean
  covers: boolean
  walkability: boolean
  portals: boolean
  npcs: boolean
}

export const defaultLiveLayers: LiveLayersState = {
  players: true,
  monsters: true,
  deadMonsters: false,
  groundItems: true,
  booths: true,
  covers: true,
  walkability: false,
  portals: true,
  npcs: false,
}

export function emptyMapState(mapId: number): LiveMapState {
  return {
    mapId,
    width: 0,
    height: 0,
    takenAt: '',
    gameOnline: false,
    exists: false,
    players: {},
    monsters: {},
    groundItems: {},
    booths: {},
  }
}

function indexBy<T>(items: T[], keyOf: (item: T) => number): Record<number, T> {
  const out: Record<number, T> = {}
  for (const item of items) out[keyOf(item)] = item
  return out
}

function applyRecordDelta<T>(
  prev: Record<number, T>,
  upsert: T[],
  removed: number[],
  keyOf: (item: T) => number,
): Record<number, T> {
  if (upsert.length === 0 && removed.length === 0) return prev
  const next = { ...prev }
  for (const id of removed) delete next[id]
  for (const item of upsert) next[keyOf(item)] = item
  return next
}

export function snapshotToState(snapshot: LiveMapSnapshot): LiveMapState {
  return {
    mapId: snapshot.mapId,
    width: snapshot.width,
    height: snapshot.height,
    takenAt: snapshot.takenAt,
    gameOnline: snapshot.gameOnline,
    exists: snapshot.exists,
    players: indexBy(snapshot.players, (p) => p.characterID),
    monsters: indexBy(snapshot.monsters, (m) => m.id),
    groundItems: indexBy(snapshot.groundItems, (i) => i.id),
    booths: indexBy(snapshot.booths, (b) => b.id),
  }
}

export function applyMapDelta(state: LiveMapState, delta: LiveMapDelta): LiveMapState {
  if (state.mapId !== delta.mapId) return state
  return {
    ...state,
    takenAt: delta.takenAt,
    gameOnline: delta.gameOnline,
    players: applyRecordDelta(state.players, delta.playersUpsert, delta.playersRemoved, (p) => p.characterID),
    monsters: applyRecordDelta(state.monsters, delta.monstersUpsert, delta.monstersRemoved, (m) => m.id),
    groundItems: applyRecordDelta(state.groundItems, delta.groundItemsUpsert, delta.groundItemsRemoved, (i) => i.id),
    booths: applyRecordDelta(state.booths, delta.boothsUpsert, delta.boothsRemoved, (b) => b.id),
  }
}

export function hpPercent(health: number, maxHealth: number): number {
  if (maxHealth <= 0) return 0
  return Math.max(0, Math.min(100, Math.round((health / maxHealth) * 100)))
}

export interface PkVisualStyle {
  border: string
  text: string
  badgeColor: string
}

export function pkNameStyle(nameColor: string): PkVisualStyle {
  switch (nameColor) {
    case 'red':
      return { border: '#ef4444', text: '#fca5a5', badgeColor: 'red' }
    case 'black':
      return { border: '#94a3b8', text: '#94a3b8', badgeColor: 'dark' }
    case 'blue':
      return { border: '#38bdf8', text: '#7dd3fc', badgeColor: 'blue' }
    default:
      return { border: '#e2e8f0', text: '#f8fafc', badgeColor: 'gray' }
  }
}

export interface ActiveMapSummary {
  mapId: number
  mapName: string | null
  count: number
}

export function summarizeActiveMaps(players: LiveOnlinePlayer[]): ActiveMapSummary[] {
  const byMap = new Map<number, ActiveMapSummary>()
  for (const p of players) {
    const existing = byMap.get(p.mapId)
    if (existing) {
      existing.count += 1
    } else {
      byMap.set(p.mapId, { mapId: p.mapId, mapName: p.mapName, count: 1 })
    }
  }
  return [...byMap.values()].sort((a, b) => b.count - a.count || a.mapId - b.mapId)
}
