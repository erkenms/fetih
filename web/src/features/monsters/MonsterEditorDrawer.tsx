import {
  ActionIcon,
  Alert,
  Badge,
  Button,
  Checkbox,
  Code,
  Divider,
  Drawer,
  Group,
  Loader,
  Modal,
  NumberInput,
  Paper,
  ScrollArea,
  SimpleGrid,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { IconAlertTriangle, IconCopy, IconMoodSmile, IconSparkles, IconTrash } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap, type Schemas } from '../../api/client'
import { ItemPicker } from '../../components/items'
import { AssetImage, LookfacePicker, SpellPicker } from '../../components/pickers'
import {
  ATTACK_USER_AGGRESSIVE,
  ATTACK_USER_GUARD,
  hasAttackUserFlag,
  toggleAttackUserFlag,
  validateMonsterDraft,
  type MonsterDraft,
} from './monsterUtils'

type MonsterProblem = Schemas['MonsterProblemDto']

export interface MonsterEditorDrawerProps {
  opened: boolean
  mode: 'create' | 'edit'
  initialDraft: MonsterDraft | null
  originalId: number | null
  existingIds: Set<number>
  saving: boolean
  deleting: boolean
  serverProblems: MonsterProblem[]
  onClose: () => void
  onSave: (draft: MonsterDraft) => void
  onDelete: (id: number) => void
  onDuplicate: (draft: MonsterDraft) => void
}

function num(v: string | number, fallback = 0): number {
  if (typeof v === 'number' && !Number.isNaN(v)) return v
  const parsed = Number(v)
  return Number.isNaN(parsed) ? fallback : parsed
}

