import {
  Button,
  Group,
  Modal,
  NumberInput,
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
import { ApiError, api, unwrap } from '../../api/client'
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
  | null

interface CharacterActionsBarProps {
  characterId: number
  characterName: string
  currentLevel: number
  currentMapId: number
  currentX: number
  currentY: number
  online: boolean
  muted: boolean
  canModerate: boolean
  canManage: boolean
  onChanged: () => void
}

export function CharacterActionsBar({
  characterId,
  characterName,
  currentLevel,
  currentMapId,
  currentX,
  currentY,
  online,
  muted,
  canModerate,
  canManage,
  onChanged,
}: CharacterActionsBarProps) {
  const { t } = useTranslation(['characters', 'items'])
  const commonError = useErrorText()

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
  const [mapId, setMapId] = useState<number | null>(currentMapId)
  const [x, setX] = useState<number | string>(currentX)
  const [y, setY] = useState<number | string>(currentY)

  const formatError = (err: unknown) => {
    if (err instanceof ApiError) {
      const translated = t(`characters:errors.${err.code}` as never, { defaultValue: '' })
      if (translated) return translated
    }
    return commonError(err)
  }

  const openModal = (kind: ActionKind) => {
    setError(null)
    setReason('')
    setMessageText('')
    if (kind === 'mute') setMinutes(muted ? 0 : 30)
    if (kind === 'level') setLevel(currentLevel)
    if (kind === 'teleport') {
      setMapId(currentMapId)
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
                mapId: mapId ?? currentMapId,
                x: Number(x) || 0,
                y: Number(y) || 0,
              },
            }),
          )
      }
    },
    onSuccess: (_, kind) => {
      notifications.show({
        message: t(`characters:actions.${kind}Success` as never),
        color: 'green',
      })
      setModal(null)
      setError(null)
      onChanged()
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
              disabled={!online}
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
              {t(muted ? 'characters:actions.unmute' : 'characters:actions.mute')}
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
              disabled={!online}
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
      </Group>

      <Modal
        opened={modal !== null}
        onClose={() => setModal(null)}
        title={modal ? t(`characters:actions.${modal}` as never) : ''}
        size={modal === 'giveItem' ? 'lg' : 'md'}
      >
        {modal && (
          <Stack>
            {Boolean(error) && (
              <Text c="red" size="sm">
                {formatError(error)}
              </Text>
            )}

            {modal === 'kick' && (
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
                {t('characters:actions.jailConfirm', { name: characterName })}
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
                  (modal === 'teleport' && mapId == null)
                }
                loading={runMutation.isPending}
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
