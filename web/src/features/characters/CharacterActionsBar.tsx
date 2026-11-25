import {
  Button,
  Group,
  Modal,
  NumberInput,
  ScrollArea,
  Select,
  SimpleGrid,
  Stack,
  Text,
  Textarea,
  TextInput,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import {
  IconArrowUpCircle,
  IconBan,
  IconCheck,
  IconCoin,
  IconDiamond,
  IconGift,
  IconLock,
  IconMapPin,
  IconMessage,
  IconUserOff,
  IconVolumeOff,
} from '@tabler/icons-react'
import { useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError, api, unwrap, type Schemas } from '../../api/client'
import { useErrorText } from '../../app/ErrorAlert'
import { ItemPicker, decodeSocket, type CatalogItem } from '../../components/items'
import { MapPicker } from '../../components/pickers'
import { GEM_SOCKET_CODES } from './characterMeta'

type ActionKind =
  | 'kick'
  | 'mute'
  | 'jail'
  | 'message'
  | 'giveItem'
  | 'silver'
  | 'cp'
  | 'level'
  | 'teleport'
  | 'ban'
  | 'unban'
  | null

interface CharacterActionsBarProps {
  // One character, or with bulkAccountIds every character of those accounts (ban and unban too)
  characterId?: number
  characterName?: string
  bulkAccountIds?: number[]
  currentLevel?: number
  currentMapId?: number
  currentX?: number
  currentY?: number
  online?: boolean
  muted?: boolean
  canModerate: boolean
  canManage: boolean
  onChanged: () => void
}

type BulkFailure = Schemas['PlayerBulkFailure']

export function CharacterActionsBar({
  characterId = 0,
  characterName = '',
  bulkAccountIds,
  currentLevel = 1,
  currentMapId,
  currentX = 0,
  currentY = 0,
  online = true,
  muted = false,
  canModerate,
  canManage,
  onChanged,
}: CharacterActionsBarProps) {
  const { t } = useTranslation(['characters', 'items'])
  const formatError = useErrorText('characters')

  const [modal, setModal] = useState<ActionKind>(null)
  const [error, setError] = useState<unknown>(null)

  const [reason, setReason] = useState('')
  const [minutes, setMinutes] = useState<number | string>(30)
  const [messageText, setMessageText] = useState('')
  const [pickedItem, setPickedItem] = useState<CatalogItem | null>(null)
  const [count, setCount] = useState<number | string>(1)
  const [plus, setPlus] = useState<number | string>(0)
  const [bless, setBless] = useState<number | string>(0)
  const [gem1, setGem1] = useState<string>('default')
  const [gem2, setGem2] = useState<string>('default')
  const [amount, setAmount] = useState<number | string>(0)
  const [level, setLevel] = useState<number | string>(currentLevel)
  const [mapId, setMapId] = useState<number | null>(currentMapId ?? null)
  const [failures, setFailures] = useState<BulkFailure[]>([])
  const bulk = bulkAccountIds !== undefined
  const count_ = bulkAccountIds?.length ?? 0
  const [x, setX] = useState<number | string>(currentX)
  const [y, setY] = useState<number | string>(currentY)

  const openModal = (kind: ActionKind) => {
    setError(null)
    setFailures([])
    setReason('')
    setMessageText('')
    if (kind === 'mute') setMinutes(muted ? 0 : 30)
    if (kind === 'level') setLevel(currentLevel)
    if (kind === 'teleport') {
      setMapId(currentMapId ?? null)
      setX(currentX)
      setY(currentY)
    }
    if (kind === 'silver' || kind === 'cp') setAmount(0)
    if (kind === 'giveItem') {
      setPickedItem(null)
      setCount(1)
      setPlus(0)
      setBless(0)
      setGem1('default')
      setGem2('default')
    }
    setModal(kind)
  }

  const parseGem = (val: string): number | null =>
    val === 'default' ? null : Number(val)

  const gemOptions = [
    { value: 'default', label: t('characters:actions.gemDefault') },
    { value: '0', label: t('characters:actions.gemNone') },
    { value: '255', label: t('characters:actions.gemEmpty') },
    ...GEM_SOCKET_CODES.map((code) => {
      const decoded = decodeSocket(code)
      const grade = decoded?.grade ? t(`items:gem.grade.${decoded.grade}` as never) : ''
      const kind = decoded?.kind !== undefined ? t(`items:gem.kind.${decoded.kind}` as never) : `#${code}`
      return { value: String(code), label: `${grade} ${kind}`.trim() }
    }),
  ]

  const runMutation = useMutation({
    mutationFn: async (kind: Exclude<ActionKind, null>) => {
      if (bulk) {
        return unwrap(
          api.POST('/api/players/bulk', {
            body: {
              action: kind,
              accountIds: bulkAccountIds,
              reason: reason.trim() || null,
              minutes: Number(minutes) || 0,
              text: messageText.trim(),
              item:
                kind === 'giveItem' && pickedItem
                  ? {
                      typeId: pickedItem.id!,
                      count: Number(count) || 1,
                      plus: Number(plus) || 0,
                      bless: Number(bless) || 0,
                      gem1: parseGem(gem1),
                      gem2: parseGem(gem2),
                    }
                  : null,
              amount: Number(amount) || 0,
              level: Number(level) || 1,
              mapId: mapId ?? 0,
              x: Number(x) || 0,
              y: Number(y) || 0,
            },
          }),
        )
      }
      const path = { id: characterId }
      switch (kind) {
        case 'kick':
          return unwrap(
            api.POST('/api/characters/{id}/kick', {
              params: { path },
              body: { reason: reason.trim() || null },
            }),
          )
        case 'mute':
          return unwrap(
            api.POST('/api/characters/{id}/mute', {
              params: { path },
              body: { minutes: Number(minutes) || 0 },
            }),
          )
        case 'jail':
          return unwrap(api.POST('/api/characters/{id}/jail', { params: { path } }))
        case 'message':
          return unwrap(
            api.POST('/api/characters/{id}/message', {
              params: { path },
              body: { text: messageText.trim() },
            }),
          )
        case 'giveItem':
          return unwrap(
            api.POST('/api/characters/{id}/give-item', {
              params: { path },
              body: {
                typeId: pickedItem!.id!,
                count: Number(count) || 1,
                plus: Number(plus) || 0,
                bless: Number(bless) || 0,
                gem1: parseGem(gem1),
                gem2: parseGem(gem2),
              },
            }),
          )
        case 'silver':
          return unwrap(
            api.POST('/api/characters/{id}/silver', {
              params: { path },
              body: { amount: Number(amount) || 0 },
            }),
          )
        case 'cp':
          return unwrap(
            api.POST('/api/characters/{id}/conquer-points', {
              params: { path },
              body: { amount: Number(amount) || 0 },
            }),
          )
        case 'level':
          return unwrap(
            api.POST('/api/characters/{id}/level', {
              params: { path },
              body: { level: Number(level) || 1 },
            }),
          )
        case 'teleport':
          return unwrap(
            api.POST('/api/characters/{id}/teleport', {
              params: { path },
              body: {
                mapId: mapId ?? currentMapId ?? 0,
                x: Number(x) || 0,
                y: Number(y) || 0,
              },
            }),
          )
        default:
          throw new Error(kind)
      }
    },
    onSuccess: (result, kind) => {
      setError(null)
      onChanged()
      if (result && 'failures' in result) {
        notifications.show({
          message: t('characters:actions.bulkResult', {
            succeeded: result.succeeded,
            failed: result.failures.length,
          }),
          color: result.failures.length === 0 ? 'green' : result.succeeded === 0 ? 'red' : 'yellow',
        })
        setFailures(result.failures)
        if (result.failures.length === 0) setModal(null)
        return
      }
      notifications.show({
        message: t(`characters:actions.${kind}Success` as never),
        color: 'green',
      })
      setModal(null)
    },
    onError: setError,
  })

  if (!canModerate && !canManage) return null

  return (
    <>
      <Group gap="xs" wrap="wrap">
        {canModerate && (
          <>
            <Button
              size="xs"
              variant="light"
              color="red"
              disabled={!bulk && !online}
              leftSection={<IconUserOff size={15} />}
              onClick={() => openModal('kick')}
            >
              {t('characters:actions.kick')}
            </Button>
            <Button
              size="xs"
              variant="light"
              color="orange"
              leftSection={<IconVolumeOff size={15} />}
              onClick={() => openModal('mute')}
            >
              {t(muted && !bulk ? 'characters:actions.unmute' : 'characters:actions.mute')}
            </Button>
            <Button
              size="xs"
              variant="light"
              color="orange"
              leftSection={<IconLock size={15} />}
              onClick={() => openModal('jail')}
            >
              {t('characters:actions.jail')}
            </Button>
            <Button
              size="xs"
              variant="light"
              disabled={!bulk && !online}
              leftSection={<IconMessage size={15} />}
              onClick={() => openModal('message')}
            >
              {t('characters:actions.message')}
            </Button>
          </>
        )}
        {canManage && (
          <>
            <Button
              size="xs"
              variant="light"
              color="teal"
              leftSection={<IconGift size={15} />}
              onClick={() => openModal('giveItem')}
            >
              {t('characters:actions.giveItem')}
            </Button>
            <Button
              size="xs"
              variant="light"
              color="yellow"
              leftSection={<IconCoin size={15} />}
              onClick={() => openModal('silver')}
            >
              {t('characters:actions.silver')}
            </Button>
            <Button
              size="xs"
              variant="light"
              color="cyan"
              leftSection={<IconDiamond size={15} />}
              onClick={() => openModal('cp')}
            >
              {t('characters:actions.cp')}
            </Button>
            <Button
              size="xs"
              variant="light"
              color="violet"
              leftSection={<IconArrowUpCircle size={15} />}
              onClick={() => openModal('level')}
            >
              {t('characters:actions.level')}
            </Button>
            <Button
              size="xs"
              variant="light"
              color="blue"
              leftSection={<IconMapPin size={15} />}
              onClick={() => openModal('teleport')}
            >
              {t('characters:actions.teleport')}
            </Button>
          </>
        )}
        {canManage && bulk && (
          <>
            <Button
              size="xs"
              variant="light"
              color="red"
              leftSection={<IconBan size={15} />}
              onClick={() => openModal('ban')}
            >
              {t('characters:actions.ban')}
            </Button>
            <Button
              size="xs"
              variant="light"
              color="green"
              leftSection={<IconCheck size={15} />}
              onClick={() => openModal('unban')}
            >
              {t('characters:actions.unban')}
            </Button>
          </>
        )}
      </Group>

      <Modal
        opened={modal !== null}
        onClose={() => setModal(null)}
        title={
          modal
            ? bulk
              ? t('characters:actions.bulkTitle', {
                  action: t(`characters:actions.${modal}` as never),
                  count: count_,
                })
              : t(`characters:actions.${modal}` as never)
            : ''
        }
        size={modal === 'giveItem' ? 'lg' : 'md'}
      >
        {modal && (
          <Stack>
            {Boolean(error) && (
              <Text c="red" size="sm">
                {formatError(error)}
              </Text>
            )}

            {failures.length > 0 && (
              <Stack gap={2}>
                <Text size="sm" fw={600}>
                  {t('characters:actions.bulkFailures', { count: failures.length })}
                </Text>
                <ScrollArea.Autosize mah={160}>
                  {failures.map((f) => (
                    <Text size="xs" key={f.accountId}>
                      {f.name ?? `#${f.accountId}`}: {formatError(new ApiError(400, f.error))}
                    </Text>
                  ))}
                </ScrollArea.Autosize>
              </Stack>
            )}

            {(modal === 'kick' || modal === 'ban' || modal === 'unban') && (
              <TextInput
                label={t('characters:actions.reason')}
                value={reason}
                onChange={(e) => setReason(e.currentTarget.value)}
              />
            )}

            {modal === 'mute' && (
              <NumberInput
                label={t('characters:actions.minutes')}
                min={0}
                max={43200}
                value={minutes}
                onChange={setMinutes}
              />
            )}

            {modal === 'jail' && (
              <Text size="sm">
                {bulk
                  ? t('characters:actions.jailConfirmBulk', { count: count_ })
                  : t('characters:actions.jailConfirm', { name: characterName })}
              </Text>
            )}

            {modal === 'message' && (
              <Textarea
                label={t('characters:actions.messageText')}
                minRows={3}
                value={messageText}
                onChange={(e) => setMessageText(e.currentTarget.value)}
              />
            )}

            {modal === 'giveItem' && (
              <Stack gap="sm">
                <ItemPicker
                  label={t('characters:actions.item')}
                  value={pickedItem?.id ?? null}
                  onChange={setPickedItem}
                />
                <SimpleGrid cols={3}>
                  <NumberInput
                    label={t('characters:actions.count')}
                    min={1}
                    max={40}
                    value={count}
                    onChange={setCount}
                  />
                  <NumberInput
                    label={t('characters:actions.plus')}
                    min={0}
                    max={12}
                    value={plus}
                    onChange={setPlus}
                  />
                  <NumberInput
                    label={t('characters:actions.bless')}
                    min={0}
                    max={7}
                    value={bless}
                    onChange={setBless}
                  />
                </SimpleGrid>
                <SimpleGrid cols={2}>
                  <Select
                    label={t('characters:actions.gem1')}
                    data={gemOptions}
                    value={gem1}
                    onChange={(v) => setGem1(v ?? 'default')}
                    searchable
                  />
                  <Select
                    label={t('characters:actions.gem2')}
                    data={gemOptions}
                    value={gem2}
                    onChange={(v) => setGem2(v ?? 'default')}
                    searchable
                  />
                </SimpleGrid>
              </Stack>
            )}

            {(modal === 'silver' || modal === 'cp') && (
              <NumberInput
                label={t('characters:actions.amount')}
                value={amount}
                onChange={setAmount}
              />
            )}

            {modal === 'level' && (
              <NumberInput
                label={t('characters:actions.newLevel')}
                min={1}
                max={140}
                value={level}
                onChange={setLevel}
              />
            )}

            {modal === 'teleport' && (
              <Stack gap="sm">
                <MapPicker
                  label={t('characters:actions.map')}
                  value={mapId}
                  onChange={(id) => setMapId(id)}
                  clearable={false}
                />
                <SimpleGrid cols={2}>
                  <NumberInput
                    label={t('characters:actions.x')}
                    min={0}
                    max={65535}
                    value={x}
                    onChange={setX}
                  />
                  <NumberInput
                    label={t('characters:actions.y')}
                    min={0}
                    max={65535}
                    value={y}
                    onChange={setY}
                  />
                </SimpleGrid>
              </Stack>
            )}

            <Group justify="flex-end">
              <Button variant="subtle" onClick={() => setModal(null)}>
                {t('characters:actions.cancel')}
              </Button>
              <Button
                disabled={
                  (modal === 'message' && !messageText.trim()) ||
                  (modal === 'giveItem' && !pickedItem) ||
                  ((modal === 'silver' || modal === 'cp') && Number(amount) === 0) ||
                  (modal === 'level' && level === '') ||
                  (bulk && count_ === 0) ||
                  (modal === 'teleport' && mapId == null)
                }
                loading={runMutation.isPending}
                color={modal === 'ban' ? 'red' : undefined}
                onClick={() => runMutation.mutate(modal)}
              >
                {t('characters:actions.confirm')}
              </Button>
            </Group>
          </Stack>
        )}
      </Modal>
    </>
  )
}
