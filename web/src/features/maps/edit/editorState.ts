export interface EditablePoint {
  x: number
  y: number
}

export interface EditableMapSettingsState {
  inMapsJson: boolean
  name: string | null
  aliases: string[]
  flags: string[]
  portal: EditablePoint | null
  rebornMap: number | null
  scrollTypeId: number | null
  ridePlus: number
}

export interface EditableNpcState {
  id: number
  lookface: number
  role: number
  x: number
  y: number
  life: number
  level: number
  name?: string | null
}

export interface EditableSpawnState {
  id: number
  x: number
  y: number
  spread: number
  amount: number
  respawn: number
  monsterId: number
  monsterName?: string | null
  monsterLevel?: number | null
  monsterBoss?: boolean
}

export interface EditablePortalState {
  index: number
  x: number | null
  y: number | null
  toMap: number | null
  toMapName: string | null
  toX: number | null
  toY: number | null
}

export interface MapEditorSnapshot {
  settings: EditableMapSettingsState
  npcs: EditableNpcState[]
  spawns: EditableSpawnState[]
  portals: EditablePortalState[]
}

export interface EditorHistory<T> {
  past: T[]
  present: T
  future: T[]
  saved: T
}

export const MAX_HISTORY = 100

export function createHistory<T>(initial: T): EditorHistory<T> {
  return {
    past: [],
    present: initial,
    future: [],
    saved: initial,
  }
}

export function pushHistory<T>(history: EditorHistory<T>, next: T): EditorHistory<T> {
  if (snapshotsEqual(history.present, next)) return history
  const past = [...history.past, history.present]
  if (past.length > MAX_HISTORY) past.shift()
  return {
    past,
    present: next,
    future: [],
    saved: history.saved,
  }
}

export function undoHistory<T>(history: EditorHistory<T>): EditorHistory<T> {
  if (history.past.length === 0) return history
  const previous = history.past[history.past.length - 1]!
  return {
    past: history.past.slice(0, -1),
    present: previous,
    future: [history.present, ...history.future],
    saved: history.saved,
  }
}

export function redoHistory<T>(history: EditorHistory<T>): EditorHistory<T> {
  if (history.future.length === 0) return history
  const next = history.future[0]!
  return {
    past: [...history.past, history.present],
    present: next,
    future: history.future.slice(1),
    saved: history.saved,
  }
}

export function markSaved<T>(history: EditorHistory<T>): EditorHistory<T> {
  return {
    ...history,
    saved: history.present,
  }
}

export function canUndo<T>(history: EditorHistory<T>): boolean {
  return history.past.length > 0
}

export function canRedo<T>(history: EditorHistory<T>): boolean {
  return history.future.length > 0
}

export function snapshotsEqual<T>(a: T, b: T): boolean {
  return a === b || JSON.stringify(a) === JSON.stringify(b)
}

export function isDirty<T>(history: EditorHistory<T>): boolean {
  return !snapshotsEqual(history.present, history.saved)
}

export function nextAvailableId(existingIds: readonly number[], suggestedMin: number): number {
  let max = Math.max(0, suggestedMin - 1)
  for (const id of existingIds) {
    if (id > max) max = id
  }
  return max + 1
}

export function cellDistance(ax: number, ay: number, bx: number, by: number): number {
  return Math.hypot(bx - ax, by - ay)
}

export function clampSpread(distance: number): number {
  if (!Number.isFinite(distance)) return 0
  return Math.max(0, Math.min(255, Math.round(distance)))
}

export function findDuplicateIds(ids: readonly number[]): number[] {
  const seen = new Set<number>()
  const dupes = new Set<number>()
  for (const id of ids) {
    if (seen.has(id)) dupes.add(id)
    else seen.add(id)
  }
  return [...dupes].sort((a, b) => a - b)
}
