import {
  Badge,
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
import { notifications } from '@mantine/notifications'
import { IconDeviceFloppy, IconPlus, IconRefresh, IconRotate, IconSearch, IconTrash } from '@tabler/icons-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, ApiError, unwrap, type Schemas } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { LookfacePicker } from '../../components/pickers'
import { stringToTextLines, textLinesToString, type TalkerRow } from './dataUtils'
import { DialogPreview } from './DialogPreview'
import { ProblemsAlert } from './ProblemsAlert'
import { useDataReload } from './useDataReload'

type DataProblem = Schemas['DataProblemDto']

export function TalkersEditor() {
  const { t } = useTranslation('data')
  const queryClient = useQueryClient()
  const reloadMutation = useDataReload('talkers.json')

  const query = useQuery({
    queryKey: ['data', 'talkers'],
    queryFn: () => unwrap(api.GET('/api/data/talkers')),
  })

  const [rows, setRows] = useState<TalkerRow[]>([])
  const [hash, setHash] = useState('')
  const [dirty, setDirty] = useState(false)
  const [search, setSearch] = useState('')
  const [selectedIndex, setSelectedIndex] = useState<number | null>(null)
  const [problems, setProblems] = useState<DataProblem[]>([])

  useEffect(() => {
    if (query.data && !dirty) {
      setRows(
        query.data.entries.map((e) => ({
          id: e.id,
          face: e.face,
          text: [...e.text],
          reply: e.reply,
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
        api.PUT('/api/data/talkers', {
          body: {
            hash,
            entries: rows.map((r) => ({
              id: r.id,
              face: r.face,
              text: r.text,
              reply: r.reply,
            })),
          },
        }),
      ),
    onSuccess: (res) => {
      setProblems(res.problems)
      if (!res.saved) {
        notifications.show({
          color: 'red',
          title: t('status.validationFailedTitle', { file: 'talkers.json' }),
          message: t('status.validationFailedMessage'),
        })
        return
      }
      setHash(res.hash)
      setDirty(false)
      void queryClient.invalidateQueries({ queryKey: ['data', 'talkers'] })
      notifications.show({
        color: 'green',
        title: t('status.savedTitle', { file: 'talkers.json' }),
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

  function resetChanges() {
    if (!query.data) return
    setRows(
      query.data.entries.map((e) => ({
        id: e.id,
        face: e.face,
        text: [...e.text],
        reply: e.reply,
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

  return (
    <Stack gap="md">
      <Group justify="space-between" align="center" wrap="wrap">
        <Group gap="sm">
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
          {dirty && (
            <Badge color="yellow" variant="light">
              {t('status.unsaved')}
            </Badge>
          )}
        </Group>

        <Group gap="xs">
          <Button variant="default" leftSection={<IconPlus size={16} />} onClick={addTalker}>
            {t('talkers.addTalker')}
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
