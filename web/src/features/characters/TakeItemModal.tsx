import { Button, Group, Modal, Stack, Text } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap } from '../../api/client'
import { useErrorText } from '../../app/ErrorAlert'
import { toCatalogItem, type CharacterItemEntry } from './characterMeta'

interface TakeItemModalProps {
  characterId: number
  characterName: string
  item: CharacterItemEntry | null
  onClose: () => void
  onTaken: () => void
}

export function TakeItemModal({ characterId, characterName, item, onClose, onTaken }: TakeItemModalProps) {
  const { t } = useTranslation('characters')
  const formatError = useErrorText('characters')
  const [error, setError] = useState<unknown>(null)

  const takeMutation = useMutation({
    mutationFn: (itemId: number) =>
      unwrap(
        api.POST('/api/characters/{id}/take-item', {
          params: { path: { id: characterId } },
          body: { itemId },
        }),
      ),
    onSuccess: () => {
      notifications.show({ message: t('detail.items.takeSuccess'), color: 'green' })
      setError(null)
      onTaken()
    },
    onError: setError,
  })

  const close = () => {
    setError(null)
    onClose()
  }

  return (
    <Modal opened={Boolean(item)} onClose={close} title={t('detail.items.takeConfirmTitle')}>
      {item && (
        <Stack>
          {Boolean(error) && (
            <Text c="red" size="sm">
              {formatError(error)}
            </Text>
          )}
          <Text size="sm">
            {t('detail.items.takeConfirmBody', {
              name: toCatalogItem(item).name,
              id: item.itemId,
              character: characterName,
            })}
          </Text>
          <Group justify="flex-end">
            <Button variant="subtle" onClick={close}>
              {t('actions.cancel')}
            </Button>
            <Button color="red" loading={takeMutation.isPending} onClick={() => takeMutation.mutate(item.itemId)}>
              {t('actions.confirm')}
            </Button>
          </Group>
        </Stack>
      )}
    </Modal>
  )
}
