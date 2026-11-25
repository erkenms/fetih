import { Anchor, Badge, Button, Group, Popover, Stack, Text } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { api, unwrap, type Schemas } from '../../api/client'
import { useErrorText } from '../../app/ErrorAlert'
import { classKey, durationBetween, pkBadgeColor, BLACK_NAME_POINTS, RED_NAME_POINTS } from './pkUtils'

export type PkCharacterRef = Schemas['PkCharacterRef']

export function useNumber() {
  const { i18n } = useTranslation()
  return (n: number) => n.toLocaleString(i18n.language)
}

export function useTime() {
  const { i18n } = useTranslation()
  return (time: string | null | undefined) => (time ? new Date(time).toLocaleString(i18n.language) : '—')
}

export function CharacterCell({ character }: { character: PkCharacterRef }) {
  const { t } = useTranslation('pk')
  const cls = classKey(character.class)
  return (
    <Group gap={6} wrap="nowrap">
      <Anchor component={Link} to={`/admin/characters/${character.id}`} size="sm" fw={500}>
        {character.name}
      </Anchor>
      {character.level > 0 && (
        <Text size="xs" c="dimmed">
          {t('levelClass', { level: character.level, class: cls ? t(`class.${cls}`) : '' })}
        </Text>
      )}
    </Group>
  )
}

export function PkStatusBadges({
  pkPoints,
  flashing,
  nameColor,
}: {
  pkPoints: number
  flashing?: boolean
  nameColor?: string
}) {
  const { t } = useTranslation('pk')
  const pointColor =
    pkPoints >= BLACK_NAME_POINTS ? 'black' : pkPoints >= RED_NAME_POINTS ? 'red' : 'white'
  const color = nameColor && nameColor !== 'blue' ? nameColor : pointColor

  return (
    <Group gap={4} wrap="nowrap">
      <Badge size="sm" variant={color === 'black' ? 'filled' : 'light'} color={pkBadgeColor(color)}>
        {t(`color.${color}`)}
      </Badge>
      {flashing && (
        <Badge size="sm" variant="light" color="blue">
          {t('color.blue')}
        </Badge>
      )}
    </Group>
  )
}

export function DurationText({ since }: { since: string | null | undefined }) {
  const { t } = useTranslation('pk')
  const parts = durationBetween(since, Date.now())
  if (!parts) return null
  if (parts.days > 0)
    return <Text size="xs" c="dimmed">{t('duration.daysHours', { days: parts.days, hours: parts.hours })}</Text>
  if (parts.hours > 0)
    return <Text size="xs" c="dimmed">{t('duration.hoursMinutes', { hours: parts.hours, minutes: parts.minutes })}</Text>
  return <Text size="xs" c="dimmed">{t('duration.minutes', { minutes: parts.minutes })}</Text>
}

export function ReleaseButton({
  character,
  disabled,
}: {
  character: PkCharacterRef
  disabled?: boolean
}) {
  const { t } = useTranslation('pk')
  const errorText = useErrorText()
  const queryClient = useQueryClient()
  const [opened, setOpened] = useState(false)

  const mutation = useMutation({
    mutationFn: () =>
      unwrap(api.POST('/api/pk/jail/{id}/release', { params: { path: { id: character.id } } })),
    onSuccess: async () => {
      setOpened(false)
      notifications.show({
        color: 'green',
        message: t('jail.released', { name: character.name }),
      })
      await queryClient.invalidateQueries({ queryKey: ['pk'] })
    },
    onError: (error) => {
      setOpened(false)
      const code = (error as { code?: string }).code
      notifications.show({
        color: 'red',
        message: t(`errors.${code}`, { defaultValue: errorText(error) }),
      })
    },
  })

  return (
    <Popover opened={opened} onChange={setOpened} withArrow shadow="md">
      <Popover.Target>
        <Button
          size="xs"
          color="teal"
          variant="light"
          disabled={disabled}
          onClick={() => setOpened((o) => !o)}
        >
          {t('jail.release')}
        </Button>
      </Popover.Target>
      <Popover.Dropdown>
        <Stack gap="xs" maw={260}>
          <Text size="sm">{t('jail.releaseConfirm', { name: character.name })}</Text>
          <Group gap="xs" justify="flex-end">
            <Button size="xs" variant="default" onClick={() => setOpened(false)}>
              {t('cancel')}
            </Button>
            <Button size="xs" color="teal" loading={mutation.isPending} onClick={() => mutation.mutate()}>
              {t('confirm')}
            </Button>
          </Group>
        </Stack>
      </Popover.Dropdown>
    </Popover>
  )
}
