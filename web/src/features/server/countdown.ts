const STANDARD_MILESTONES = [3600, 1800, 900, 600, 300, 180, 120, 60, 30, 10, 5, 4, 3, 2, 1]

export function remainingSeconds(shutdownAt: string | null | undefined, nowMs: number): number {
  if (!shutdownAt) return 0
  const target = Date.parse(shutdownAt)
  if (Number.isNaN(target)) return 0
  return Math.max(0, Math.ceil((target - nowMs) / 1000))
}

export function formatClock(totalSeconds: number): string {
  const clamped = Math.max(0, Math.floor(totalSeconds))
  const hours = Math.floor(clamped / 3600)
  const minutes = Math.floor((clamped % 3600) / 60)
  const seconds = clamped % 60
  const pad = (n: number) => String(n).padStart(2, '0')
  return hours > 0 ? `${hours}:${pad(minutes)}:${pad(seconds)}` : `${pad(minutes)}:${pad(seconds)}`
}

export function milestoneLabels(totalSeconds: number): string[] {
  const points = [totalSeconds, ...STANDARD_MILESTONES.filter((m) => m < totalSeconds)]
  const labels: string[] = []
  let lowRun = false
  for (const s of points) {
    if (s <= 5) {
      if (!lowRun) {
        labels.push(s === 1 ? '1s' : `${s}–1s`)
        lowRun = true
      }
      continue
    }
    if (s >= 3600 && s % 3600 === 0) labels.push(`${s / 3600}h`)
    else if (s >= 60 && s % 60 === 0) labels.push(`${s / 60}m`)
    else labels.push(`${s}s`)
  }
  return labels
}

export function motdLines(text: string): string[] {
  return text
    .split('\n')
    .map((l) => l.trim())
    .filter((l) => l.length > 0)
}
