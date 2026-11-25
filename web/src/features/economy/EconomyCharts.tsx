import { Group, Stack, Text } from '@mantine/core'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import classes from './EconomyPage.module.css'
import { buildPolyline, formatCompact, maxFlowScale, type FlowBucketPoint, type NumericPoint } from './economyUtils'

const CHART_W = 640
const CHART_H = 160
const PAD_LEFT = 54
const PAD_RIGHT = 12
const PAD_TOP = 12
const PAD_BOTTOM = 22
const PLOT_W = CHART_W - PAD_LEFT - PAD_RIGHT
const PLOT_H = CHART_H - PAD_TOP - PAD_BOTTOM

export function MoneyFlowChart({
  buckets,
  formatTime,
}: {
  buckets: FlowBucketPoint[]
  formatTime: (iso: string) => string
}) {
  const { t } = useTranslation('economy')
  const [hoverIdx, setHoverIdx] = useState<number | null>(null)

  if (buckets.length === 0) {
    return (
      <Text size="xs" c="dimmed">
        {t('flow.noData')}
      </Text>
    )
  }

  const peak = maxFlowScale(buckets)
  const inflows = buckets.map((b) => b.earnedFromNpcs + b.pickedUpSilver)
  const spent = buckets.map((b) => b.spentAtNpcs)

  const inflowPath = buildPolyline(inflows, PLOT_W, PLOT_H, peak)
  const spentPath = buildPolyline(spent, PLOT_W, PLOT_H, peak)
  const stepX = buckets.length > 1 ? PLOT_W / (buckets.length - 1) : PLOT_W
  const active = hoverIdx != null ? buckets[hoverIdx] : null

  return (
    <Stack gap={6}>
      <div className={classes.chartBox}>
        <svg viewBox={`0 0 ${CHART_W} ${CHART_H}`} className={classes.chartSvg}>
          <g transform={`translate(${PAD_LEFT}, ${PAD_TOP})`}>
            {(peak <= 1 ? [0, 1] : [0, 0.5, 1]).map((frac) => {
              const y = PLOT_H - frac * PLOT_H
              return (
                <g key={frac}>
                  <line x1={0} y1={y} x2={PLOT_W} y2={y} stroke="#9c7e52" strokeOpacity="0.45" strokeDasharray="3 3" />
                  <text x={-6} y={y + 3} textAnchor="end" fontSize="10" fill="#5b4024">
                    {formatCompact(peak * frac)}
                  </text>
                </g>
              )
            })}

            <polyline fill="none" stroke="#1f6f2b" strokeWidth="2.2" points={inflowPath} />
            <polyline fill="none" stroke="#9e1b16" strokeWidth="2.2" points={spentPath} />

            {buckets.map((b, i) => {
              const x = buckets.length > 1 ? i * stepX : PLOT_W / 2
              const yIn = PLOT_H - ((b.earnedFromNpcs + b.pickedUpSilver) / peak) * PLOT_H
              const yOut = PLOT_H - (b.spentAtNpcs / peak) * PLOT_H
              const isHover = hoverIdx === i
              return (
                <g key={b.timestamp} onMouseEnter={() => setHoverIdx(i)} onMouseLeave={() => setHoverIdx(null)}>
                  <rect
                    x={Math.max(0, x - stepX / 2)}
                    y={0}
                    width={Math.max(stepX, 12)}
                    height={PLOT_H}
                    fill="transparent"
                  />
                  {isHover && (
                    <line x1={x} y1={0} x2={x} y2={PLOT_H} stroke="#4a3019" strokeOpacity="0.4" strokeWidth="1" />
                  )}
                  <circle cx={x} cy={yIn} r={isHover ? 4 : 2.5} fill="#1f6f2b" />
                  <circle cx={x} cy={yOut} r={isHover ? 4 : 2.5} fill="#9e1b16" />
                </g>
              )
            })}

            <text x={0} y={PLOT_H + 16} fontSize="10" fill="#5b4024">
              {formatTime(buckets[0].timestamp)}
            </text>
            <text x={PLOT_W} y={PLOT_H + 16} textAnchor="end" fontSize="10" fill="#5b4024">
              {formatTime(buckets[buckets.length - 1].timestamp)}
            </text>
          </g>
        </svg>
      </div>

      <Group justify="space-between" wrap="wrap" gap="xs">
        <Group gap="md">
          <span className={classes.legendItem}>
            <span className={classes.swatchIn} />
            {t('flow.legendIn')}
          </span>
          <span className={classes.legendItem}>
            <span className={classes.swatchOut} />
            {t('flow.legendOut')}
          </span>
        </Group>
        {active && (
          <Text size="xs" c="dimmed">
            {formatTime(active.timestamp)} · {t('flow.npcSales')}: {formatCompact(active.earnedFromNpcs)} ·{' '}
            {t('flow.pickups')}: {formatCompact(active.pickedUpSilver)} · {t('flow.spent')}:{' '}
            {formatCompact(active.spentAtNpcs)}
          </Text>
        )}
      </Group>
    </Stack>
  )
}

export function SupplyTrendChart({
  points,
  color,
  emptyText,
  formatTime,
}: {
  points: NumericPoint[]
  color: string
  emptyText: string
  formatTime: (iso: string) => string
}) {
  if (points.length === 0) {
    return (
      <Text size="xs" c="dimmed">
        {emptyText}
      </Text>
    )
  }

  const values = points.map((p) => p.value)
  const peak = Math.max(...values, 1)
  const poly = buildPolyline(values, PLOT_W, PLOT_H, peak)

  return (
    <div className={classes.chartBox}>
      <svg viewBox={`0 0 ${CHART_W} ${CHART_H}`} className={classes.chartSvg}>
        <g transform={`translate(${PAD_LEFT}, ${PAD_TOP})`}>
          {[0, 0.5, 1].map((frac) => {
            const y = PLOT_H - frac * PLOT_H
            return (
              <g key={frac}>
                <line x1={0} y1={y} x2={PLOT_W} y2={y} stroke="#9c7e52" strokeOpacity="0.45" strokeDasharray="3 3" />
                <text x={-6} y={y + 3} textAnchor="end" fontSize="10" fill="#5b4024">
                  {formatCompact(peak * frac)}
                </text>
              </g>
            )
          })}
          <polyline fill="none" stroke={color} strokeWidth="2.2" points={poly} />
          <text x={0} y={PLOT_H + 16} fontSize="10" fill="#5b4024">
            {formatTime(points[0].timestamp)}
          </text>
          <text x={PLOT_W} y={PLOT_H + 16} textAnchor="end" fontSize="10" fill="#5b4024">
            {formatTime(points[points.length - 1].timestamp)}
          </text>
        </g>
      </svg>
    </div>
  )
}
