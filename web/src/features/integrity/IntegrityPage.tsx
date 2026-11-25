import {
  Alert,
  Badge,
  Button,
  Group,
  Loader,
  Paper,
  ScrollArea,
  SegmentedControl,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import {
  IconAlertTriangle,
  IconArrowRight,
  IconCheck,
  IconRefresh,
  IconSearch,
  IconShieldCheck,
} from '@tabler/icons-react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { api, unwrap } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { distinctCategories, filterProblems, type LevelFilter } from './integrityUtils'

export default function IntegrityPage() {
  const { t } = useTranslation('integrity')
  const queryClient = useQueryClient()
  const [level, setLevel] = useState<LevelFilter>('errors')
  const [fileFilter, setFileFilter] = useState<string | null>(null)
  const [categoryFilter, setCategoryFilter] = useState<string | null>(null)
  const [search, setSearch] = useState('')

  const report = useQuery({
    queryKey: ['integrity'],
    queryFn: () => unwrap(api.GET('/api/integrity')),
  })

  const problems = report.data?.problems ?? []
  const files = report.data?.files ?? []

  const filtered = useMemo(
    () =>
      filterProblems(problems, {
        level,
        file: fileFilter,
        category: categoryFilter,
        search,
      }),
    [problems, level, fileFilter, categoryFilter, search],
  )

  const fileOptions = useMemo(
    () =>
      files.map((f) => ({
        value: f.file,
        label: f.file,
      })),
    [files],
  )

  const categoryOptions = useMemo(
    () =>
      distinctCategories(problems).map((c) => ({
        value: c,
        label: t(`category.${c}`, { defaultValue: c }),
      })),
    [problems, t],
  )

  const errorCount = report.data?.errorCount ?? 0
  const warningCount = report.data?.warningCount ?? 0
  const totalCount = problems.length

  return (
    <Stack style={{ minWidth: 0 }}>
      <Group justify="space-between" align="flex-start">
        <Stack gap={4}>
          <Title order={2}>{t('title')}</Title>
          <Text c="dimmed">{t('description')}</Text>
        </Stack>
        <Group>
          {report.isFetching && <Loader size="sm" />}
          <Button
            variant="light"
            leftSection={<IconRefresh size={16} />}
            onClick={() => void queryClient.invalidateQueries({ queryKey: ['integrity'] })}
          >
            {t('refresh')}
          </Button>
        </Group>
      </Group>

      {report.data && (
        <Group gap="xs">
          <Badge color={report.data.clientFilesAvailable ? 'green' : 'orange'} variant="light">
            {report.data.clientFilesAvailable ? t('status.clientAvailable') : t('status.clientMissing')}
          </Badge>
          <Badge color="blue" variant="light">
            {t('status.checkedFiles', { count: report.data.checkedFiles })}
          </Badge>
          <Badge color={errorCount > 0 ? 'red' : 'green'} variant="light">
            {t('status.errors', { count: errorCount })}
          </Badge>
          <Badge color={warningCount > 0 ? 'yellow' : 'gray'} variant="light">
            {t('status.warnings', { count: warningCount })}
          </Badge>
          {report.data.dataDirectory && (
            <Text size="xs" c="dimmed" ff="monospace" ml="auto">
              {report.data.dataDirectory}
            </Text>
          )}
        </Group>
      )}

      <ErrorAlert error={report.error} />

      {report.data && errorCount === 0 && (
        <Alert color="green" icon={<IconShieldCheck size={18} />} title={t('clean.title')}>
          <Stack gap={4}>
            <Text size="sm">{t('clean.body', { files: report.data.checkedFiles })}</Text>
            {warningCount > 0 && level === 'errors' && (
              <Text size="xs" c="dimmed">
                {t('clean.withWarnings', { warnings: warningCount })}
              </Text>
            )}
          </Stack>
        </Alert>
      )}

      {report.data && (
        <Group gap="sm" align="flex-end" wrap="wrap">
          <SegmentedControl
            value={level}
            onChange={(v) => setLevel(v as LevelFilter)}
            data={[
              { label: t('filter.errors', { count: errorCount }), value: 'errors' },
              { label: t('filter.warnings', { count: warningCount }), value: 'warnings' },
              { label: t('filter.all', { count: totalCount }), value: 'all' },
            ]}
          />
          <Select
            placeholder={t('filter.allFiles')}
            data={fileOptions}
            value={fileFilter}
            onChange={setFileFilter}
            clearable
            w={190}
          />
          <Select
            placeholder={t('filter.allCategories')}
            data={categoryOptions}
            value={categoryFilter}
            onChange={setCategoryFilter}
            clearable
            w={200}
          />
          <TextInput
            placeholder={t('filter.search')}
            leftSection={<IconSearch size={14} />}
            value={search}
            onChange={(e) => setSearch(e.currentTarget.value)}
            style={{ flex: 1, minWidth: 220 }}
          />
        </Group>
      )}

      {report.data && (filtered.length > 0 || fileFilter || categoryFilter || search.trim() || level !== 'errors') && (
        <Stack gap="xs">
          <Title order={4}>{t('problemsHeading', { count: filtered.length })}</Title>
          <Paper withBorder>
            <ScrollArea>
              <Table highlightOnHover striped miw={820}>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>{t('column.level')}</Table.Th>
                    <Table.Th>{t('column.file')}</Table.Th>
                    <Table.Th>{t('column.category')}</Table.Th>
                    <Table.Th>{t('column.entity')}</Table.Th>
                    <Table.Th>{t('column.message')}</Table.Th>
                    <Table.Th>{t('column.fix')}</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {filtered.map((p, idx) => (
                    <Table.Tr key={`${p.file}-${p.line ?? 'n'}-${idx}`}>
                      <Table.Td style={{ whiteSpace: 'nowrap' }}>
                        <Badge
                          color={p.level === 'warning' ? 'yellow' : 'red'}
                          variant="light"
                          size="sm"
                          style={{ overflow: 'visible' }}
                          leftSection={<IconAlertTriangle size={12} />}
                        >
                          {t(`level.${p.level}`, { defaultValue: p.level })}
                        </Badge>
                      </Table.Td>
                      <Table.Td style={{ whiteSpace: 'nowrap' }}>
                        <Text size="sm" ff="monospace">
                          {p.file}
                          {p.line != null ? `:${p.line}` : ''}
                        </Text>
                      </Table.Td>
                      <Table.Td style={{ whiteSpace: 'nowrap' }}>
                        <Badge variant="outline" color="gray" size="sm" style={{ overflow: 'visible' }}>
                          {t(`category.${p.category}`, { defaultValue: p.category })}
                        </Badge>
                      </Table.Td>
                      <Table.Td style={{ whiteSpace: 'nowrap' }}>
                        <Text size="sm" fw={500}>
                          {p.entityId ?? '—'}
                        </Text>
                      </Table.Td>
                      <Table.Td>
                        <Text size="sm" style={{ wordBreak: 'break-word' }}>
                          {p.message}
                        </Text>
                      </Table.Td>
                      <Table.Td style={{ whiteSpace: 'nowrap' }}>
                        <Button
                          component={Link}
                          to={p.editorPath}
                          size="xs"
                          variant="light"
                          rightSection={<IconArrowRight size={14} />}
                        >
                          {t(`editor.${p.editorKind}`, { defaultValue: p.editorKind })}
                        </Button>
                      </Table.Td>
                    </Table.Tr>
                  ))}
                  {filtered.length === 0 && (
                    <Table.Tr>
                      <Table.Td colSpan={6}>
                        <Text c="dimmed" ta="center" py="md">
                          {t('emptyFiltered')}
                        </Text>
                      </Table.Td>
                    </Table.Tr>
                  )}
                </Table.Tbody>
              </Table>
            </ScrollArea>
          </Paper>
        </Stack>
      )}

      {report.data && files.length > 0 && (
        <Stack gap="xs">
          <Title order={4}>{t('filesHeading')}</Title>
          <Paper withBorder>
            <ScrollArea>
              <Table highlightOnHover striped miw={600}>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>{t('column.file')}</Table.Th>
                    <Table.Th>{t('column.entries')}</Table.Th>
                    <Table.Th>{t('column.errors')}</Table.Th>
                    <Table.Th>{t('column.warnings')}</Table.Th>
                    <Table.Th>{t('column.status')}</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {files.map((f) => {
                    const isSelected = fileFilter === f.file
                    return (
                      <Table.Tr
                        key={f.file}
                        onClick={() => {
                          setFileFilter(isSelected ? null : f.file)
                          if (!isSelected && f.errors === 0 && f.warnings > 0 && level === 'errors') {
                            setLevel('all')
                          }
                        }}
                        style={{
                          cursor: 'pointer',
                          backgroundColor: isSelected ? 'var(--mantine-color-blue-light)' : undefined,
                        }}
                      >
                        <Table.Td ff="monospace" fw={isSelected ? 700 : 500}>
                          {f.file}
                        </Table.Td>
                        <Table.Td>{f.checkedEntries}</Table.Td>
                        <Table.Td>
                          <Text size="sm" c={f.errors > 0 ? 'red' : 'dimmed'} fw={f.errors > 0 ? 600 : 400}>
                            {f.errors}
                          </Text>
                        </Table.Td>
                        <Table.Td>
                          <Text size="sm" c={f.warnings > 0 ? 'yellow.8' : 'dimmed'} fw={f.warnings > 0 ? 600 : 400}>
                            {f.warnings}
                          </Text>
                        </Table.Td>
                        <Table.Td>
                          {f.errors > 0 ? (
                            <Badge color="red" variant="light" size="sm" leftSection={<IconAlertTriangle size={12} />}>
                              {t('fileStatus.errors')}
                            </Badge>
                          ) : f.warnings > 0 ? (
                            <Badge color="yellow" variant="light" size="sm">
                              {t('fileStatus.warnings')}
                            </Badge>
                          ) : (
                            <Badge color="green" variant="light" size="sm" leftSection={<IconCheck size={12} />}>
                              {t('fileStatus.ok')}
                            </Badge>
                          )}
                        </Table.Td>
                      </Table.Tr>
                    )
                  })}
                </Table.Tbody>
              </Table>
            </ScrollArea>
          </Paper>
        </Stack>
      )}
    </Stack>
  )
}
