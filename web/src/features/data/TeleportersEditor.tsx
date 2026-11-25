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
import { IconMapPin, IconPlus, IconSearch, IconTrash } from '@tabler/icons-react'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { LookfacePicker, MapPicker } from '../../components/pickers'
import { DataEditorToolbar } from './DataEditorToolbar'
import { stringToTextLines, textLinesToString, type TeleporterDestRow, type TeleporterRow } from './dataUtils'
import { DialogPreview } from './DialogPreview'
import { MapCellPickerModal } from './MapCellPickerModal'
import { useDataFileEditor } from './useDataFileEditor'

export function TeleportersEditor() {
  const { t } = useTranslation('data')
  const [search, setSearch] = useState('')
  const [selectedIndex, setSelectedIndex] = useState<number | null>(null)
  const [pickerDestIndex, setPickerDestIndex] = useState<number | null>(null)

  const {
    query,
    rows,
    setRows,
    dirty,
    setDirty,
    problems,
    saveMutation,
    reloadMutation,
    resetChanges,
  } = useDataFileEditor({
    queryKey: ['data', 'teleporters'],
    fileName: 'teleporters.json',
    queryFn: () => unwrap(api.GET('/api/data/teleporters')),
    mapEntry: (e): TeleporterRow => ({
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
    }),
    saveFn: (hash, currentRows) =>
      unwrap(
        api.PUT('/api/data/teleporters', {
          body: {
            hash,
            entries: currentRows.map((r) => ({
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
    onSynced: (entries) => {
      if (selectedIndex == null && entries.length > 0) setSelectedIndex(0)
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

  if (query.isLoading) return <Loader />
  if (query.error) return <ErrorAlert error={query.error} />

  const activeDest = selected && pickerDestIndex != null ? selected.destinations[pickerDestIndex] ?? null : null

  return (
    <Stack gap="md">
      <DataEditorToolbar
        dirty={dirty}
        addLabel={t('teleporters.addTeleporter')}
        onAdd={addTeleporter}
        onReset={resetChanges}
        onSave={() => saveMutation.mutate()}
        savePending={saveMutation.isPending}
        saveError={saveMutation.error}
        onReload={() => reloadMutation.mutate()}
        reloadPending={reloadMutation.isPending}
        problems={problems}
      >
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
      </DataEditorToolbar>

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
