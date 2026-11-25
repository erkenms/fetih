import { HoverCard } from '@mantine/core'
import type { TFunction } from 'i18next'
import type { CSSProperties, ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import {
  buildTooltipModel,
  qualityColor,
  type CatalogItem,
  type DecodedSocket,
  type ItemInstance,
  type TooltipRequirement,
} from './itemMeta'

export interface ItemTooltipContentProps {
  item: CatalogItem
  instance?: ItemInstance
}

function formatSocket(socket: DecodedSocket, t: TFunction<'items'>): string {
  if (socket.type === 'empty') return t('gem.empty')
  if (socket.type === 'unknown' || socket.kind === undefined || socket.grade === undefined) {
    return t('gem.unknown', { value: socket.raw })
  }
  const grade = t(`gem.grade.${socket.grade}` as never)
  const kind = t(`gem.kind.${socket.kind}` as never, { defaultValue: `#${socket.kind}` })
  const effect =
    socket.percent && socket.percent > 0
      ? ` (${t(`gem.effect.${socket.kind}` as never, { percent: socket.percent, defaultValue: `+${socket.percent}%` })})`
      : ''
  return `${grade} ${kind}${effect}`
}

function formatRequirement(req: TooltipRequirement, t: TFunction<'items'>): string {
  if (req.key === 'profession') {
    return t('tooltip.profession', { value: t(req.value as never) })
  }
  if (req.key === 'gender') {
    return t('tooltip.gender', { value: t(`gender.${req.value}` as never) })
  }
  return t(`tooltip.${req.key}`, { value: req.value })
}

// The client's item tip: plain lines on a see-through dark box with a thin light border, the
// name in the item's quality color and everything else white.
const tipBox: CSSProperties = {
  background: 'rgba(0, 0, 0, 0.72)',
  border: '1px solid #c8c8c8',
  borderRadius: 0,
  padding: '4px 8px',
  color: '#ffffff',
  font: '12px/1.35 Tahoma, Verdana, Arial, sans-serif',
  minWidth: 160,
  maxWidth: 300,
}

export function ItemTooltipContent({ item, instance }: ItemTooltipContentProps) {
  const { t } = useTranslation('items')
  const model = buildTooltipModel(item, instance)
  const line = (label: string, value: ReactNode) => t('tooltip.line', { label, value })

  const lines: { key: string; text: string; color?: string }[] = [
    { key: 'type', text: t('tooltip.type', { value: model.type || t(`category.${model.category}`) }) },
    ...model.requirements.map((req) => ({ key: `req-${req.key}`, text: formatRequirement(req, t) })),
    ...model.stats.map((stat) => ({ key: stat.key, text: line(t(`tooltip.${stat.key}`), stat.value) })),
  ]
  if (model.durability)
    lines.push({
      key: 'durability',
      text: line(
        t(model.durability.isArrow ? 'tooltip.arrows' : 'tooltip.durability'),
        `${model.durability.current}/${model.durability.max}`,
      ),
    })
  if (model.bless > 0) lines.push({ key: 'bless', text: line(t('tooltip.bless'), `-${model.bless}%`) })
  if (model.enchant > 0) lines.push({ key: 'enchant', text: line(t('tooltip.enchant'), `+${model.enchant}`) })
  model.sockets.forEach((socket, idx) => lines.push({ key: `socket-${idx}`, text: formatSocket(socket, t) }))
  if (model.price > 0) lines.push({ key: 'price', text: t('tooltip.price', { value: model.price.toLocaleString() }) })
  if (model.emoneyPrice > 0)
    lines.push({ key: 'cp', text: t('tooltip.emoneyPrice', { value: model.emoneyPrice.toLocaleString() }) })
  if (model.stackLimit > 1) lines.push({ key: 'stack', text: t('tooltip.stackLimit', { value: model.stackLimit }) })
  if (model.bound) lines.push({ key: 'bound', text: t('tooltip.bound') })
  if (model.locked) lines.push({ key: 'locked', text: t('tooltip.locked') })

  return (
    <div style={tipBox}>
      <div style={{ color: qualityColor(model.quality) }}>
        {model.name}
        {model.plus > 0 ? `(+${model.plus})` : ''}
      </div>
      {lines.map((l) => (
        <div key={l.key}>{l.text}</div>
      ))}
      {model.description && <div style={{ whiteSpace: 'pre-wrap', marginTop: 2 }}>{model.description}</div>}
    </div>
  )
}

export interface ItemTooltipProps {
  item: CatalogItem
  instance?: ItemInstance
  children: ReactNode
}

export function ItemTooltip({ item, instance, children }: ItemTooltipProps) {
  return (
    <HoverCard openDelay={150} closeDelay={80} position="right-start" offset={6}>
      <HoverCard.Target>{children}</HoverCard.Target>
      <HoverCard.Dropdown p={0} style={{ background: 'transparent', border: 0, boxShadow: 'none' }}>
        <ItemTooltipContent item={item} instance={instance} />
      </HoverCard.Dropdown>
    </HoverCard>
  )
}
