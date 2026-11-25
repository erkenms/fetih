import {
  Badge,
  Button,
  Checkbox,
  Code,
  Divider,
  Group,
  NumberInput,
  Paper,
  ScrollArea,
  Select,
  SimpleGrid,
  Stack,
  Tabs,
  Text,
  TextInput,
} from '@mantine/core'
import { IconCrosshair, IconPlus, IconTrash } from '@tabler/icons-react'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { LookfacePicker, MapPicker, MonsterPicker } from '../../../components/pickers'
import { isCellWalkable } from '../../../lib/iso'
import type { DecodedGrid } from '../gridCanvasLayer'
import type { EditorSelection } from './MapEditorCanvas'
import type {
  EditableMapSettingsState,
  EditableNpcState,
  EditablePortalState,
  EditableSpawnState,
  MapEditorSnapshot,
} from './editorState'

const ROLE_PRESET_VALUES = ['1', '2', '3', '5', '10', '14', '19', '26'] as const

interface MapEditorSidebarProps {
  snapshot: MapEditorSnapshot
  availableFlags: string[]
  selected: EditorSelection | null
  activeTab: string
  decodedGrid: DecodedGrid | null
  onTabChange: (tab: string) => void
  onSelect: (selection: EditorSelection | null) => void
  onFocusCell: (x: number, y: number) => void
  onUpdateNpc: (id: number, patch: Partial<EditableNpcState>) => void
  onDeleteNpc: (id: number) => void
  onQuickAddNpc: () => void
  onUpdateSpawn: (id: number, patch: Partial<EditableSpawnState>) => void
  onDeleteSpawn: (id: number) => void
  onQuickAddSpawn: () => void
  onUpdatePortal: (index: number, patch: Partial<EditablePortalState>) => void
  onOpenPortalTargetPicker: (portal: EditablePortalState) => void
  onUpdateSettings: (patch: Partial<EditableMapSettingsState>) => void
  onStartPickStartPoint: () => void
}

