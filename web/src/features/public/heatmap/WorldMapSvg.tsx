import { useTranslation } from 'react-i18next'
import classes from './HeatmapPage.module.css'
import {
  heatRadius,
  heatTier,
  WORLD_HEIGHT,
  WORLD_NODES,
  WORLD_NODES_BY_ID,
  WORLD_ROUTES,
  WORLD_WIDTH,
  type HeatmapMapItem,
  type HeatTier,
} from './worldMapData'

interface WorldMapSvgProps {
  maps: HeatmapMapItem[]
  selectedMapId: number | null
  onSelect: (mapId: number | null) => void
}

const TIER_GRADIENT: Record<Exclude<HeatTier, 'quiet'>, string> = {
  warm: 'url(#heat-warm)',
  busy: 'url(#heat-busy)',
  crowded: 'url(#heat-crowded)',
}

const TIER_BADGE_FILL: Record<Exclude<HeatTier, 'quiet'>, string> = {
  warm: '#8f4f12',
  busy: '#a12a16',
  crowded: '#6e100d',
}

export function WorldMapSvg({ maps, selectedMapId, onSelect }: WorldMapSvgProps) {
  const { t } = useTranslation('publicHeatmap')
  const activeById = new Map(maps.map((m) => [m.mapId, m]))
  const maxPlayers = maps.reduce((acc, m) => Math.max(acc, m.players), 0)

  const visibleNodes = WORLD_NODES.filter((n) => n.major || activeById.has(n.mapId))
  const activeNodes = visibleNodes.filter((n) => activeById.has(n.mapId))

  return (
    <div className={classes.mapFrame}>
      <svg
        viewBox={`0 0 ${WORLD_WIDTH} ${WORLD_HEIGHT}`}
        className={classes.mapSvg}
        role="img"
        aria-label={t('map.ariaLabel')}
      >
        <defs>
          <radialGradient id="sea-bg" cx="50%" cy="48%" r="72%">
            <stop offset="0%" stopColor="#c4b28a" />
            <stop offset="70%" stopColor="#ae9a71" />
            <stop offset="100%" stopColor="#947f57" />
          </radialGradient>
          <linearGradient id="land-main" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#e8d6ac" />
            <stop offset="55%" stopColor="#dcc491" />
            <stop offset="100%" stopColor="#caa971" />
          </linearGradient>
          <radialGradient id="heat-warm" cx="50%" cy="50%" r="50%">
            <stop offset="0%" stopColor="#d98a1f" stopOpacity="0.62" />
            <stop offset="60%" stopColor="#c96f16" stopOpacity="0.28" />
            <stop offset="100%" stopColor="#c96f16" stopOpacity="0" />
          </radialGradient>
          <radialGradient id="heat-busy" cx="50%" cy="50%" r="50%">
            <stop offset="0%" stopColor="#c43d18" stopOpacity="0.72" />
            <stop offset="55%" stopColor="#c43d18" stopOpacity="0.34" />
            <stop offset="100%" stopColor="#c43d18" stopOpacity="0" />
          </radialGradient>
          <radialGradient id="heat-crowded" cx="50%" cy="50%" r="50%">
            <stop offset="0%" stopColor="#9e1410" stopOpacity="0.82" />
            <stop offset="55%" stopColor="#b82914" stopOpacity="0.4" />
            <stop offset="100%" stopColor="#b82914" stopOpacity="0" />
          </radialGradient>
        </defs>

        {/* Sea backdrop & subtle cartographic grid */}
        <rect x="0" y="0" width={WORLD_WIDTH} height={WORLD_HEIGHT} fill="url(#sea-bg)" />
        <g stroke="#7d6741" strokeOpacity="0.18" strokeWidth="1">
          {[160, 320, 480, 640, 800].map((x) => (
            <line key={`vx-${x}`} x1={x} y1={0} x2={x} y2={WORLD_HEIGHT} />
          ))}
          {[140, 280, 420].map((y) => (
            <line key={`hy-${y}`} x1={0} y1={y} x2={WORLD_WIDTH} y2={y} />
          ))}
        </g>

        {/* Mainland continent */}
        <path
          d="M 105 115
             C 155 62, 295 55, 395 82
             C 455 64, 545 78, 595 125
             C 640 168, 650 238, 635 288
             C 678 320, 742 355, 768 422
             C 782 465, 745 505, 670 508
             C 595 512, 545 488, 495 445
             C 442 468, 365 508, 255 505
             C 182 502, 152 455, 175 402
             C 202 352, 232 325, 222 280
             C 175 262, 130 228, 118 182
             C 108 152, 92 132, 105 115 Z"
          fill="url(#land-main)"
          stroke="#6b4e2a"
          strokeWidth="2.2"
        />

        {/* Bird Island */}
        <path
          d="M 742 155
             C 772 122, 838 122, 872 158
             C 898 188, 888 242, 845 262
             C 800 282, 748 265, 732 225
             C 722 198, 725 174, 742 155 Z"
          fill="url(#land-main)"
          stroke="#6b4e2a"
          strokeWidth="2.2"
        />

        {/* Prison island */}
        <path
          d="M 86 306
             C 104 288, 142 290, 156 312
             C 166 332, 152 358, 124 360
             C 96 362, 76 340, 86 306 Z"
          fill="url(#land-main)"
          stroke="#6b4e2a"
          strokeWidth="2"
        />

        {/* Subtle region labels */}
        <g className={classes.regionWatermark} aria-hidden="true">
          <text x="200" y="96">DESERT</text>
          <text x="485" y="210">CENTRAL PLAIN</text>
          <text x="695" y="362">PHOENIX WOODS</text>
          <text x="255" y="366">APE CANYON</text>
          <text x="805" y="146">EASTERN SEA</text>
        </g>

        {/* Trade & sea routes */}
        <g stroke="#6a4925" strokeOpacity="0.48" strokeWidth="1.5" fill="none">
          {WORLD_ROUTES.map((route) => {
            const a = WORLD_NODES_BY_ID.get(route.from)
            const b = WORLD_NODES_BY_ID.get(route.to)
            if (!a || !b) return null
            return (
              <line
                key={`${route.from}-${route.to}`}
                x1={a.x}
                y1={a.y}
                x2={b.x}
                y2={b.y}
                strokeDasharray={route.sea ? '4 5' : '6 4'}
              />
            )
          })}
        </g>

        {/* Compass rose */}
        <g transform="translate(888, 488)" opacity="0.65" aria-hidden="true">
          <circle cx="0" cy="0" r="28" fill="none" stroke="#573b1d" strokeWidth="1.2" />
          <polygon points="0,-24 5,-4 0,-8 -5,-4" fill="#6e1b18" />
          <polygon points="0,24 5,4 0,8 -5,4" fill="#573b1d" />
          <polygon points="24,0 4,5 8,0 4,-5" fill="#573b1d" />
          <polygon points="-24,0 -4,5 -8,0 -4,-5" fill="#573b1d" />
          <text x="0" y="-31" textAnchor="middle" fontSize="10" fontWeight="bold" fill="#4a2e14">
            N
          </text>
        </g>

        {/* Heat blobs behind markers */}
        <g pointerEvents="none">
          {activeNodes.map((node) => {
            const entry = activeById.get(node.mapId)!
            const tier = heatTier(entry.players, maxPlayers)
            if (tier === 'quiet') return null
            const r = heatRadius(entry.players, maxPlayers)
            return (
              <circle
                key={`heat-${node.mapId}`}
                cx={node.x}
                cy={node.y}
                r={r}
                fill={TIER_GRADIENT[tier]}
              />
            )
          })}
        </g>

        {/* Map nodes */}
        {visibleNodes.map((node) => {
          const entry = activeById.get(node.mapId)
          const isSelected = selectedMapId === node.mapId
          const tier = entry ? heatTier(entry.players, maxPlayers) : 'quiet'
          const label = entry?.name ?? node.fallbackName

          return (
            <g
              key={node.mapId}
              transform={`translate(${node.x}, ${node.y})`}
              className={entry ? classes.nodeInteractive : classes.nodeQuiet}
              onClick={() => entry && onSelect(isSelected ? null : node.mapId)}
              role={entry ? 'button' : undefined}
              tabIndex={entry ? 0 : undefined}
              onKeyDown={(e) => {
                if (entry && (e.key === 'Enter' || e.key === ' ')) {
                  e.preventDefault()
                  onSelect(isSelected ? null : node.mapId)
                }
              }}
            >
              <title>
                {entry
                  ? `${label}: ${t('map.playersTooltip', { count: entry.players, pct: Math.round(entry.share * 100) })}`
                  : `${label}: ${t('map.quietTooltip')}`}
              </title>

              {isSelected && (
                <circle cx="0" cy="0" r={entry ? 20 : 12} fill="none" stroke="#fff0c0" strokeWidth="2.5" />
              )}

              {entry && tier !== 'quiet' ? (
                <>
                  <circle
                    cx="0"
                    cy="0"
                    r={14}
                    fill={TIER_BADGE_FILL[tier]}
                    stroke="#f5dfa3"
                    strokeWidth="2"
                  />
                  <text
                    x="0"
                    y="4"
                    textAnchor="middle"
                    className={classes.countText}
                  >
                    {entry.players}
                  </text>
                </>
              ) : (
                <circle cx="0" cy="0" r={5} fill="#5e4223" stroke="#e8d6ac" strokeWidth="1.5" />
              )}

              <text
                x="0"
                y={entry ? 27 : 18}
                textAnchor="middle"
                className={entry ? classes.labelActive : classes.labelQuiet}
              >
                {label}
              </text>
            </g>
          )
        })}
      </svg>

      <div className={classes.legend}>
        <span className={classes.legendItem}>
          <span className={`${classes.swatch} ${classes.swatchQuiet}`} />
          {t('legend.quiet')}
        </span>
        <span className={classes.legendItem}>
          <span className={`${classes.swatch} ${classes.swatchWarm}`} />
          {t('legend.warm')}
        </span>
        <span className={classes.legendItem}>
          <span className={`${classes.swatch} ${classes.swatchBusy}`} />
          {t('legend.busy')}
        </span>
        <span className={classes.legendItem}>
          <span className={`${classes.swatch} ${classes.swatchCrowded}`} />
          {t('legend.crowded')}
        </span>
      </div>
    </div>
  )
}
