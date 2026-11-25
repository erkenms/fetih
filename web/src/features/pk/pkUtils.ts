export const RED_NAME_POINTS = 30
export const BLACK_NAME_POINTS = 100
export const JAIL_MAP_ID = 6000

export type PkNameColor = 'blue' | 'black' | 'red' | 'white'

export function pkNameColor(points: number, flashing = false): PkNameColor {
  if (flashing) return 'blue'
  if (points >= BLACK_NAME_POINTS) return 'black'
  if (points >= RED_NAME_POINTS) return 'red'
  return 'white'
}

export function pkBadgeColor(color: string): string {
  switch (color) {
    case 'blue':
      return 'blue'
    case 'red':
      return 'red'
    case 'black':
      return 'dark'
    default:
      return 'gray'
  }
}

export function detainedStatusColor(status: string): string {
  switch (status) {
    case 'redeemable':
      return 'yellow'
    case 'expired':
      return 'red'
    case 'redeemed':
      return 'green'
    default:
      return 'gray'
  }
}

export interface DurationParts {
  days: number
  hours: number
  minutes: number
}

export function durationBetween(fromIso: string | null | undefined, toMs: number): DurationParts | null {
  if (!fromIso) return null
  const fromMs = Date.parse(fromIso)
  if (Number.isNaN(fromMs)) return null
  const totalMinutes = Math.max(0, Math.floor((toMs - fromMs) / 60_000))
  const days = Math.floor(totalMinutes / (60 * 24))
  const hours = Math.floor((totalMinutes % (60 * 24)) / 60)
  const minutes = totalMinutes % 60
  return { days, hours, minutes }
}

export function timeRemaining(untilIso: string | null | undefined, nowMs: number): (DurationParts & { expired: boolean }) | null {
  if (!untilIso) return null
  const untilMs = Date.parse(untilIso)
  if (Number.isNaN(untilMs)) return null
  if (nowMs >= untilMs) return { expired: true, days: 0, hours: 0, minutes: 0 }
  const totalMinutes = Math.max(0, Math.ceil((untilMs - nowMs) / 60_000))
  const days = Math.floor(totalMinutes / (60 * 24))
  const hours = Math.floor((totalMinutes % (60 * 24)) / 60)
  const minutes = totalMinutes % 60
  return { expired: false, days, hours, minutes }
}

export function classKey(cls: number): string | null {
  if (cls >= 10 && cls <= 15) return 'trojan'
  if (cls >= 20 && cls <= 25) return 'warrior'
  if (cls >= 40 && cls <= 45) return 'archer'
  if (cls >= 50 && cls <= 55) return 'ninja'
  if (cls >= 60 && cls <= 65) return 'monk'
  if (cls >= 70 && cls <= 75) return 'pirate'
  if (cls >= 100 && cls <= 101) return 'taoist'
  if (cls >= 132 && cls <= 135) return 'waterTaoist'
  if (cls >= 142 && cls <= 145) return 'fireTaoist'
  return null
}
