import {
  Anchor,
  Badge,
  Button,
  Group,
  Loader,
  NumberInput,
  Pagination,
  Paper,
  ScrollArea,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { IconSearch } from '@tabler/icons-react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import { api, unwrap } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { formatProfession } from '../../lib/gameMeta'
import { MapPicker } from '../../components/pickers'
import { pkColor } from './characterMeta'

const pageSize = 25

export default function CharactersPage() {
  const { t, i18n } = useTranslation(['characters', 'items'])
  const navigate = useNavigate()

  const [q, setQ] = useState('')
  const [minLevel, setMinLevel] = useState<number | string>('')
  const [maxLevel, setMaxLevel] = useState<number | string>('')
  const [mapId, setMapId] = useState<number | null>(null)
  const [onlineOnly, setOnlineOnly] = useState(false)
  const [page, setPage] = useState(1)

  const [debouncedQ] = useDebouncedValue(q, 250)

  const query = {
    q: debouncedQ.trim() || undefined,
    minLevel: minLevel === '' ? undefined : Number(minLevel),
    maxLevel: maxLevel === '' ? undefined : Number(maxLevel),
    mapId: mapId ?? undefined,
    onlineOnly: onlineOnly || undefined,
    page,
    pageSize,
  }

  const characters = useQuery({
    queryKey: ['characters', query],
    queryFn: () => unwrap(api.GET('/api/characters', { params: { query } })),
    placeholderData: keepPreviousData,
  })

  const total = characters.data?.total ?? 0
  const hasFilters = Boolean(q || minLevel !== '' || maxLevel !== '' || mapId != null || onlineOnly)

  const formatClass = (cls: number) => formatProfession(cls, t)

  const clearFilters = () => {
    setQ('')
    setMinLevel('')
    setMaxLevel('')
    setMapId(null)
    setOnlineOnly(false)
    setPage(1)
  }

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={2}>{t('characters:title')}</Title>
        {characters.isFetching && <Loader size="sm" />}
      </Group>
      <Text c="dimmed">{t('characters:description')}</Text>

      <Group align="end" wrap="wrap">
        <TextInput
          style={{ flex: 2, minWidth: 220 }}
          placeholder={t('characters:searchPlaceholder')}
          leftSection={<IconSearch size={16} />}
          value={q}
          onChange={(e) => {
            setQ(e.currentTarget.value)
            setPage(1)
          }}
        />
        <NumberInput
          w={110}
          placeholder={t('characters:filter.minLevel')}
          min={1}
          max={140}
          value={minLevel}
          onChange={(v) => {
            setMinLevel(v)
            setPage(1)
          }}
        />
        <NumberInput
          w={110}
          placeholder={t('characters:filter.maxLevel')}
          min={1}
          max={140}
          value={maxLevel}
          onChange={(v) => {
            setMaxLevel(v)
            setPage(1)
          }}
        />
        <div style={{ minWidth: 200 }}>
          <MapPicker
            label=""
            placeholder={t('characters:filter.map')}
            value={mapId}
            onChange={(id) => {
              setMapId(id)
              setPage(1)
            }}
          />
        </div>
        <Switch
          label={t('characters:filter.onlineOnly')}
          checked={onlineOnly}
          onChange={(e) => {
            setOnlineOnly(e.currentTarget.checked)
            setPage(1)
          }}
        />
        {hasFilters && (
          <Button variant="subtle" onClick={clearFilters}>
            {t('characters:filter.clear')}
          </Button>
        )}
      </Group>

      <ErrorAlert error={characters.error} />

      <Paper withBorder>
        <ScrollArea>
          <Table highlightOnHover striped miw={860}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('characters:column.name')}</Table.Th>
                <Table.Th>{t('characters:column.account')}</Table.Th>
                <Table.Th>{t('characters:column.level')}</Table.Th>
                <Table.Th>{t('characters:column.class')}</Table.Th>
                <Table.Th>{t('characters:column.location')}</Table.Th>
                <Table.Th>{t('characters:column.silver')}</Table.Th>
                <Table.Th>{t('characters:column.cp')}</Table.Th>
                <Table.Th>{t('characters:column.pk')}</Table.Th>
                <Table.Th>{t('characters:column.status')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {characters.data?.items.map((ch) => (
                <Table.Tr
                  key={ch.characterId}
                  style={{ cursor: 'pointer' }}
                  onClick={() => navigate(`/admin/characters/${ch.characterId}`)}
                >
                  <Table.Td>
                    <Text size="sm" fw={600}>
                      {ch.name}
                    </Text>
                    <Text size="xs" c="dimmed">
                      #{ch.characterId}
                    </Text>
                  </Table.Td>
                  <Table.Td onClick={(e) => e.stopPropagation()}>
                    <Anchor component={Link} to={`/admin/accounts/${ch.accountId}`} size="sm">
                      {ch.accountUsername || `#${ch.accountId}`}
                    </Anchor>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm">
                      {ch.level}
                      {ch.rebirths > 0 ? ` (RB ${ch.rebirths})` : ''}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm">{formatClass(ch.currentClass)}</Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm">
                      {ch.mapName ? `${ch.mapName} ` : `#${ch.mapId} `}
                      <Text span size="xs" c="dimmed">
                        ({ch.x}, {ch.y})
                      </Text>
                    </Text>
                  </Table.Td>
                  <Table.Td>{ch.silver.toLocaleString(i18n.language)}</Table.Td>
                  <Table.Td>{ch.conquerPoints.toLocaleString(i18n.language)}</Table.Td>
                  <Table.Td>
                    <Badge size="sm" variant="light" color={pkColor(ch.killPoints)}>
                      {ch.killPoints}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    <Badge size="sm" variant="light" color={ch.online ? 'green' : 'gray'}>
                      {t(ch.online ? 'characters:status.online' : 'characters:status.offline')}
                    </Badge>
                  </Table.Td>
                </Table.Tr>
              ))}
              {characters.data && characters.data.items.length === 0 && (
                <Table.Tr>
                  <Table.Td colSpan={9}>
                    <Text c="dimmed" ta="center" py="md">
                      {t('characters:empty')}
                    </Text>
                  </Table.Td>
                </Table.Tr>
              )}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      </Paper>

      <Group justify="space-between">
        <Text size="sm" c="dimmed">
          {t('characters:total', { count: total })}
        </Text>
        <Pagination
          total={Math.max(1, Math.ceil(total / pageSize))}
          value={page}
          onChange={setPage}
        />
      </Group>
    </Stack>
  )
}
