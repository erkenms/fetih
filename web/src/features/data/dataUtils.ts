export interface LevelRow {
  level: number
  experience: number
  upLevTime: number
  line?: number
}

export interface GoalRow {
  id: number
  lookface: number
  role: number
  level: number
  life: number
  map: number
  x: number
  y: number
  line?: number
}

export interface TeleporterDestRow {
  name: string
  map: number
  x: number
  y: number
}

export interface TeleporterRow {
  id: number
  fare: number
  face: number
  text: string[]
  destinations: TeleporterDestRow[]
  close: string | null
  line?: number
  npcMap?: number | null
  npcX?: number | null
  npcY?: number | null
}

export interface TalkerRow {
  id: number
  face: number
  text: string[]
  reply: string
  line?: number
  npcMap?: number | null
  npcX?: number | null
  npcY?: number | null
}

// An EXP ball is worth 600 units of upLevTime at the player's level (from levels.json).
export function expBallsForLevel(upLevTime: number): number {
  if (upLevTime <= 0) return 0
  return upLevTime / 600
}

export function nextLevelNumber(rows: LevelRow[]): number {
  if (rows.length === 0) return 1
  const max = rows.reduce((m, r) => (r.level > m ? r.level : m), 0)
  return Math.min(255, max + 1)
}

export function nextGoalId(rows: GoalRow[]): number {
  if (rows.length === 0) return 100011
  const max = rows.reduce((m, r) => (r.id > m ? r.id : m), 0)
  return max + 2
}

// Converts a normalized click position (0..1, 0..1) on a map preview to a cell coordinate.
export function normalizedToCell(
  normX: number,
  normY: number,
  width: number,
  height: number,
): { x: number; y: number } {
  const clampedX = Math.max(0, Math.min(1, normX))
  const clampedY = Math.max(0, Math.min(1, normY))
  return {
    x: Math.min(width - 1, Math.max(0, Math.floor(clampedX * width))),
    y: Math.min(height - 1, Math.max(0, Math.floor(clampedY * height))),
  }
}

export function cellToNormalized(
  x: number,
  y: number,
  width: number,
  height: number,
): { normX: number; normY: number } {
  if (width <= 0 || height <= 0) return { normX: 0, normY: 0 }
  return {
    normX: Math.max(0, Math.min(1, (x + 0.5) / width)),
    normY: Math.max(0, Math.min(1, (y + 0.5) / height)),
  }
}

export function textLinesToString(lines: string[]): string {
  return lines.join('\n')
}

export function stringToTextLines(raw: string): string[] {
  const parts = raw.split(/\r?\n/)
  // Trim trailing empty line only if there's more than one line or all empty
  if (parts.length === 1 && parts[0] === '') return []
  return parts
}
