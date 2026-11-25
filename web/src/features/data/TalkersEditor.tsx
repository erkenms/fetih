import {
  Button,
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
import { IconSearch, IconTrash } from '@tabler/icons-react'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { LookfacePicker } from '../../components/pickers'
import { DataEditorToolbar } from './DataEditorToolbar'
import { stringToTextLines, textLinesToString, type TalkerRow } from './dataUtils'
import { DialogPreview } from './DialogPreview'
import { useDataFileEditor } from './useDataFileEditor'

export function TalkersEditor() {
  const { t } = useTranslation('data')
  const [search, setSearch] = useState('')
  const [selectedIndex, setSelectedIndex] = useState<number | null>(null)

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
    queryKey: ['data', 'talkers'],
    fileName: 'talkers.json',
    queryFn: () => unwrap(api.GET('/api/data/talkers')),
    mapEntry: (e): TalkerRow => ({
      id: e.id,
      face: e.face,
      text: [...e.text],
      reply: e.reply,
      line: e.line,
      npcMap: e.npcMap,
      npcX: e.npcX,
      npcY: e.npcY,
    }),
    saveFn: (hash, currentRows) =>
      unwrap(
        api.PUT('/api/data/talkers', {
          body: {
            hash,
            entries: currentRows.map((r) => ({
              id: r.id,
              face: r.face,
              text: r.text,
              reply: r.reply,
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
          row.reply.toLowerCase().includes(term) ||
          row.text.some((l) => l.toLowerCase().includes(term))
        )
      })
  }, [rows, search])

  const selected = selectedIndex != null ? rows[selectedIndex] ?? null : null

  function updateSelected(patch: Partial<TalkerRow>) {
    if (selectedIndex == null) return
    setRows((prev) => prev.map((r, i) => (i === selectedIndex ? { ...r, ...patch } : r)))
    setDirty(true)
  }

  function addTalker() {
    const nextId = rows.reduce((m, r) => Math.max(m, r.id), 10000) + 1
    setRows((prev) => [
      ...prev,
      {
        id: nextId,
        face: 6,
        text: ['Hello, traveler!'],
        reply: 'I see.',
      },
    ])
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

  return (
    <Stack gap="md">
      <DataEditorToolbar
        dirty={dirty}
        addLabel={t('talkers.addTalker')}
        onAdd={addTalker}
        onReset={resetChanges}
        onSave={() => saveMutation.mutate()}
        savePending={saveMutation.isPending}
        saveError={saveMutation.error}
        onReload={() => reloadMutation.mutate()}
        reloadPending={reloadMutation.isPending}
        problems={problems}
      >
        <TextInput
          placeholder={t('talkers.search')}
          leftSection={<IconSearch size={16} />}
          value={search}
          onChange={(e) => setSearch(e.currentTarget.value)}
          w={280}
        />
        <Text size="sm" c="dimmed">
          {t('talkers.summary', { count: rows.length })}
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
                        {row.npcMap != null && (
                          <Text size="xs" c="dimmed">
                            {row.npcMap} ({row.npcX}, {row.npcY})
                          </Text>
                        )}
                      </Group>
                      <Text size="xs" c="dimmed" truncate mt={2}>
                        {row.text[0] || '—'}
                      </Text>
                    </Paper>
                  )
                })}
                {filtered.length === 0 && (
                  <Text c="dimmed" ta="center" py="lg" size="sm">
                    {t('talkers.empty')}
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
                        {t('talkers.npcLocation', {
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

                <NumberInput
                  label={t('talkers.fields.id')}
                  value={selected.id}
                  min={1}
                  allowDecimal={false}
                  onChange={(v) => updateSelected({ id: Number(v) || 0 })}
                />

                <LookfacePicker
                  label={t('talkers.fields.face')}
                  mode="npc"
                  value={selected.face}
                  clearable={false}
                  onChange={(v) => updateSelected({ face: Math.min(255, Math.max(0, v ?? 0)) })}
                />

                <Textarea
                  label={t('talkers.fields.text')}
                  autosize
                  minRows={3}
                  maxRows={8}
                  value={textLinesToString(selected.text)}
                  onChange={(e) => updateSelected({ text: stringToTextLines(e.currentTarget.value) })}
                />

                <TextInput
                  label={t('talkers.fields.reply')}
                  value={selected.reply}
                  onChange={(e) => updateSelected({ reply: e.currentTarget.value })}
                />

                <DialogPreview
                  title={t('talkers.previewTitle')}
                  face={selected.face}
                  lines={selected.text}
                  choices={[]}
                  closeLabel={selected.reply || '…'}
                />
              </Stack>
            </Paper>
          ) : (
            <Paper withBorder p="xl">
              <Text c="dimmed" ta="center">
                {t('talkers.selectPrompt')}
              </Text>
            </Paper>
          )}
        </Grid.Col>
      </Grid>
    </Stack>
  )
}
