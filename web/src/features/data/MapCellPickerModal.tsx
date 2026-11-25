import { Alert, Badge, Box, Button, Group, Modal, NumberInput, Stack, Text } from '@mantine/core'
import { IconCheck, IconMapPin, IconX } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useEffect, useState, type MouseEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap } from '../../api/client'
import { MapPicker } from '../../components/pickers'
import { cellToNormalized, normalizedToCell } from './dataUtils'

export interface MapCellPickerModalProps {
  opened: boolean
  onClose: () => void
  initialMap: number
  initialX: number
  initialY: number
  onApply: (map: number, x: number, y: number) => void
}

export function MapCellPickerModal({
  opened,
  onClose,
  initialMap,
  initialX,
  initialY,
  onApply,
}: MapCellPickerModalProps) {
  const { t } = useTranslation('data')
  const [mapId, setMapId] = useState<number>(initialMap)
  const [x, setX] = useState<number>(initialX)
  const [y, setY] = useState<number>(initialY)
  const [imageBroken, setImageBroken] = useState(false)

  useEffect(() => {
    if (opened) {
      setMapId(initialMap)
      setX(initialX)
      setY(initialY)
      setImageBroken(false)
    }
  }, [opened, initialMap, initialX, initialY])

  const mapInfo = useQuery({
    queryKey: ['data', 'mapInfo', mapId],
    queryFn: () => unwrap(api.GET('/api/data/maps/{mapId}/info', { params: { path: { mapId } } })),
    enabled: opened && mapId > 0,
  })

  const cellCheck = useQuery({
    queryKey: ['data', 'cellCheck', mapId, x, y],
    queryFn: () =>
      unwrap(
        api.GET('/api/data/maps/{mapId}/cell', {
          params: { path: { mapId }, query: { x, y } },
        }),
      ),
    enabled: opened && mapId > 0,
  })

  const width = mapInfo.data?.width ?? 1000
  const height = mapInfo.data?.height ?? 1000
  const previewSrc = !imageBroken ? (mapInfo.data?.background ?? mapInfo.data?.minimap ?? null) : null
  const marker = cellToNormalized(x, y, width, height)

  function handleCanvasClick(e: MouseEvent<HTMLDivElement>) {
    const rect = e.currentTarget.getBoundingClientRect()
    if (rect.width <= 0 || rect.height <= 0) return
    const normX = (e.clientX - rect.left) / rect.width
    const normY = (e.clientY - rect.top) / rect.height
    const cell = normalizedToCell(normX, normY, width, height)
    setX(cell.x)
    setY(cell.y)
  }

  return (
    <Modal opened={opened} onClose={onClose} title={t('mapPickerModal.title')} size="lg">
      <Stack gap="sm">
        <MapPicker
          value={mapId || null}
          onChange={(id) => {
            if (id != null) {
              setMapId(id)
              setImageBroken(false)
            }
          }}
          clearable={false}
        />

        {mapInfo.data?.width && mapInfo.data?.height ? (
          <Text size="xs" c="dimmed">
            {t('mapPickerModal.mapSize', {
              id: mapId,
              width: mapInfo.data.width,
              height: mapInfo.data.height,
            })}{' '}
            — {t('mapPickerModal.clickHint')}
          </Text>
        ) : (
          <Alert color="gray" p="xs">
            <Text size="xs">{t('mapPickerModal.noGrid')}</Text>
          </Alert>
        )}

        <Box
          onClick={handleCanvasClick}
          style={{
            position: 'relative',
            width: '100%',
            aspectRatio: `${Math.max(1, width)} / ${Math.max(1, height)}`,
            maxHeight: 380,
            borderRadius: 8,
            overflow: 'hidden',
            cursor: 'crosshair',
            border: '1px solid var(--mantine-color-default-border)',
            background:
              previewSrc == null
                ? 'repeating-linear-gradient(0deg, transparent, transparent 23px, var(--mantine-color-default-border) 24px), repeating-linear-gradient(90deg, transparent, transparent 23px, var(--mantine-color-default-border) 24px)'
                : undefined,
          }}
        >
          {previewSrc ? (
            <img
              src={previewSrc}
              alt={mapInfo.data?.name ?? `#${mapId}`}
              onError={() => setImageBroken(true)}
              style={{ width: '100%', height: '100%', objectFit: 'fill', display: 'block', userSelect: 'none' }}
              draggable={false}
            />
          ) : (
            <Text
              size="xs"
              c="dimmed"
              ta="center"
              style={{ position: 'absolute', bottom: 8, left: 8, right: 8, pointerEvents: 'none' }}
            >
              {t('mapPickerModal.noImage')}
            </Text>
          )}

          <Box
            style={{
              position: 'absolute',
              left: `${marker.normX * 100}%`,
              top: `${marker.normY * 100}%`,
              transform: 'translate(-50%, -100%)',
              pointerEvents: 'none',
              color: 'var(--mantine-color-red-6)',
              filter: 'drop-shadow(0 1px 2px rgba(0,0,0,0.65))',
            }}
          >
            <IconMapPin size={24} />
          </Box>
        </Box>

        <Group grow align="flex-end">
          <NumberInput
            label={t('teleporters.fields.destX')}
            value={x}
            min={0}
            max={65535}
            allowDecimal={false}
            onChange={(v) => setX(Number(v) || 0)}
          />
          <NumberInput
            label={t('teleporters.fields.destY')}
            value={y}
            min={0}
            max={65535}
            allowDecimal={false}
            onChange={(v) => setY(Number(v) || 0)}
          />
        </Group>

        <Group justify="space-between" align="center">
          <Group gap="xs">
            <Text size="sm" fw={500}>
              {t('mapPickerModal.selected', { x, y })}
            </Text>
            {cellCheck.data?.hasGrid && (
              <Badge
                color={!cellCheck.data.inBounds ? 'red' : cellCheck.data.walkable ? 'green' : 'yellow'}
                leftSection={cellCheck.data.walkable ? <IconCheck size={12} /> : <IconX size={12} />}
              >
                {!cellCheck.data.inBounds
                  ? t('mapPickerModal.outOfBounds')
                  : cellCheck.data.walkable
                    ? t('mapPickerModal.walkable')
                    : t('mapPickerModal.blocked')}
              </Badge>
            )}
          </Group>

          <Group gap="xs">
            <Button variant="default" onClick={onClose}>
              {t('actions.cancel')}
            </Button>
            <Button
              onClick={() => {
                onApply(mapId, x, y)
                onClose()
              }}
            >
              {t('actions.apply')}
            </Button>
          </Group>
        </Group>
      </Stack>
    </Modal>
  )
}
