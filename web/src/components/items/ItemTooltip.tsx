import { Divider, Group, HoverCard, Stack, Text } from '@mantine/core'
import type { TFunction } from 'i18next'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { ItemIcon } from './ItemIcon'
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

export function ItemTooltipContent({ item, instance }: ItemTooltipContentProps) {
  const { t } = useTranslation('items')
  const model = buildTooltipModel(item, instance)
  const titleColor = qualityColor(model.quality)
  const titleSuffix = model.plus > 0 ? ` +${model.plus}` : ''

  return (
    <Stack gap={6} p="xs" style={{ minWidth: 220, maxWidth: 300 }}>
      <Group gap="xs" wrap="nowrap" align="flex-start">
        <ItemIcon item={item} instance={instance} size={42} />
        <Stack gap={1} style={{ minWidth: 0 }}>
          <Text fw={700} size="sm" style={{ color: titleColor, lineHeight: 1.25 }}>
            {model.name}
            {titleSuffix}
          </Text>
          <Text size="xs" c="gray.4">
            {model.type || t(`category.${model.category}`)}
            {model.quality !== 'None' ? ` · ${t(`quality.${model.quality}`)}` : ''}
          </Text>
          <Text size="xs" c="gray.5">
            #{model.id}
          </Text>
        </Stack>
      </Group>

      {(model.stats.length > 0 || model.durability || model.bless > 0 || model.enchant > 0 || model.locked) && (
        <>
          <Divider color="dark.4" />
          <Stack gap={2}>
            {model.stats.map((stat) => (
              <Group key={stat.key} justify="space-between" gap="md">
                <Text size="xs" c="gray.4">
                  {t(`tooltip.${stat.key}`)}
                </Text>
                <Text size="xs" fw={600} c="gray.0">
                  {stat.value}
                </Text>
              </Group>
            ))}
            {model.durability && (
              <Group justify="space-between" gap="md">
                <Text size="xs" c="gray.4">
                  {t(model.durability.isArrow ? 'tooltip.arrows' : 'tooltip.durability')}
                </Text>
                <Text size="xs" fw={600} c="gray.0">
                  {model.durability.current}/{model.durability.max}
                </Text>
              </Group>
            )}
            {model.bless > 0 && (
              <Group justify="space-between" gap="md">
                <Text size="xs" c="cyan.3">
                  {t('tooltip.bless')}
                </Text>
                <Text size="xs" fw={600} c="cyan.3">
                  -{model.bless}%
                </Text>
              </Group>
            )}
            {model.enchant > 0 && (
              <Group justify="space-between" gap="md">
                <Text size="xs" c="grape.3">
                  {t('tooltip.enchant')}
                </Text>
                <Text size="xs" fw={600} c="grape.3">
                  +{model.enchant}
                </Text>
              </Group>
            )}
            {model.locked && (
              <Text size="xs" c="red.4" fw={600}>
                {t('tooltip.locked')}
              </Text>
            )}
          </Stack>
        </>
      )}

      {model.sockets.length > 0 && (
        <>
          <Divider color="dark.4" />
          <Stack gap={2}>
            <Text size="xs" c="gray.5" fw={600}>
              {t('tooltip.sockets')}
            </Text>
            {model.sockets.map((socket, idx) => (
              <Text key={idx} size="xs" c={socket.type === 'empty' ? 'gray.5' : 'teal.3'}>
                • {formatSocket(socket, t)}
              </Text>
            ))}
          </Stack>
        </>
      )}

      {model.requirements.length > 0 && (
        <>
          <Divider color="dark.4" />
          <Stack gap={2}>
            <Text size="xs" c="gray.5" fw={600}>
              {t('tooltip.requirements')}
            </Text>
            {model.requirements.map((req) => (
              <Text key={req.key} size="xs" c="yellow.3">
                {formatRequirement(req, t)}
              </Text>
            ))}
          </Stack>
        </>
      )}

      {(model.price > 0 || model.emoneyPrice > 0 || model.stackLimit > 1 || model.bound) && (
        <>
          <Divider color="dark.4" />
          <Stack gap={2}>
            {model.price > 0 && (
              <Text size="xs" c="gray.3">
                {t('tooltip.price', { value: model.price.toLocaleString() })}
              </Text>
            )}
            {model.emoneyPrice > 0 && (
              <Text size="xs" c="yellow.4">
                {t('tooltip.emoneyPrice', { value: model.emoneyPrice.toLocaleString() })}
              </Text>
            )}
            {model.stackLimit > 1 && (
              <Text size="xs" c="gray.4">
                {t('tooltip.stackLimit', { value: model.stackLimit })}
              </Text>
            )}
            {model.bound && (
              <Text size="xs" c="orange.4">
                {t('tooltip.bound')}
              </Text>
            )}
          </Stack>
        </>
      )}

      {model.description && (
        <>
          <Divider color="dark.4" />
          <Text size="xs" c="gray.2" style={{ whiteSpace: 'pre-wrap' }}>
            {model.description}
          </Text>
        </>
      )}
    </Stack>
  )
}

export interface ItemTooltipProps {
  item: CatalogItem
  instance?: ItemInstance
  children: ReactNode
}

export function ItemTooltip({ item, instance, children }: ItemTooltipProps) {
  return (
    <HoverCard openDelay={150} closeDelay={80} position="right-start" withArrow shadow="md">
      <HoverCard.Target>{children}</HoverCard.Target>
      <HoverCard.Dropdown
        p={4}
        style={{
          background: 'rgba(18, 20, 26, 0.96)',
          borderColor: 'rgba(255, 255, 255, 0.14)',
          color: '#f1f3f5',
        }}
      >
        <ItemTooltipContent item={item} instance={instance} />
      </HoverCard.Dropdown>
    </HoverCard>
  )
}
