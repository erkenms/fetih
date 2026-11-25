import { Button, Group, Popover, ScrollArea, Stack, Table, Text, TextInput } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { Schemas } from '../../api/client'
import { useErrorText } from '../../app/ErrorAlert'
import { classKey } from './names'

export type CharacterRef = Schemas['CharacterRef']

export function useNumber() {
  const { i18n } = useTranslation()
  return (n: number) => n.toLocaleString(i18n.language)
}

export function useDay() {
  const { i18n } = useTranslation()
  return (day: string | null | undefined) =>
    day ? new Date(`${day}T00:00:00`).toLocaleDateString(i18n.language) : '—'
}

export function useTime() {
  const { i18n } = useTranslation()
  return (time: string | null | undefined) => (time ? new Date(time).toLocaleString(i18n.language) : '—')
}

export function CharacterCell({ character }: { character: CharacterRef | null | undefined }) {
  const { t } = useTranslation('social')
  if (!character) return <Text c="dimmed">—</Text>
  const cls = classKey(character.class)
  return (
    <Group gap={6} wrap="nowrap">
      <Text size="sm" fw={500}>
        {character.name}
      </Text>
      {character.level > 0 && (
        <Text size="xs" c="dimmed">
          {t('levelClass', { level: character.level, class: cls ? t(`class.${cls}`) : '' })}
        </Text>
      )}
    </Group>
  )
}

export function SearchBox({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const { t } = useTranslation('social')
  return (
    <TextInput
      placeholder={t('search')}
      value={value}
      onChange={(e) => onChange(e.currentTarget.value)}
      maw={320}
    />
  )
}

// A table with a header row; `empty` shows when there are no rows
export function DataTable({ head, rows, empty }: { head: string[]; rows: ReactNode[]; empty: string }) {
  if (rows.length === 0) return <Text c="dimmed">{empty}</Text>
  return (
    <ScrollArea>
      <Table striped highlightOnHover miw={600}>
        <Table.Thead>
          <Table.Tr>
            {head.map((h) => (
              <Table.Th key={h}>{h}</Table.Th>
            ))}
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>{rows}</Table.Tbody>
      </Table>
    </ScrollArea>
  )
}

// A button that asks once more before it runs `action`, then refreshes `invalidate`
export function ConfirmAction({
  label,
  question,
  done,
  action,
  invalidate,
  color = 'red',
  disabled,
}: {
  label: string
  question: string
  done: string
  action: () => Promise<unknown>
  invalidate: string[][]
  color?: string
  disabled?: boolean
}) {
  const { t } = useTranslation('social')
  const errorText = useErrorText()
  const client = useQueryClient()
  const [opened, setOpened] = useState(false)
  const mutation = useMutation({
    mutationFn: action,
    onSuccess: async () => {
      setOpened(false)
      notifications.show({ color: 'green', message: done })
      await Promise.all(invalidate.map((key) => client.invalidateQueries({ queryKey: key })))
    },
    onError: (error) => {
      setOpened(false)
      notifications.show({ color: 'red', message: t(`errors.${(error as { code?: string }).code}`, { defaultValue: errorText(error) }) })
    },
  })
  return (
    <Popover opened={opened} onChange={setOpened} withArrow shadow="md">
      <Popover.Target>
        <Button size="xs" color={color} variant="light" disabled={disabled} onClick={() => setOpened((o) => !o)}>
          {label}
        </Button>
      </Popover.Target>
      <Popover.Dropdown>
        <Stack gap="xs" maw={280}>
          <Text size="sm">{question}</Text>
          <Group gap="xs" justify="flex-end">
            <Button size="xs" variant="default" onClick={() => setOpened(false)}>
              {t('cancel')}
            </Button>
            <Button size="xs" color={color} loading={mutation.isPending} onClick={() => mutation.mutate()}>
              {t('confirm')}
            </Button>
          </Group>
        </Stack>
      </Popover.Dropdown>
    </Popover>
  )
}
