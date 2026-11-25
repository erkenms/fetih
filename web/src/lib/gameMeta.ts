import { professionKey } from '../components/items/itemMeta'

export function classKey(classId: number): string {
  if (classId >= 10 && classId <= 15) return 'trojan'
  if (classId >= 20 && classId <= 25) return 'warrior'
  if (classId >= 40 && classId <= 45) return 'archer'
  if (classId >= 50 && classId <= 55) return 'ninja'
  if (classId >= 60 && classId <= 65) return 'monk'
  if (classId >= 70 && classId <= 75) return 'pirate'
  if (classId >= 132 && classId <= 135) return 'waterTaoist'
  if (classId >= 142 && classId <= 145) return 'fireTaoist'
  if (classId >= 100 && classId <= 101) return 'taoist'
  return 'unknown'
}

export const nobilityColors: Record<string, string> = {
  King: 'yellow',
  Prince: 'grape',
  Duke: 'blue',
  Earl: 'cyan',
  Baron: 'teal',
  Knight: 'gray',
}

export function formatProfession(
  cls: number,
  t: (key: never, options: { defaultValue: string }) => string,
  emptyLabel?: string,
): string {
  if (!cls && emptyLabel !== undefined) return emptyLabel
  const key = professionKey(cls)
  return key ? t(`items:${key}` as never, { defaultValue: `#${cls}` }) : `#${cls}`
}