export function MonsterEditorDrawer({
  opened,
  mode,
  initialDraft,
  originalId,
  existingIds,
  saving,
  deleting,
  serverProblems,
  onClose,
  onSave,
  onDelete,
  onDuplicate,
}: MonsterEditorDrawerProps) {
  const { t } = useTranslation('monsters')
  const [draft, setDraft] = useState<MonsterDraft | null>(initialDraft)
  const [deleteConfirmOpen, setDeleteConfirmOpen] = useState(false)

  useEffect(() => {
    setDraft(initialDraft)
    setDeleteConfirmOpen(false)
  }, [initialDraft, opened])

  const detail = useQuery({
    queryKey: ['studio', 'monsters', 'detail', originalId],
    queryFn: () =>
      unwrap(
        api.GET('/api/studio/monsters/{id}', {
          params: { path: { id: originalId! } },
        }),
      ),
    enabled: opened && mode === 'edit' && originalId != null,
  })

  const faces = useQuery({
    queryKey: ['catalogs', 'faces'],
    queryFn: () => unwrap(api.GET('/api/catalogs/faces')),
    enabled: opened,
  })

  const spellsCatalog = useQuery({
    queryKey: ['catalogs', 'spells', 'all-for-monsters'],
    queryFn: () =>
      unwrap(
        api.GET('/api/catalogs/spells', {
          params: { query: { pageSize: 1000 } },
        }),
      ),
    enabled: opened,
  })

  const faceLookup = useMemo(() => {
    const map = new Map<number, string>()
    if (!faces.data) return map
    for (const f of faces.data.npc) map.set(f.id, f.path)
    for (const f of faces.data.player) if (!map.has(f.id)) map.set(f.id, f.path)
    return map
  }, [faces.data])

  const spellLookup = useMemo(() => {
    const map = new Map<number, Schemas['SpellCatalogEntry']>()
    for (const s of spellsCatalog.data?.items ?? []) map.set(s.type, s)
    return map
  }, [spellsCatalog.data])

  const clientErrors = useMemo(
    () => (draft ? validateMonsterDraft(draft, existingIds, mode === 'edit' ? originalId : null) : []),
    [draft, existingIds, mode, originalId],
  )

  if (!draft) return null

  const update = <K extends keyof MonsterDraft>(key: K, value: MonsterDraft[K]) => {
    setDraft((prev) => (prev ? { ...prev, [key]: value } : prev))
  }

  const facePath = faceLookup.get(draft.lookface) ?? null
  const allProblems = [...serverProblems, ...(detail.data?.problems ?? [])]

  return (
    <>
      <Drawer
        opened={opened}
        onClose={onClose}
        position="right"
        size="xl"
        title={
          <Group gap="sm">
            <Title order={4}>
              {mode === 'create'
                ? t('drawer.createTitle')
                : t('drawer.editTitle', { name: draft.name || `#${draft.id}`, id: draft.id })}
            </Title>
            {draft.boss && (
              <Badge color="red" variant="light">
                {t('badge.boss')}
              </Badge>
            )}
            {detail.data && (
              <Text size="xs" c="dimmed">
                {t('drawer.lineHint', { line: detail.data.line })}
              </Text>
            )}
          </Group>
        }
      >
        <Stack gap="md" pb="xl">
          {(clientErrors.length > 0 || allProblems.length > 0) && (
            <Alert
              color={clientErrors.length > 0 || allProblems.some((p) => p.level === 'error') ? 'red' : 'yellow'}
              icon={<IconAlertTriangle size={16} />}
            >
              <Stack gap={4}>
                {clientErrors.map((key) => (
                  <Text key={key} size="sm">
                    {t(`validation.${key}`)}
                  </Text>
                ))}
                {allProblems.map((p, idx) => (
                  <Text key={`${p.file}-${p.line}-${idx}`} size="sm">
                    {p.line ? `${p.file}:${p.line} — ` : `${p.file} — `}
                    {p.message}
                  </Text>
                ))}
              </Stack>
            </Alert>
          )}

          {/* Identity & Look Preview */}
          <Text fw={600} size="sm">
            {t('section.identity')}
          </Text>
          <Group align="flex-start" wrap="nowrap">
            <Paper withBorder p="sm" w={132} style={{ flexShrink: 0 }}>
              <Stack align="center" gap={6}>
                <Text size="xs" c="dimmed">
                  {t('lookPreview.title')}
                </Text>
                <AssetImage
                  path={facePath}
                  alt={t('lookPreview.id', { id: draft.lookface })}
                  size={72}
                  fallback={<IconMoodSmile size={32} />}
                />
                <Code>{draft.lookface}</Code>
                {!facePath && (
                  <Text size="xs" c="dimmed" ta="center">
                    {t('lookPreview.noAsset')}
                  </Text>
                )}
              </Stack>
            </Paper>

            <Stack style={{ flex: 1 }} gap="sm">
              <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="sm">
                <NumberInput
                  label={t('field.id')}
                  value={draft.id}
                  min={1}
                  allowDecimal={false}
                  onChange={(v) => update('id', num(v, 0))}
                />
                <TextInput
                  label={t('field.name')}
                  value={draft.name}
                  onChange={(e) => update('name', e.currentTarget.value)}
                />
                <NumberInput
                  label={t('field.level')}
                  value={draft.level}
                  min={0}
                  max={255}
                  allowDecimal={false}
                  onChange={(v) => update('level', num(v, 0))}
                />
                <Group align="flex-end" h="100%" pb={6}>
                  <Switch
                    label={t('field.boss')}
                    checked={draft.boss}
                    onChange={(e) => update('boss', e.currentTarget.checked)}
                  />
                </Group>
              </SimpleGrid>

              <LookfacePicker
                label={t('field.lookface')}
                mode="npc"
                clearable={false}
                value={draft.lookface}
                onChange={(v) => update('lookface', v ?? 0)}
              />
            </Stack>
          </Group>

          <Divider />

          {/* Combat & Movement */}
          <Text fw={600} size="sm">
            {t('section.combat')}
          </Text>
          <SimpleGrid cols={{ base: 2, sm: 4 }} spacing="sm">
            <NumberInput
              label={t('field.life')}
              value={draft.life}
              min={1}
              allowDecimal={false}
              onChange={(v) => update('life', num(v, 1))}
            />
            <NumberInput
              label={t('field.attackMin')}
              value={draft.attackMin}
              min={0}
              allowDecimal={false}
              onChange={(v) => update('attackMin', num(v, 0))}
            />
            <NumberInput
              label={t('field.attackMax')}
              value={draft.attackMax}
              min={0}
              allowDecimal={false}
              onChange={(v) => update('attackMax', num(v, 0))}
            />
            <NumberInput
              label={t('field.defense')}
              value={draft.defense}
              min={0}
              allowDecimal={false}
              onChange={(v) => update('defense', num(v, 0))}
            />
            <NumberInput
              label={t('field.dexterity')}
              value={draft.dexterity}
              min={0}
              allowDecimal={false}
              onChange={(v) => update('dexterity', num(v, 0))}
            />
            <NumberInput
              label={t('field.dodge')}
              value={draft.dodge}
              min={0}
              allowDecimal={false}
              onChange={(v) => update('dodge', num(v, 0))}
            />
            <NumberInput
              label={t('field.attackRange')}
              value={draft.attackRange}
              min={0}
              allowDecimal={false}
              onChange={(v) => update('attackRange', num(v, 0))}
            />
            <NumberInput
              label={t('field.viewRange')}
              value={draft.viewRange}
              min={0}
              allowDecimal={false}
              onChange={(v) => update('viewRange', num(v, 0))}
            />
            <NumberInput
              label={t('field.attackSpeed')}
              value={draft.attackSpeed}
              min={0}
              allowDecimal={false}
              onChange={(v) => update('attackSpeed', num(v, 0))}
            />
            <NumberInput
              label={t('field.moveSpeed')}
              value={draft.moveSpeed}
              min={0}
              allowDecimal={false}
              onChange={(v) => update('moveSpeed', num(v, 0))}
            />
          </SimpleGrid>

          <Divider />

          {/* AI & Behavior */}
          <Text fw={600} size="sm">
            {t('section.behavior')}
          </Text>
          <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="sm">
            <NumberInput
              label={t('field.attackUser')}
              value={draft.attackUser}
              min={0}
              allowDecimal={false}
              onChange={(v) => update('attackUser', num(v, 0))}
            />
            <Stack gap="xs" justify="center">
              <Checkbox
                label={t('field.flagAggressive')}
                checked={hasAttackUserFlag(draft.attackUser, ATTACK_USER_AGGRESSIVE)}
                onChange={(e) =>
                  update('attackUser', toggleAttackUserFlag(draft.attackUser, ATTACK_USER_AGGRESSIVE, e.currentTarget.checked))
                }
              />
              <Checkbox
                label={t('field.flagGuard')}
                checked={hasAttackUserFlag(draft.attackUser, ATTACK_USER_GUARD)}
                onChange={(e) =>
                  update('attackUser', toggleAttackUserFlag(draft.attackUser, ATTACK_USER_GUARD, e.currentTarget.checked))
                }
              />
            </Stack>
          </SimpleGrid>

          <Divider />

          {/* Spells & Magic */}
          <Text fw={600} size="sm">
            {t('section.magic')}
          </Text>
          <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="sm">
            <SpellPicker
              label={t('field.magicType')}
              value={draft.magicType > 0 ? draft.magicType : null}
              onChange={(type) => update('magicType', type ?? 0)}
            />
            <NumberInput
              label={t('field.magicHitRate')}
              value={draft.magicHitRate}
              min={0}
              max={100}
              allowDecimal={false}
              onChange={(v) => update('magicHitRate', num(v, 100))}
            />
          </SimpleGrid>

          <Stack gap="xs">
            <Text size="sm" fw={500}>
              {t('field.spells')}
            </Text>
            {draft.spells.length > 0 ? (
              <Group gap="xs" wrap="wrap">
                {draft.spells.map((spellId) => {
                  const info = spellLookup.get(spellId)
                  return (
                    <Paper key={spellId} withBorder px="xs" py={4}>
                      <Group gap="xs" wrap="nowrap">
                        <AssetImage
                          path={info?.icon}
                          alt={info?.name ?? `#${spellId}`}
                          size={20}
                          fallback={<IconSparkles size={14} />}
                        />
                        <Text size="sm">
                          {info?.name ?? `#${spellId}`}{' '}
                          <Text span size="xs" c="dimmed">
                            #{spellId}
                          </Text>
                        </Text>
                        {info?.sort && (
                          <Badge size="xs" variant="light">
                            {info.sort}
                          </Badge>
                        )}
                        <ActionIcon
                          size="xs"
                          variant="subtle"
                          color="red"
                          aria-label={t('spells.remove')}
                          onClick={() =>
                            update(
                              'spells',
                              draft.spells.filter((id) => id !== spellId),
                            )
                          }
                        >
                          <IconTrash size={12} />
                        </ActionIcon>
                      </Group>
                    </Paper>
                  )
                })}
              </Group>
            ) : (
              <Text size="xs" c="dimmed">
                {t('spells.empty')}
              </Text>
            )}
            <SpellPicker
              label={t('field.addSpell')}
              value={null}
              clearable={false}
              onChange={(type) => {
                if (type != null && type > 0 && !draft.spells.includes(type)) {
                  update('spells', [...draft.spells, type])
                }
              }}
            />
          </Stack>

          <Divider />

          {/* Drops */}
          <Text fw={600} size="sm">
            {t('section.drops')}
          </Text>
          <NumberInput
            label={t('field.dropMoney')}
            value={draft.dropMoney}
            min={0}
            allowDecimal={false}
            onChange={(v) => update('dropMoney', num(v, 0))}
          />
          <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="sm">
            <ItemPicker
              label={t('field.dropHp')}
              value={draft.dropHp > 0 ? draft.dropHp : null}
              category="Consumable"
              onChange={(item) => update('dropHp', item?.id ?? 0)}
            />
            <ItemPicker
              label={t('field.dropMp')}
              value={draft.dropMp > 0 ? draft.dropMp : null}
              category="Consumable"
              onChange={(item) => update('dropMp', item?.id ?? 0)}
            />
          </SimpleGrid>

          {mode === 'edit' && detail.data && detail.data.drops.length > 0 && (
            <Paper withBorder p="sm">
              <Stack gap="xs">
                <Text size="sm" fw={600}>
                  {t('drops.customTitle')}
                </Text>
                <Text size="xs" c="dimmed">
                  {t('drops.customHint')}
                </Text>
                <Group gap="xs" wrap="wrap">
                  {detail.data.drops.map((d, i) => (
                    <Badge key={`${d.itemId}-${i}`} variant="light">
                      {t('drops.item', { id: d.itemId })} · {t('drops.chance', { chance: d.chance })} ·{' '}
                      {t('drops.count', { count: d.count })}
                    </Badge>
                  ))}
                </Group>
              </Stack>
            </Paper>
          )}

          {/* Where it spawns */}
          {mode === 'edit' && (
            <>
              <Divider />
              <Group justify="space-between">
                <Text fw={600} size="sm">
                  {t('section.spawns')}
                </Text>
                {detail.isFetching && <Loader size="xs" />}
              </Group>

              {detail.data && detail.data.spawns.length === 0 && detail.data.bossSpawns.length === 0 && (
                <Text size="sm" c="dimmed">
                  {t('spawns.empty')}
                </Text>
              )}

              {detail.data && detail.data.spawns.length > 0 && (
                <Stack gap="xs">
                  <Text size="xs" fw={600} c="dimmed">
                    {t('spawns.generatorsHeading', { count: detail.data.spawns.length })}
                  </Text>
                  <ScrollArea.Autosize mah={220}>
                    <Table striped withTableBorder>
                      <Table.Thead>
                        <Table.Tr>
                          <Table.Th>{t('spawns.column.id')}</Table.Th>
                          <Table.Th>{t('spawns.column.map')}</Table.Th>
                          <Table.Th>{t('spawns.column.coords')}</Table.Th>
                          <Table.Th>{t('spawns.column.spread')}</Table.Th>
                          <Table.Th>{t('spawns.column.amount')}</Table.Th>
                          <Table.Th>{t('spawns.column.respawn')}</Table.Th>
                        </Table.Tr>
                      </Table.Thead>
                      <Table.Tbody>
                        {detail.data.spawns.map((s) => (
                          <Table.Tr key={s.id}>
                            <Table.Td>
                              <Code>{s.id}</Code>
                            </Table.Td>
                            <Table.Td>
                              {s.mapName ? `${s.mapName} (#${s.mapId})` : `#${s.mapId}`}
                            </Table.Td>
                            <Table.Td>
                              ({s.x}, {s.y})
                            </Table.Td>
                            <Table.Td>{s.spread}</Table.Td>
                            <Table.Td>{s.amount}</Table.Td>
                            <Table.Td>{s.respawnSeconds}s</Table.Td>
                          </Table.Tr>
                        ))}
                      </Table.Tbody>
                    </Table>
                  </ScrollArea.Autosize>
                </Stack>
              )}

              {detail.data && detail.data.bossSpawns.length > 0 && (
                <Stack gap="xs">
                  <Text size="xs" fw={600} c="dimmed">
                    {t('spawns.bossesHeading', { count: detail.data.bossSpawns.length })}
                  </Text>
                  <Table striped withTableBorder>
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th>{t('spawns.column.id')}</Table.Th>
                        <Table.Th>{t('spawns.column.map')}</Table.Th>
                        <Table.Th>{t('spawns.column.coords')}</Table.Th>
                        <Table.Th>{t('spawns.column.schedule')}</Table.Th>
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {detail.data.bossSpawns.map((b) => (
                        <Table.Tr key={b.id}>
                          <Table.Td>
                            <Code>{b.id}</Code>
                          </Table.Td>
                          <Table.Td>
                            {b.mapName ? `${b.mapName} (#${b.mapId})` : `#${b.mapId}`}
                          </Table.Td>
                          <Table.Td>
                            ({b.x}, {b.y}) ±{b.spread}
                          </Table.Td>
                          <Table.Td>
                            {b.times.length > 0
                              ? b.times.join(', ')
                              : t('spawns.everySeconds', { seconds: b.intervalSeconds })}
                          </Table.Td>
                        </Table.Tr>
                      ))}
                    </Table.Tbody>
                  </Table>
                </Stack>
              )}
            </>
          )}

          <Divider />

          {/* Footer actions */}
          <Group justify="space-between">
            <Group gap="xs">
              {mode === 'edit' && originalId != null && (
                <>
                  <Button
                    color="red"
                    variant="light"
                    leftSection={<IconTrash size={16} />}
                    loading={deleting}
                    onClick={() => setDeleteConfirmOpen(true)}
                  >
                    {t('action.delete')}
                  </Button>
                  <Button
                    variant="default"
                    leftSection={<IconCopy size={16} />}
                    onClick={() => onDuplicate(draft)}
                  >
                    {t('action.duplicate')}
                  </Button>
                </>
              )}
            </Group>
            <Group gap="xs">
              <Button variant="default" onClick={onClose}>
                {t('action.cancel')}
              </Button>
              <Button
                loading={saving}
                disabled={clientErrors.length > 0}
                onClick={() => onSave(draft)}
              >
                {mode === 'create' ? t('action.create') : t('action.save')}
              </Button>
            </Group>
          </Group>
        </Stack>
      </Drawer>

      <Modal
        opened={deleteConfirmOpen}
        onClose={() => setDeleteConfirmOpen(false)}
        title={t('delete.title', { name: draft.name, id: originalId })}
      >
        <Stack>
          <Text size="sm">{t('delete.body')}</Text>
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setDeleteConfirmOpen(false)}>
              {t('action.cancel')}
            </Button>
            <Button
              color="red"
              loading={deleting}
              onClick={() => {
                if (originalId != null) onDelete(originalId)
                setDeleteConfirmOpen(false)
              }}
            >
              {t('delete.confirm')}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </>
  )
}
