import {
  ActionIcon,
  Loader,
  NumberInput,
  Paper,
  ScrollArea,
  Stack,
  Table,
  Text,
  TextInput,
} from '@mantine/core'
import { IconSearch, IconTrash } from '@tabler/icons-react'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { DataEditorToolbar } from './DataEditorToolbar'
import { expBallsForLevel, nextLevelNumber, type LevelRow } from './dataUtils'
import { useDataFileEditor } from './useDataFileEditor'

export function LevelsEditor() {
  const { t } = useTranslation('data')
  const [search, setSearch] = useState('')

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
    queryKey: ['data', 'levels'],
    fileName: 'levels.json',
    queryFn: () => unwrap(api.GET('/api/data/levels')),
    mapEntry: (e): LevelRow => ({ ...e }),
    saveFn: (hash, currentRows) =>
      unwrap(
        api.PUT('/api/data/levels', {
          body: {
            hash,
            entries: currentRows.map((r) => ({
              level: r.level,
              experience: r.experience,
              upLevTime: r.upLevTime,
            })),
          },
        }),
      ),
  })

  const filtered = useMemo(() => {
    const term = search.trim()
    return rows
      .map((row, index) => ({ row, index }))
      .filter(({ row }) => !term || String(row.level).includes(term))
  }, [rows, search])

  const levelCap = useMemo(() => {
    if (rows.length === 0) return 0
    return rows.reduce((m, r) => (r.level > m ? r.level : m), 0) + 1
  }, [rows])

  function updateRow(index: number, patch: Partial<LevelRow>) {
    setRows((prev) => prev.map((r, i) => (i === index ? { ...r, ...patch } : r)))
    setDirty(true)
  }

  function addLevel() {
    const next = nextLevelNumber(rows)
    const last = rows[rows.length - 1]
    setRows((prev) => [
      ...prev,
      {
        level: next,
        experience: last ? last.experience : 100,
        upLevTime: last ? last.upLevTime : 2,
      },
    ])
    setDirty(true)
  }

  function removeRow(index: number) {
    setRows((prev) => prev.filter((_, i) => i !== index))
    setDirty(true)
  }

  if (query.isLoading) return <Loader />
  if (query.error) return <ErrorAlert error={query.error} />

  return (
    <Stack gap="md">
      <DataEditorToolbar
        dirty={dirty}
        addLabel={t('levels.addLevel')}
        onAdd={addLevel}
        onReset={resetChanges}
        onSave={() => saveMutation.mutate()}
        savePending={saveMutation.isPending}
        saveError={saveMutation.error}
        onReload={() => reloadMutation.mutate()}
        reloadPending={reloadMutation.isPending}
        problems={problems}
      >
        <TextInput
          placeholder={t('levels.search')}
          leftSection={<IconSearch size={16} />}
          value={search}
          onChange={(e) => setSearch(e.currentTarget.value)}
          w={220}
        />
        <Text size="sm" c="dimmed">
          {t('levels.summary', { count: rows.length, cap: levelCap })}
        </Text>
      </DataEditorToolbar>

      <Paper withBorder>
        <ScrollArea h={560}>
          <Table striped highlightOnHover stickyHeader>
            <Table.Thead>
              <Table.Tr>
                <Table.Th w={110}>{t('levels.columns.level')}</Table.Th>
                <Table.Th w={220}>{t('levels.columns.experience')}</Table.Th>
                <Table.Th w={160}>{t('levels.columns.upLevTime')}</Table.Th>
                <Table.Th w={140}>{t('levels.columns.expBalls')}</Table.Th>
                <Table.Th w={60}>{t('levels.columns.actions')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {filtered.map(({ row, index }) => (
                <Table.Tr key={`${index}-${row.line ?? 'new'}`}>
                  <Table.Td>
                    <NumberInput
                      size="xs"
                      value={row.level}
                      min={1}
                      max={255}
                      allowDecimal={false}
                      onChange={(v) => updateRow(index, { level: Number(v) || 0 })}
                    />
                  </Table.Td>
                  <Table.Td>
                    <NumberInput
                      size="xs"
                      value={row.experience}
                      min={0}
                      thousandSeparator=","
                      allowDecimal={false}
                      onChange={(v) => updateRow(index, { experience: Number(v) || 0 })}
                    />
                  </Table.Td>
                  <Table.Td>
                    <NumberInput
                      size="xs"
                      value={row.upLevTime}
                      min={0}
                      allowDecimal={false}
                      onChange={(v) => updateRow(index, { upLevTime: Number(v) || 0 })}
                    />
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" c="dimmed">
                      {expBallsForLevel(row.upLevTime).toFixed(2)}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <ActionIcon
                      variant="subtle"
                      color="red"
                      aria-label={t('actions.delete')}
                      onClick={() => removeRow(index)}
                    >
                      <IconTrash size={16} />
                    </ActionIcon>
                  </Table.Td>
                </Table.Tr>
              ))}
              {filtered.length === 0 && (
                <Table.Tr>
                  <Table.Td colSpan={5}>
                    <Text c="dimmed" ta="center" py="md">
                      {t('levels.empty')}
                    </Text>
                  </Table.Td>
                </Table.Tr>
              )}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      </Paper>
    </Stack>
  )
}
