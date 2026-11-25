import {
  ActionIcon,
  Badge,
  Button,
  Divider,
  Grid,
  Group,
  Loader,
  NumberInput,
  Paper,
  ScrollArea,
  Stack,
  Text,
  Textarea,
  TextInput,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import {
  IconDeviceFloppy,
  IconMapPin,
  IconPlus,
  IconRefresh,
  IconRotate,
  IconSearch,
  IconTrash,
} from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, ApiError, unwrap, type Schemas } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { LookfacePicker, MapPicker } from '../../components/pickers'
import { stringToTextLines, textLinesToString, type TeleporterDestRow, type TeleporterRow } from './dataUtils'
import { DialogPreview } from './DialogPreview'
import { MapCellPickerModal } from './MapCellPickerModal'
import { ProblemsAlert } from './ProblemsAlert'
import { useDataReload } from './useDataReload'

type DataProblem = Schemas['DataProblemDto']

export function TeleportersEditor() {
  const { t } = useTranslation('data')
  const queryClient = useQueryClient()
  const reloadMutation = useDataReload('teleporters.json')

  const query = useQuery({
    queryKey: ['data', 'teleporters'],
    queryFn: () => unwrap(api.GET('/api/data/teleporters')),
  })

  const [rows, setRows] = useState<TeleporterRow[]>([])
  const [hash, setHash] = useState('')
  const [dirty, setDirty] = useState(false)
  const [search, setSearch] = useState('')
  const [selectedIndex, setSelectedIndex] = useState<number | null>(null)
  const [problems, setProblems] = useState<DataProblem[]>([])
  const [pickerDestIndex, setPickerDestIndex] = useState<number | null>(null)

  useEffect(() => {
    if (query.data && !dirty) {
      setRows(
        query.data.entries.map((e) => ({
          id: e.id,
          fare: e.fare,
          face: e.face,
          text: [...e.text],
          destinations: e.destinations.map((d) => ({ ...d })),
          close: e.close ?? null,
          line: e.line,
          npcMap: e.npcMap,
          npcX: e.npcX,
          npcY: e.npcY,
        })),
      )
      setHash(query.data.hash)
      setProblems(query.data.problems)
      if (selectedIndex == null && query.data.entries.length > 0) setSelectedIndex(0)
    }
  }, [query.data, dirty, selectedIndex])

  const saveMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.PUT('/api/data/teleporters', {
          body: {
            hash,
            entries: rows.map((r) => ({
              id: r.id,
              fare: r.fare,
              face: r.face,
              text: r.text,
              destinations: r.destinations,
              close: r.close && r.close.trim() !== '' ? r.close : null,
            })),
          },
        }),
      ),
    onSuccess: (res) => {
      setProblems(res.problems)
      if (!res.saved) {
        notifications.show({
          color: 'red',
          title: t('status.validationFailedTitle', { file: 'teleporters.json' }),
          message: t('status.validationFailedMessage'),
        })
        return
      }
      setHash(res.hash)
      setDirty(false)
      void queryClient.invalidateQueries({ queryKey: ['data', 'teleporters'] })
      notifications.show({
        color: 'green',
        title: t('status.savedTitle', { file: 'teleporters.json' }),
        message:
          res.added + res.updated + res.removed > 0
            ? t('status.savedSummary', { added: res.added, updated: res.updated, removed: res.removed })
            : t('status.noChanges'),
      })
    },
    onError: (err) => {
      if (err instanceof ApiError && err.code === 'conflict') {
        notifications.show({
          color: 'orange',
          title: t('status.conflictTitle'),
          message: t('status.conflictMessage'),
        })
      }
    },
  })

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase()
    return rows
      .map((row, index) => ({ row, index }))
      .filter(({ row }) => {
        if (!term) return true
        return (
          String(row.id).includes(term) ||
          row.text.some((l) => l.toLowerCase().includes(term)) ||
          row.destinations.some((d) => d.name.toLowerCase().includes(term) || String(d.map).includes(term))
        )
      })
  }, [rows, search])

  const selected = selectedIndex != null ? rows[selectedIndex] ?? null : null

  function updateSelected(patch: Partial<TeleporterRow>) {
    if (selectedIndex == null) return
    setRows((prev) => prev.map((r, i) => (i === selectedIndex ? { ...r, ...patch } : r)))
    setDirty(true)
  }

  function updateDestination(destIdx: number, patch: Partial<TeleporterDestRow>) {
    if (!selected) return
    const nextDests = selected.destinations.map((d, i) => (i === destIdx ? { ...d, ...patch } : d))
    updateSelected({ destinations: nextDests })
  }

  function addDestination() {
    if (!selected) return
    updateSelected({
      destinations: [...selected.destinations, { name: 'Twin City', map: 1002, x: 430, y: 380 }],
    })
  }

  function removeDestination(destIdx: number) {
    if (!selected) return
    updateSelected({
      destinations: selected.destinations.filter((_, i) => i !== destIdx),
    })
  }

  function addTeleporter() {
    const nextId = rows.reduce((m, r) => Math.max(m, r.id), 10050) + 1
    const nextRow: TeleporterRow = {
      id: nextId,
      fare: 100,
      face: 1,
      text: ['Where would you like to go?'],
      destinations: [{ name: 'Twin City', map: 1002, x: 430, y: 380 }],
      close: null,
    }
    setRows((prev) => [...prev, nextRow])
    setSelectedIndex(rows.length)
    setDirty(true)
  }

  function removeSelected() {
    if (selectedIndex == null) return
    setRows((prev) => prev.filter((_, i) => i !== selectedIndex))
    setSelectedIndex((prev) => (prev == null || prev === 0 ? (rows.length > 1 ? 0 : null) : prev - 1))
    setDirty(true)
  }

  function resetChanges() {
    if (!query.data) return
    setRows(
      query.data.entries.map((e) => ({
        id: e.id,
        fare: e.fare,
        face: e.face,
        text: [...e.text],
        destinations: e.destinations.map((d) => ({ ...d })),
        close: e.close ?? null,
        line: e.line,
        npcMap: e.npcMap,
        npcX: e.npcX,
        npcY: e.npcY,
      })),
    )
    setHash(query.data.hash)
    setProblems(query.data.problems)
    setDirty(false)
  }

  if (query.isLoading) return <Loader />
  if (query.error) return <ErrorAlert error={query.error} />

  const activeDest = selected && pickerDestIndex != null ? selected.destinations[pickerDestIndex] ?? null : null

  return (
    <Stack gap="md">
      <Group justify="space-between" align="center" wrap="wrap">
        <Group gap="sm">
          <TextInput
            placeholder={t('teleporters.search')}
            leftSection={<IconSearch size={16} />}
            value={search}
            onChange={(e) => setSearch(e.currentTarget.value)}
            w={280}
          />
          <Text size="sm" c="dimmed">
            {t('teleporters.summary', { count: rows.length })}
          </Text>
          {dirty && (
            <Badge color="yellow" variant="light">
              {t('status.unsaved')}
            </Badge>
          )}
        </Group>

        <Group gap="xs">
          <Button variant="default" leftSection={<IconPlus size={16} />} onClick={addTeleporter}>
            {t('teleporters.addTeleporter')}
          </Button>
          {dirty && (
            <Button variant="subtle" color="gray" leftSection={<IconRotate size={16} />} onClick={resetChanges}>
              {t('actions.reset')}
            </Button>
          )}
          <Button
            leftSection={<IconDeviceFloppy size={16} />}
            loading={saveMutation.isPending}
            disabled={!dirty}
            onClick={() => saveMutation.mutate()}
          >
            {t('actions.save')}
          </Button>
          <Button
            variant="light"
            leftSection={<IconRefresh size={16} />}
            loading={reloadMutation.isPending}
            onClick={() => reloadMutation.mutate()}
          >
            {t('actions.reload')}
          </Button>
        </Group>
      </Group>

      {saveMutation.error && <ErrorAlert error={saveMutation.error} />}
      <ProblemsAlert problems={problems} />

      <Grid>
        <Grid.Col span={{ base: 12, md: 4 }}>
          <Paper withBorder p="xs">
            <ScrollArea h={560}>
              <Stack gap={6}>
                {filtered.map(({ row, index }) => {
                  const isActive = index === selectedIndex
                  return (
                    <Paper
                      key={`${index}-${row.id}`}
                      withBorder
                      p="xs"
                      style={{
                        cursor: 'pointer',
                        borderColor: isActive ? 'var(--mantine-color-blue-6)' : undefined,
                        backgroundColor: isActive ? 'var(--mantine-color-blue-light)' : undefined,
                      }}
                      onClick={() => setSelectedIndex(index)}
                    >
                      <Group justify="space-between" wrap="nowrap">
                        <Text fw={600} size="sm">
                          NPC #{row.id}
                        </Text>
                        <Badge size="xs" variant="light" color={row.fare > 0 ? 'yellow' : 'green'}>
                          {row.fare > 0 ? t('teleporters.fareBadge', { fare: row.fare }) : t('teleporters.freeBadge')}
                        </Badge>
                      </Group>
                      <Text size="xs" c="dimmed" truncate mt={2}>
                        {row.destinations.map((d) => d.name).join(', ') || '—'}
                      </Text>
                      <Group justify="space-between" mt={4}>
                        <Text size="xs" c="dimmed">
                          {t('teleporters.destinationsCount', { count: row.destinations.length })}
                        </Text>
                        {row.npcMap != null && (
                          <Text size="xs" c="dimmed">
                            {row.npcMap} ({row.npcX}, {row.npcY})
                          </Text>
                        )}
                      </Group>
                    </Paper>
                  )
                })}
                {filtered.length === 0 && (
                  <Text c="dimmed" ta="center" py="lg" size="sm">
                    {t('teleporters.empty')}
                  </Text>
                )}
              </Stack>
            </ScrollArea>
          </Paper>
        </Grid.Col>

        <Grid.Col span={{ base: 12, md: 8 }}>
          {selected ? (
            <Paper withBorder p="md">
              <Stack gap="md">
                <Group justify="space-between" align="center">
                  <div>
                    <Text fw={600}>NPC #{selected.id}</Text>
                    {selected.npcMap != null && (
                      <Text size="xs" c="dimmed">
                        {t('teleporters.npcLocation', {
                          map: selected.npcMap,
                          x: selected.npcX,
                          y: selected.npcY,
                        })}
                      </Text>
                    )}
                  </div>
                  <Button
                    variant="subtle"
                    color="red"
                    size="xs"
                    leftSection={<IconTrash size={14} />}
                    onClick={removeSelected}
                  >
                    {t('actions.delete')}
                  </Button>
                </Group>

                <Group grow align="flex-start">
                  <NumberInput
                    label={t('teleporters.fields.id')}
                    value={selected.id}
                    min={1}
                    allowDecimal={false}
                    onChange={(v) => updateSelected({ id: Number(v) || 0 })}
                  />
                  <NumberInput
                    label={t('teleporters.fields.fare')}
                    value={selected.fare}
                    min={0}
                    allowDecimal={false}
                    onChange={(v) => updateSelected({ fare: Number(v) || 0 })}
                  />
                </Group>

                <LookfacePicker
                  label={t('teleporters.fields.face')}
                  mode="npc"
                  value={selected.face}
                  clearable={false}
                  onChange={(v) => updateSelected({ face: Math.min(255, Math.max(0, v ?? 0)) })}
                />

                <Textarea
                  label={t('teleporters.fields.text')}
                  autosize
                  minRows={2}
                  maxRows={6}
                  value={textLinesToString(selected.text)}
                  onChange={(e) => updateSelected({ text: stringToTextLines(e.currentTarget.value) })}
                />

                <TextInput
                  label={t('teleporters.fields.close')}
                  placeholder={t('teleporters.fields.closePlaceholder')}
                  value={selected.close ?? ''}
                  onChange={(e) => updateSelected({ close: e.currentTarget.value || null })}
                />

                <Divider label={t('teleporters.fields.destinations')} labelPosition="left" />

                <Stack gap="xs">
                  {selected.destinations.map((dest, dIdx) => (
                    <Paper key={dIdx} withBorder p="xs">
                      <Stack gap="xs">
                        <Group justify="space-between" align="flex-end" wrap="nowrap">
                          <TextInput
                            style={{ flex: 1 }}
                            size="xs"
                            label={t('teleporters.fields.destName')}
                            value={dest.name}
                            onChange={(e) => updateDestination(dIdx, { name: e.currentTarget.value })}
                          />
                          <ActionIcon
                            variant="subtle"
                            color="red"
                            mb={2}
                            aria-label={t('actions.delete')}
                            onClick={() => removeDestination(dIdx)}
                          >
                            <IconTrash size={16} />
                          </ActionIcon>
                        </Group>

                        <Group align="flex-end" wrap="wrap" gap="xs">
                          <div style={{ flex: 2, minWidth: 200 }}>
                            <MapPicker
                              label={t('teleporters.fields.destMap')}
                              value={dest.map || null}
                              clearable={false}
                              onChange={(id) => updateDestination(dIdx, { map: id ?? 1002 })}
                            />
                          </div>
                          <NumberInput
                            w={95}
                            label={t('teleporters.fields.destX')}
                            value={dest.x}
                            min={0}
                            max={65535}
                            allowDecimal={false}
                            onChange={(v) => updateDestination(dIdx, { x: Number(v) || 0 })}
                          />
                          <NumberInput
                            w={95}
                            label={t('teleporters.fields.destY')}
                            value={dest.y}
                            min={0}
                            max={65535}
                            allowDecimal={false}
                            onChange={(v) => updateDestination(dIdx, { y: Number(v) || 0 })}
                          />
                          <Button
                            variant="light"
                            size="sm"
                            leftSection={<IconMapPin size={15} />}
                            onClick={() => setPickerDestIndex(dIdx)}
                          >
                            {t('actions.pickOnMap')}
                          </Button>
                        </Group>
                      </Stack>
                    </Paper>
                  ))}

                  <Button
                    variant="default"
                    size="xs"
                    leftSection={<IconPlus size={14} />}
                    onClick={addDestination}
                    w="fit-content"
                  >
                    {t('teleporters.fields.addDestination')}
                  </Button>
                </Stack>

                <DialogPreview
                  title={t('teleporters.previewTitle')}
                  face={selected.face}
                  lines={selected.text}
                  choices={selected.destinations.map((d) => d.name)}
                  closeLabel={selected.close?.trim() ? selected.close : t('teleporters.defaultClose')}
                  badge={
                    selected.fare > 0
                      ? t('teleporters.fareBadge', { fare: selected.fare })
                      : t('teleporters.freeBadge')
                  }
                />
              </Stack>
            </Paper>
          ) : (
            <Paper withBorder p="xl">
              <Text c="dimmed" ta="center">
                {t('teleporters.selectPrompt')}
              </Text>
            </Paper>
          )}
        </Grid.Col>
      </Grid>

      {activeDest && pickerDestIndex != null && (
        <MapCellPickerModal
          opened={true}
          onClose={() => setPickerDestIndex(null)}
          initialMap={activeDest.map}
          initialX={activeDest.x}
          initialY={activeDest.y}
          onApply={(map, x, y) => updateDestination(pickerDestIndex, { map, x, y })}
        />
      )}
    </Stack>
  )
}
