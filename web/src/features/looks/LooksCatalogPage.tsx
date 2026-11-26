import { Badge, Code, Group, Loader, Modal, Paper, SimpleGrid, Stack, Tabs, Text, TextInput, Title } from '@mantine/core'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ErrorAlert } from '../../app/ErrorAlert'
import {
  LookSprite,
  matchesMonsterLook,
  matchesNpcLook,
  useLooks,
  type MonsterLook,
  type NpcLook,
} from '../../components/looks'

const pageLimit = 300

export default function LooksCatalogPage() {
  const { t } = useTranslation('looks')
  const looks = useLooks()
  const [tab, setTab] = useState<string>('npc')
  const [filter, setFilter] = useState('')
  const [selectedNpc, setSelectedNpc] = useState<NpcLook | null>(null)
  const [selectedMonster, setSelectedMonster] = useState<MonsterLook | null>(null)

  const npcTypes = looks.index.npcTypes
  const monsters = useMemo(() => [...looks.index.monster.values()], [looks.index])
  const npcVisible = useMemo(() => npcTypes.filter((l) => matchesNpcLook(l, filter)), [npcTypes, filter])
  const monsterVisible = useMemo(() => monsters.filter((l) => matchesMonsterLook(l, filter)), [monsters, filter])
  const visibleCount = tab === 'npc' ? npcVisible.length : monsterVisible.length

  // Every rendered direction of the selected NPC type
  const npcDirections = useMemo(() => {
    if (!selectedNpc) return []
    const type = Math.floor(selectedNpc.lookface / 10)
    return Array.from({ length: 10 }, (_, d) => looks.index.npc.get(type * 10 + d)).filter((l): l is NpcLook => !!l)
  }, [selectedNpc, looks.index])

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={2}>{t('title')}</Title>
        {looks.isLoading && <Loader size="sm" />}
      </Group>
      <Text c="dimmed">{t('description')}</Text>
      <ErrorAlert error={looks.error} />

      <Tabs value={tab} onChange={(v) => setTab(v ?? 'npc')}>
        <Tabs.List>
          <Tabs.Tab value="npc">{t('tabs.npc', { count: npcTypes.length })}</Tabs.Tab>
          <Tabs.Tab value="monster">{t('tabs.monster', { count: monsters.length })}</Tabs.Tab>
        </Tabs.List>
      </Tabs>

      <TextInput
        maw={320}
        placeholder={tab === 'npc' ? t('filter.npc') : t('filter.monster')}
        value={filter}
        onChange={(e) => setFilter(e.currentTarget.value)}
      />

      <SimpleGrid cols={{ base: 3, xs: 4, sm: 6, md: 8, lg: 10 }} spacing="xs">
        {tab === 'npc' &&
          npcVisible.slice(0, pageLimit).map((l) => (
            <Tile
              key={l.lookface}
              look={l}
              title={l.name ?? '—'}
              subtitle={`#${Math.floor(l.lookface / 10)}`}
              onClick={() => setSelectedNpc(l)}
            />
          ))}
        {tab === 'monster' &&
          monsterVisible
            .slice(0, pageLimit)
            .map((l) => (
              <Tile
                key={l.look}
                look={l}
                title={l.monsters[0]?.name ?? '—'}
                subtitle={`#${l.look}`}
                onClick={() => setSelectedMonster(l)}
              />
            ))}
      </SimpleGrid>

      {visibleCount > pageLimit && (
        <Text c="dimmed" ta="center" size="sm">
          {t('more', { count: visibleCount - pageLimit })}
        </Text>
      )}
      {!looks.isLoading && visibleCount === 0 && (
        <Text c="dimmed" ta="center" py="lg">
          {npcTypes.length + monsters.length === 0 ? t('notBuilt') : t('empty')}
        </Text>
      )}

      <Modal
        opened={!!selectedNpc}
        onClose={() => setSelectedNpc(null)}
        title={selectedNpc ? `${selectedNpc.name ?? '—'} (${t('npcType')} #${Math.floor(selectedNpc.lookface / 10)})` : ''}
        size="lg"
      >
        <Stack gap="sm">
          <Text size="sm">{t('directions')}</Text>
          <Group justify="center" align="flex-end">
            {npcDirections.map((l) => (
              <Stack key={l.lookface} align="center" gap={2}>
                <LookSprite look={l} alt={`${l.lookface}`} size={160} />
                <Code>{l.lookface}</Code>
              </Stack>
            ))}
          </Group>
        </Stack>
      </Modal>

      <Modal
        opened={!!selectedMonster}
        onClose={() => setSelectedMonster(null)}
        title={selectedMonster ? `${t('monsterLook')} #${selectedMonster.look}` : ''}
        size="md"
      >
        {selectedMonster && (
          <Stack gap="sm" align="center">
            <LookSprite look={selectedMonster} alt={`#${selectedMonster.look}`} size={240} />
            <Text size="sm" fw={600}>
              {t('usedBy')}
            </Text>
            <Group gap={4} justify="center">
              {selectedMonster.monsters.length === 0 && (
                <Text size="sm" c="dimmed">
                  {t('unused')}
                </Text>
              )}
              {selectedMonster.monsters.map((m) => (
                <Badge key={m.id} variant="light">
                  {m.name} #{m.id}
                </Badge>
              ))}
            </Group>
          </Stack>
        )}
      </Modal>
    </Stack>
  )
}

function Tile({
  look,
  title,
  subtitle,
  onClick,
}: {
  look: NpcLook | MonsterLook
  title: string
  subtitle: string
  onClick: () => void
}) {
  return (
    <Paper withBorder p={4} style={{ cursor: 'pointer' }} onClick={onClick}>
      <Stack align="center" gap={2}>
        <LookSprite look={look} alt={title} size={96} />
        <Text size="xs" fw={600} ta="center" lineClamp={1}>
          {title}
        </Text>
        <Text size="xs" c="dimmed">
          {subtitle}
        </Text>
      </Stack>
    </Paper>
  )
}
