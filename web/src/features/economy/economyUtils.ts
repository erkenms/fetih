export type EconomyRange = '1h' | '24h' | '7d' | '30d'

export interface NumericPoint {
  timestamp: string
  value: number
}

export interface FlowBucketPoint {
  timestamp: string
  spentAtNpcs: number
  earnedFromNpcs: number
  pickedUpSilver: number
  netFlow: number
}

export function formatCompact(value: number): string {
  const abs = Math.abs(value)
  const sign = value < 0 ? '-' : ''
  if (abs >= 1_000_000_000) return `${sign}${(abs / 1_000_000_000).toFixed(2)}B`
  if (abs >= 1_000_000) return `${sign}${(abs / 1_000_000).toFixed(2)}M`
  if (abs >= 1_000) return `${sign}${(abs / 1_000).toFixed(1)}K`
  return `${sign}${Math.round(abs)}`
}

export function buildPolyline(values: number[], width: number, height: number, maxVal?: number): string {
  if (values.length === 0) return ''
  const peak = maxVal ?? Math.max(...values, 1)
  const safePeak = peak <= 0 ? 1 : peak
  if (values.length === 1) {
    const y = height - (values[0] / safePeak) * height
    return `0,${y.toFixed(1)} ${width},${y.toFixed(1)}`
  }
  const stepX = width / (values.length - 1)
  return values
    .map((v, i) => {
      const x = i * stepX
      const y = height - (Math.max(0, v) / safePeak) * height
      return `${x.toFixed(1)},${y.toFixed(1)}`
    })
    .join(' ')
}

export function maxFlowScale(buckets: FlowBucketPoint[]): number {
  let peak = 0
  for (const b of buckets) {
    const inflow = b.earnedFromNpcs + b.pickedUpSilver
    if (inflow > peak) peak = inflow
    if (b.spentAtNpcs > peak) peak = b.spentAtNpcs
  }
  return peak > 0 ? peak : 1
}
