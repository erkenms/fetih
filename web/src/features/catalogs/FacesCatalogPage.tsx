import { Group, Loader, Paper, SimpleGrid, Stack, Tabs, Text, TextInput, Title } from '@mantine/core'
import { IconMoodSmile } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { AssetImage, LookfacePicker } from '../../components/pickers'

export default function FacesCatalogPage() {
  const { t } = useTranslation('catalogs')
  const [tab, setTab] = useState<string>('npc')
  const [filter, setFilter] = useState('')
  const [sampleLookface, setSampleLookface] = useState<number | null>(20)

  const faces = useQuery({
    queryKey: ['catalogs', 'faces'],
    queryFn: () => unwrap(api.GET('/api/catalogs/faces')),
  })

  const npc = faces.data?.npc ?? []
  const player = faces.data?.player ?? []
  const active = tab === 'player' ? player : npc

  const visible = useMemo(() => {
    const term = filter.trim()
    if (!term) return active
    return active.filter((f) => String(f.id).includes(term))
  }, [active, filter])

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={2}>{t('faces.title')}</Title>
        {faces.isFetching && <Loader size="sm" />}
      </Group>
      <Text c="dimmed">{t('faces.description')}</Text>

      <Paper withBorder p="md">
        <Stack gap="xs">
          <Text fw={600} size="sm">{t('faces.pickerTest')}</Text>
          <LookfacePicker mode="lookface" value={sampleLookface} onChange={setSampleLookface} />
        </Stack>
      </Paper>

      <ErrorAlert error={faces.error} />

      <Tabs value={tab} onChange={(v) => setTab(v ?? 'npc')}>
        <Tabs.List>
          <Tabs.Tab value="npc">{t('faces.tabs.npc', { count: npc.length })}</Tabs.Tab>
          <Tabs.Tab value="player">{t('faces.tabs.player', { count: player.length })}</Tabs.Tab>
        </Tabs.List>
      </Tabs>

      <TextInput
        maw={280}
        placeholder={t('faces.filterPlaceholder')}
        value={filter}
        onChange={(e) => setFilter(e.currentTarget.value)}
      />

      <SimpleGrid cols={{ base: 3, xs: 4, sm: 6, md: 8, lg: 10 }} spacing="xs">
        {visible.map((face) => (
          <Paper
            key={face.id}
            withBorder
            p="xs"
            style={{ cursor: 'pointer' }}
            onClick={() => setSampleLookface(tab === 'npc' ? face.id * 10 : face.id)}
          >
            <Stack align="center" gap={4}>
              <AssetImage path={face.path} alt={`#${face.id}`} size={56} fallback={<IconMoodSmile size={24} />} />
              <Text size="xs" fw={600}>#{face.id}</Text>
            </Stack>
          </Paper>
        ))}
      </SimpleGrid>

      {faces.data && visible.length === 0 && (
        <Text c="dimmed" ta="center" py="lg">{t('faces.empty')}</Text>
      )}
    </Stack>
  )
}
