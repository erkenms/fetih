export interface ScheduleInput {
  enabled: boolean
  intervalMinutes: number
  startAt?: string | null
  endAt?: string | null
  lastSentAt?: string | null
}

export type AnnouncementState = 'disabled' | 'scheduled' | 'active' | 'expired'

export function announcementState(item: ScheduleInput, now: Date): AnnouncementState {
  if (!item.enabled) return 'disabled'
  const nowMs = now.getTime()
  if (item.startAt && nowMs < new Date(item.startAt).getTime()) return 'scheduled'
  if (item.endAt && nowMs >= new Date(item.endAt).getTime()) return 'expired'
  return 'active'
}

export function nextRunAt(item: ScheduleInput, now: Date): Date | null {
  if (!item.enabled || item.intervalMinutes <= 0) return null
  const nowMs = now.getTime()
  const endMs = item.endAt ? new Date(item.endAt).getTime() : null
  if (endMs !== null && nowMs >= endMs) return null

  const startMs = item.startAt ? new Date(item.startAt).getTime() : null
  let candidateMs: number
  if (!item.lastSentAt) {
    candidateMs = startMs !== null && startMs > nowMs ? startMs : nowMs
  } else {
    candidateMs = new Date(item.lastSentAt).getTime() + item.intervalMinutes * 60_000
    if (startMs !== null && candidateMs < startMs) candidateMs = startMs
    if (candidateMs < nowMs) candidateMs = nowMs
  }

  if (endMs !== null && candidateMs >= endMs) return null
  return new Date(candidateMs)
}