export function MapEditorSidebar({
  snapshot,
  availableFlags,
  selected,
  activeTab,
  decodedGrid,
  onTabChange,
  onSelect,
  onFocusCell,
  onUpdateNpc,
  onDeleteNpc,
  onQuickAddNpc,
  onUpdateSpawn,
  onDeleteSpawn,
  onQuickAddSpawn,
  onUpdatePortal,
  onOpenPortalTargetPicker,
  onUpdateSettings,
  onStartPickStartPoint,
}: MapEditorSidebarProps) {
  const { t } = useTranslation('mapEdit')
  const [entitySearch, setEntitySearch] = useState('')

  const selectedNpc = useMemo(
    () => (selected?.type === 'npc' ? snapshot.npcs.find((n) => n.id === selected.id) ?? null : null),
    [selected, snapshot.npcs],
  )
  const selectedSpawn = useMemo(
    () => (selected?.type === 'spawn' ? snapshot.spawns.find((s) => s.id === selected.id) ?? null : null),
    [selected, snapshot.spawns],
  )
  const selectedPortal = useMemo(
    () => (selected?.type === 'portal' ? snapshot.portals.find((p) => p.index === selected.index) ?? null : null),
    [selected, snapshot.portals],
  )

  const term = entitySearch.trim().toLowerCase()
  const filteredNpcs = useMemo(
    () =>
      snapshot.npcs.filter(
        (n) => !term || String(n.id).includes(term) || (n.name?.toLowerCase().includes(term) ?? false),
      ),
    [snapshot.npcs, term],
  )
  const filteredSpawns = useMemo(
    () =>
      snapshot.spawns.filter(
        (s) =>
          !term ||
          String(s.id).includes(term) ||
          String(s.monsterId).includes(term) ||
          (s.monsterName?.toLowerCase().includes(term) ?? false),
      ),
    [snapshot.spawns, term],
  )
  const filteredPortals = useMemo(
    () =>
      snapshot.portals.filter(
        (p) =>
          !term ||
          String(p.index).includes(term) ||
          (p.toMap != null && String(p.toMap).includes(term)) ||
          (p.toMapName?.toLowerCase().includes(term) ?? false),
      ),
    [snapshot.portals, term],
  )

  const rolePresetData = useMemo(
    () =>
      ROLE_PRESET_VALUES.map((v) => ({
        value: v,
        label: t(`inspector.rolePresets.${v}`),
      })),
    [t],
  )

  const npcWalkable =
    selectedNpc && decodedGrid
      ? isCellWalkable(decodedGrid.walkable, decodedGrid.width, decodedGrid.height, selectedNpc.x, selectedNpc.y)
      : null

  const toggleFlag = (flag: string, checked: boolean) => {
    const next = checked
      ? [...snapshot.settings.flags.filter((f) => f !== flag), flag]
      : snapshot.settings.flags.filter((f) => f !== flag)
    onUpdateSettings({ flags: next })
  }

  return (
    <Paper withBorder w={{ base: 300, xl: 360 }} p="sm" style={{ display: 'flex', flexDirection: 'column', minHeight: 0, flexShrink: 0 }}>
      <Tabs
        value={activeTab}
        onChange={(val) => val && onTabChange(val)}
        style={{ display: 'flex', flexDirection: 'column', flex: 1, minHeight: 0 }}
      >
        <Tabs.List mb="xs">
          <Tabs.Tab value="inspector">{t('tabs.inspector')}</Tabs.Tab>
          <Tabs.Tab value="entities">{t('tabs.entities')}</Tabs.Tab>
          <Tabs.Tab value="settings">{t('tabs.settings')}</Tabs.Tab>
        </Tabs.List>

        {/* INSPECTOR TAB */}
        <Tabs.Panel value="inspector" style={{ flex: 1, minHeight: 0, overflow: 'auto' }}>
          {!selectedNpc && !selectedSpawn && !selectedPortal && (
            <Text size="sm" c="dimmed" py="md">
              {t('inspector.empty')}
            </Text>
          )}

          {selectedNpc && (
            <Stack gap="xs">
              <Group justify="space-between">
                <Text fw={700} size="sm">
                  {t('inspector.npcTitle', { id: selectedNpc.id })}
                </Text>
                {npcWalkable != null && (
                  <Badge size="xs" color={npcWalkable ? 'green' : 'red'}>
                    {npcWalkable ? t('inspector.walkable') : t('inspector.blocked')}
                  </Badge>
                )}
              </Group>

              <NumberInput
                label={t('inspector.id')}
                size="xs"
                min={1}
                allowDecimal={false}
                value={selectedNpc.id}
                onChange={(val) => {
                  const id = Number(val)
                  if (Number.isInteger(id) && id > 0) onUpdateNpc(selectedNpc.id, { id })
                }}
              />

              <LookfacePicker
                mode="lookface"
                label={t('inspector.lookface')}
                value={selectedNpc.lookface}
                clearable={false}
                onChange={(val) => {
                  if (val != null && val >= 0) onUpdateNpc(selectedNpc.id, { lookface: Math.min(65535, val) })
                }}
              />

              <Group grow align="flex-end">
                <NumberInput
                  label={t('inspector.role')}
                  size="xs"
                  min={0}
                  max={255}
                  allowDecimal={false}
                  value={selectedNpc.role}
                  onChange={(val) => {
                    const role = Number(val)
                    if (Number.isInteger(role) && role >= 0 && role <= 255) onUpdateNpc(selectedNpc.id, { role })
                  }}
                />
                <Select
                  size="xs"
                  placeholder="—"
                  data={rolePresetData}
                  value={ROLE_PRESET_VALUES.includes(String(selectedNpc.role) as (typeof ROLE_PRESET_VALUES)[number]) ? String(selectedNpc.role) : null}
                  onChange={(val) => {
                    if (val != null) onUpdateNpc(selectedNpc.id, { role: Number(val) })
                  }}
                />
              </Group>

              <Group grow>
                <NumberInput
                  label={t('inspector.x')}
                  size="xs"
                  min={0}
                  max={65535}
                  allowDecimal={false}
                  value={selectedNpc.x}
                  onChange={(val) => {
                    const x = Number(val)
                    if (Number.isInteger(x) && x >= 0) onUpdateNpc(selectedNpc.id, { x })
                  }}
                />
                <NumberInput
                  label={t('inspector.y')}
                  size="xs"
                  min={0}
                  max={65535}
                  allowDecimal={false}
                  value={selectedNpc.y}
                  onChange={(val) => {
                    const y = Number(val)
                    if (Number.isInteger(y) && y >= 0) onUpdateNpc(selectedNpc.id, { y })
                  }}
                />
              </Group>

              <Group grow>
                <NumberInput
                  label={t('inspector.life')}
                  size="xs"
                  min={0}
                  allowDecimal={false}
                  value={selectedNpc.life}
                  onChange={(val) => {
                    const life = Number(val)
                    if (Number.isInteger(life) && life >= 0) onUpdateNpc(selectedNpc.id, { life })
                  }}
                />
                <NumberInput
                  label={t('inspector.level')}
                  size="xs"
                  min={0}
                  max={255}
                  allowDecimal={false}
                  value={selectedNpc.level}
                  onChange={(val) => {
                    const level = Number(val)
                    if (Number.isInteger(level) && level >= 0 && level <= 255) onUpdateNpc(selectedNpc.id, { level })
                  }}
                />
              </Group>

              <Divider my={4} />
              <Button
                size="xs"
                color="red"
                variant="light"
                leftSection={<IconTrash size={14} />}
                onClick={() => onDeleteNpc(selectedNpc.id)}
              >
                {t('inspector.deleteNpc')}
              </Button>
            </Stack>
          )}

          {selectedSpawn && (
            <Stack gap="xs">
              <Text fw={700} size="sm">
                {t('inspector.spawnTitle', { id: selectedSpawn.id })}
              </Text>
              <Text size="xs" c="dimmed">
                {t('inspector.spawnDragHint')}
              </Text>

              <NumberInput
                label={t('inspector.id')}
                size="xs"
                min={1}
                allowDecimal={false}
                value={selectedSpawn.id}
                onChange={(val) => {
                  const id = Number(val)
                  if (Number.isInteger(id) && id > 0) onUpdateSpawn(selectedSpawn.id, { id })
                }}
              />

              <MonsterPicker
                label={t('inspector.monster')}
                value={selectedSpawn.monsterId}
                clearable={false}
                onChange={(monsterId, monster) => {
                  if (monsterId != null) {
                    onUpdateSpawn(selectedSpawn.id, {
                      monsterId,
                      monsterName: monster?.name ?? selectedSpawn.monsterName,
                      monsterLevel: monster?.level ?? monster?.clientLevel ?? selectedSpawn.monsterLevel,
                      monsterBoss: monster?.boss ?? false,
                    })
                  }
                }}
              />

              <Group grow>
                <NumberInput
                  label={t('inspector.x')}
                  size="xs"
                  min={0}
                  max={65535}
                  allowDecimal={false}
                  value={selectedSpawn.x}
                  onChange={(val) => {
                    const x = Number(val)
                    if (Number.isInteger(x) && x >= 0) onUpdateSpawn(selectedSpawn.id, { x })
                  }}
                />
                <NumberInput
                  label={t('inspector.y')}
                  size="xs"
                  min={0}
                  max={65535}
                  allowDecimal={false}
                  value={selectedSpawn.y}
                  onChange={(val) => {
                    const y = Number(val)
                    if (Number.isInteger(y) && y >= 0) onUpdateSpawn(selectedSpawn.id, { y })
                  }}
                />
              </Group>

              <NumberInput
                label={t('inspector.spread')}
                size="xs"
                min={0}
                max={255}
                allowDecimal={false}
                value={selectedSpawn.spread}
                onChange={(val) => {
                  const spread = Number(val)
                  if (Number.isInteger(spread) && spread >= 0) onUpdateSpawn(selectedSpawn.id, { spread })
                }}
              />

              <Group grow>
                <NumberInput
                  label={t('inspector.amount')}
                  size="xs"
                  min={0}
                  allowDecimal={false}
                  value={selectedSpawn.amount}
                  onChange={(val) => {
                    const amount = Number(val)
                    if (Number.isInteger(amount) && amount >= 0) onUpdateSpawn(selectedSpawn.id, { amount })
                  }}
                />
                <NumberInput
                  label={t('inspector.respawn')}
                  size="xs"
                  min={0}
                  allowDecimal={false}
                  value={selectedSpawn.respawn}
                  onChange={(val) => {
                    const respawn = Number(val)
                    if (Number.isInteger(respawn) && respawn >= 0) onUpdateSpawn(selectedSpawn.id, { respawn })
                  }}
                />
              </Group>

              <Divider my={4} />
              <Button
                size="xs"
                color="red"
                variant="light"
                leftSection={<IconTrash size={14} />}
                onClick={() => onDeleteSpawn(selectedSpawn.id)}
              >
                {t('inspector.deleteSpawn')}
              </Button>
            </Stack>
          )}

          {selectedPortal && (
            <Stack gap="xs">
              <Text fw={700} size="sm">
                {t('inspector.portalTitle', { index: selectedPortal.index })}
              </Text>
              <Text size="xs" c="dimmed">
                {t('inspector.sourceCell')}:{' '}
                {selectedPortal.x != null && selectedPortal.y != null ? (
                  <Code>
                    ({selectedPortal.x}, {selectedPortal.y})
                  </Code>
                ) : (
                  t('inspector.noSourceCell')
                )}
              </Text>

              <MapPicker
                label={t('inspector.targetMap')}
                value={selectedPortal.toMap}
                onChange={(toMap, m) =>
                  onUpdatePortal(selectedPortal.index, {
                    toMap,
                    toMapName: m?.name ?? m?.serverName ?? null,
                    toX: toMap == null ? null : (selectedPortal.toX ?? 0),
                    toY: toMap == null ? null : (selectedPortal.toY ?? 0),
                  })
                }
              />

              <Group grow>
                <NumberInput
                  label={t('inspector.targetX')}
                  size="xs"
                  min={0}
                  max={65535}
                  allowDecimal={false}
                  disabled={selectedPortal.toMap == null}
                  value={selectedPortal.toX ?? ''}
                  onChange={(val) => {
                    const toX = val === '' ? null : Number(val)
                    onUpdatePortal(selectedPortal.index, { toX })
                  }}
                />
                <NumberInput
                  label={t('inspector.targetY')}
                  size="xs"
                  min={0}
                  max={65535}
                  allowDecimal={false}
                  disabled={selectedPortal.toMap == null}
                  value={selectedPortal.toY ?? ''}
                  onChange={(val) => {
                    const toY = val === '' ? null : Number(val)
                    onUpdatePortal(selectedPortal.index, { toY })
                  }}
                />
              </Group>

              <Button
                size="xs"
                variant="light"
                color="cyan"
                leftSection={<IconCrosshair size={14} />}
                onClick={() => onOpenPortalTargetPicker(selectedPortal)}
              >
                {t('inspector.pickOnTargetMap')}
              </Button>

              {selectedPortal.toMap != null && (
                <Button
                  size="xs"
                  variant="subtle"
                  color="red"
                  onClick={() =>
                    onUpdatePortal(selectedPortal.index, {
                      toMap: null,
                      toMapName: null,
                      toX: null,
                      toY: null,
                    })
                  }
                >
                  {t('inspector.clearPortal')}
                </Button>
              )}
            </Stack>
          )}
        </Tabs.Panel>

        {/* ENTITIES TAB */}
        <Tabs.Panel value="entities" style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
          <TextInput
            size="xs"
            mb="xs"
            placeholder={t('entities.search')}
            value={entitySearch}
            onChange={(e) => setEntitySearch(e.currentTarget.value)}
          />
          <ScrollArea style={{ flex: 1 }}>
            <Stack gap="sm">
              <Stack gap={4}>
                <Group justify="space-between">
                  <Text size="xs" fw={700} c="dimmed" tt="uppercase">
                    {t('entities.npcs', { count: filteredNpcs.length })}
                  </Text>
                  <Button size="compact-xs" variant="subtle" leftSection={<IconPlus size={12} />} onClick={onQuickAddNpc}>
                    {t('entities.addNpcQuick')}
                  </Button>
                </Group>
                {filteredNpcs.slice(0, 120).map((n) => (
                  <Button
                    key={n.id}
                    size="xs"
                    variant={selected?.type === 'npc' && selected.id === n.id ? 'light' : 'subtle'}
                    justify="space-between"
                    onClick={() => {
                      onSelect({ type: 'npc', id: n.id })
                      onFocusCell(n.x, n.y)
                    }}
                  >
                    <span>{n.name ? `${n.name} (#${n.id})` : `#${n.id}`}</span>
                    <Code fz="xs">
                      {n.x},{n.y}
                    </Code>
                  </Button>
                ))}
                {filteredNpcs.length === 0 && (
                  <Text size="xs" c="dimmed">
                    {t('entities.empty')}
                  </Text>
                )}
              </Stack>

              <Stack gap={4}>
                <Group justify="space-between">
                  <Text size="xs" fw={700} c="dimmed" tt="uppercase">
                    {t('entities.spawns', { count: filteredSpawns.length })}
                  </Text>
                  <Button size="compact-xs" variant="subtle" color="orange" leftSection={<IconPlus size={12} />} onClick={onQuickAddSpawn}>
                    {t('entities.addSpawnQuick')}
                  </Button>
                </Group>
                {filteredSpawns.slice(0, 120).map((s) => (
                  <Button
                    key={s.id}
                    size="xs"
                    color={s.monsterBoss ? 'red' : 'orange'}
                    variant={selected?.type === 'spawn' && selected.id === s.id ? 'light' : 'subtle'}
                    justify="space-between"
                    onClick={() => {
                      onSelect({ type: 'spawn', id: s.id })
                      onFocusCell(s.x, s.y)
                    }}
                  >
                    <span>
                      {s.monsterName ?? `#${s.monsterId}`} ×{s.amount}
                    </span>
                    <Code fz="xs">
                      {s.x},{s.y}
                    </Code>
                  </Button>
                ))}
                {filteredSpawns.length === 0 && (
                  <Text size="xs" c="dimmed">
                    {t('entities.empty')}
                  </Text>
                )}
              </Stack>

              <Stack gap={4}>
                <Text size="xs" fw={700} c="dimmed" tt="uppercase">
                  {t('entities.portals', { count: filteredPortals.length })}
                </Text>
                {filteredPortals.map((p) => (
                  <Button
                    key={p.index}
                    size="xs"
                    color="cyan"
                    variant={selected?.type === 'portal' && selected.index === p.index ? 'light' : 'subtle'}
                    justify="space-between"
                    onClick={() => {
                      onSelect({ type: 'portal', index: p.index })
                      if (p.x != null && p.y != null) onFocusCell(p.x, p.y)
                    }}
                  >
                    <span>
                      #{p.index} → {p.toMapName ?? (p.toMap != null ? `#${p.toMap}` : t('entities.unlinked'))}
                    </span>
                    {p.x != null && p.y != null && (
                      <Code fz="xs">
                        {p.x},{p.y}
                      </Code>
                    )}
                  </Button>
                ))}
                {filteredPortals.length === 0 && (
                  <Text size="xs" c="dimmed">
                    {t('entities.empty')}
                  </Text>
                )}
              </Stack>
            </Stack>
          </ScrollArea>
        </Tabs.Panel>

        {/* SETTINGS TAB */}
        <Tabs.Panel value="settings" style={{ flex: 1, minHeight: 0, overflow: 'auto' }}>
          <Stack gap="xs">
            <TextInput
              label={t('settings.serverName')}
              placeholder={t('settings.serverNamePlaceholder')}
              size="xs"
              value={snapshot.settings.name ?? ''}
              onChange={(e) => onUpdateSettings({ name: e.currentTarget.value || null })}
            />

            <TextInput
              label={t('settings.aliases')}
              placeholder={t('settings.aliasesPlaceholder')}
              size="xs"
              value={snapshot.settings.aliases.join(', ')}
              onChange={(e) => {
                const raw = e.currentTarget.value
                const aliases = raw
                  .split(',')
                  .map((s) => s.trim())
                  .filter(Boolean)
                onUpdateSettings({ aliases })
              }}
            />

            <Divider my={4} label={t('settings.startPoint')} labelPosition="left" />

            <Group grow>
              <NumberInput
                label={t('settings.startX')}
                size="xs"
                min={0}
                max={65535}
                allowDecimal={false}
                value={snapshot.settings.portal?.x ?? ''}
                onChange={(val) => {
                  if (val === '') {
                    onUpdateSettings({ portal: null })
                  } else {
                    onUpdateSettings({
                      portal: { x: Number(val), y: snapshot.settings.portal?.y ?? 0 },
                    })
                  }
                }}
              />
              <NumberInput
                label={t('settings.startY')}
                size="xs"
                min={0}
                max={65535}
                allowDecimal={false}
                value={snapshot.settings.portal?.y ?? ''}
                onChange={(val) => {
                  if (val === '') {
                    onUpdateSettings({ portal: null })
                  } else {
                    onUpdateSettings({
                      portal: { x: snapshot.settings.portal?.x ?? 0, y: Number(val) },
                    })
                  }
                }}
              />
            </Group>

            <Group gap="xs">
              <Button
                size="xs"
                variant="light"
                color="teal"
                leftSection={<IconCrosshair size={14} />}
                onClick={onStartPickStartPoint}
              >
                {t('settings.pickStartOnMap')}
              </Button>
              {snapshot.settings.portal && (
                <Button size="xs" variant="subtle" color="gray" onClick={() => onUpdateSettings({ portal: null })}>
                  {t('settings.clearStart')}
                </Button>
              )}
            </Group>

            <MapPicker
              label={t('settings.rebornMap')}
              value={snapshot.settings.rebornMap}
              onChange={(rebornMap) => onUpdateSettings({ rebornMap })}
            />

            <Group grow>
              <NumberInput
                label={t('settings.scrollTypeId')}
                size="xs"
                min={0}
                allowDecimal={false}
                value={snapshot.settings.scrollTypeId ?? ''}
                onChange={(val) => onUpdateSettings({ scrollTypeId: val === '' ? null : Number(val) })}
              />
              <NumberInput
                label={t('settings.ridePlus')}
                size="xs"
                min={0}
                max={12}
                allowDecimal={false}
                value={snapshot.settings.ridePlus}
                onChange={(val) => onUpdateSettings({ ridePlus: val === '' ? 0 : Number(val) })}
              />
            </Group>

            <Divider my={4} label={t('settings.flags')} labelPosition="left" />

            <SimpleGrid cols={2} spacing={6}>
              {availableFlags.map((flag) => (
                <Checkbox
                  key={flag}
                  size="xs"
                  label={flag}
                  checked={snapshot.settings.flags.includes(flag)}
                  onChange={(e) => toggleFlag(flag, e.currentTarget.checked)}
                />
              ))}
            </SimpleGrid>
          </Stack>
        </Tabs.Panel>
      </Tabs>
    </Paper>
  )
}
