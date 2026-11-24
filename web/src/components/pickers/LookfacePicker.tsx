import { Button, Group, Modal, NumberInput, Paper, ScrollArea, Select, SimpleGrid, Stack, Text, TextInput } from '@mantine/core'
import { IconMoodSmile } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap } from '../../api/client'
import { AssetImage } from './AssetImage'
import { combineLookface, splitLookface } from './catalogUtils'

export interface LookfacePickerProps {
  value: number | null
  onChange: (value: number | null) => void
  mode?: 'npc' | 'player' | 'lookface'
  label?: string
  placeholder?: string
  clearable?: boolean
  disabled?: boolean
}

export function LookfacePicker({
  value,
  onChange,
  mode = 'npc',
  label,
  placeholder,
  clearable = true,
  disabled = false,
}: LookfacePickerProps) {
  const { t } = useTranslation('catalogs')
  const [opened, setOpened] = useState(false)
  const [filter, setFilter] = useState('')

  const faces = useQuery({
    queryKey: ['catalogs', 'faces'],
    queryFn: () => unwrap(api.GET('/api/catalogs/faces')),
  })

  const entries = useMemo(() => {
    if (!faces.data) return []
    return mode === 'player' ? faces.data.player : faces.data.npc
  }, [faces.data, mode])

  const faceLookup = useMemo(() => {
    const map = new Map<number, string>()
    for (const entry of entries) map.set(entry.id, entry.path)
    return map
  }, [entries])

  const parsed = value != null ? splitLookface(value) : null
  const activeFaceId = value == null ? null : mode === 'lookface' ? parsed!.look : value
  const activeFacePath = activeFaceId != null ? faceLookup.get(activeFaceId) ?? null : null

  const filtered = useMemo(() => {
    const term = filter.trim()
    if (!term) return entries.slice(0, 180)
    return entries.filter((e) => String(e.id).includes(term)).slice(0, 180)
  }, [entries, filter])

  const directions = useMemo(
    () => Array.from({ length: 8 }, (_, d) => ({ value: String(d), label: `${t('pickers.lookface.direction')} ${d}` })),
    [t],
  )

  return (
    <>
      <Stack gap={4}>
        {label !== '' && (
          <Text size="sm" fw={500}>
            {label ?? t('pickers.lookface.label')}
          </Text>
        )}
        <Group gap="xs" align="flex-end" wrap="nowrap">
          <AssetImage
            path={activeFacePath}
            alt={activeFaceId != null ? `#${activeFaceId}` : ''}
            size={36}
            fallback={<IconMoodSmile size={18} />}
          />
          <NumberInput
            style={{ flex: 1 }}
            value={value ?? ''}
            placeholder={placeholder ?? t('pickers.lookface.placeholder')}
            disabled={disabled}
            min={0}
            allowDecimal={false}
            onChange={(val) => {
              if (val === '' || val == null) onChange(clearable ? null : 0)
              else onChange(Number(val))
            }}
          />
          {mode === 'lookface' && (
            <Select
              w={130}
              data={directions}
              value={String(parsed?.direction ?? 0)}
              disabled={disabled || value == null}
              onChange={(d) => {
                if (d == null || parsed == null) return
                onChange(combineLookface(parsed.look, Number(d)))
              }}
            />
          )}
          <Button variant="default" onClick={() => setOpened(true)} disabled={disabled}>
            {t('pickers.lookface.browse')}
          </Button>
        </Group>
      </Stack>

      <Modal opened={opened} onClose={() => setOpened(false)} title={t('pickers.lookface.modalTitle')} size="lg">
        <Stack>
          <TextInput
            placeholder={t('pickers.lookface.filterPlaceholder')}
            value={filter}
            onChange={(e) => setFilter(e.currentTarget.value)}
          />
          <ScrollArea h={380}>
            <SimpleGrid cols={{ base: 4, sm: 6 }} spacing="xs">
              {filtered.map((f) => {
                const isSelected = activeFaceId === f.id
                return (
                  <Paper
                    key={f.id}
                    withBorder
                    p="xs"
                    style={{
                      cursor: 'pointer',
                      borderColor: isSelected ? 'var(--mantine-color-blue-6)' : undefined,
                    }}
                    onClick={() => {
                      if (mode === 'lookface') {
                        onChange(combineLookface(f.id, parsed?.direction ?? 0))
                      } else {
                        onChange(f.id)
                      }
                      setOpened(false)
                    }}
                  >
                    <Stack align="center" gap={4}>
                      <AssetImage path={f.path} alt={`#${f.id}`} size={48} fallback={<IconMoodSmile size={20} />} />
                      <Text size="xs" fw={500}>
                        #{f.id}
                      </Text>
                    </Stack>
                  </Paper>
                )
              })}
            </SimpleGrid>
            {filtered.length === 0 && (
              <Text c="dimmed" ta="center" py="lg">
                {t('pickers.empty')}
              </Text>
            )}
          </ScrollArea>
        </Stack>
      </Modal>
    </>
  )
}
