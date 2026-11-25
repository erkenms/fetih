import {
  Alert,
  Anchor,
  Badge,
  Group,
  Loader,
  ScrollArea,
  SegmentedControl,
  Stack,
  Switch,
  Table,
  Text,
  TextInput,
} from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { IconAlertCircle } from '@tabler/icons-react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { api, unwrap } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { CharacterCell, PkStatusBadges, useNumber } from './shared'

export function ActiveTab() {
  const { t } = useTranslation('pk')
  const number = useNumber()
  const [color, setColor] = useState('all')
  const [search, setSearch] = useState('')
  const [debounced] = useDebouncedValue(search, 250)
  const [autoRefresh, setAutoRefresh] = useState(true)

  const active = useQuery({
    queryKey: ['pk', 'active', color, debounced],
    queryFn: () =>
      unwrap(
        api.GET('/api/pk/active', {
          params: {
            query: {
              color: color === 'all' ? undefined : color,
              search: debounced.trim() || undefined,
            },
          },
        }),
      ),
    placeholderData: keepPreviousData,
    refetchInterval: autoRefresh ? 10_000 : false,
  })

  const players = active.data?.players ?? []
  const connected = active.data?.gameConnected ?? false

  return (
    <Stack>
      <Group justify="space-between" wrap="wrap" gap="sm">
        <Group wrap="wrap" gap="sm">
          <SegmentedControl
            value={color}
            onChange={setColor}
            data={[
              { value: 'all', label: t('active.all') },
              { value: 'blue', label: t('active.flashing') },
              { value: 'red', label: t('active.red') },
              { value: 'black', label: t('active.black') },
            ]}
          />
          <TextInput
            placeholder={t('search')}
            value={search}
            onChange={(e) => setSearch(e.currentTarget.value)}
            w={260}
          />
        </Group>
        <Switch
          label={t('active.autoRefresh')}
          checked={autoRefresh}
          onChange={(e) => setAutoRefresh(e.currentTarget.checked)}
        />
      </Group>

      <ErrorAlert error={active.error} />

      {active.isPending ? (
        <Loader />
      ) : !connected ? (
        <Alert color="yellow" icon={<IconAlertCircle size={16} />}>
          {t('active.offlineEmpty')}
        </Alert>
      ) : players.length === 0 ? (
        <Text c="dimmed">{t('active.empty')}</Text>
      ) : (
        <ScrollArea>
          <Table striped highlightOnHover miw={720}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('active.character')}</Table.Th>
                <Table.Th>{t('active.status')}</Table.Th>
                <Table.Th>{t('active.pkPoints')}</Table.Th>
                <Table.Th>{t('active.pkMode')}</Table.Th>
                <Table.Th>{t('active.location')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {players.map((p) => (
                <Table.Tr key={p.character.id}>
                  <Table.Td>
                    <CharacterCell character={p.character} />
                  </Table.Td>
                  <Table.Td>
                    <Group gap={4} wrap="nowrap">
                      <PkStatusBadges
                        pkPoints={p.pkPoints}
                        flashing={p.flashing}
                        nameColor={p.nameColor}
                      />
                      {p.inJail && (
                        <Badge size="sm" variant="outline" color="orange">
                          {t('status.inJail')}
                        </Badge>
                      )}
                      {p.dead && (
                        <Badge size="sm" variant="outline" color="gray">
                          {t('status.dead')}
                        </Badge>
                      )}
                    </Group>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" fw={600}>
                      {number(p.pkPoints)}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Badge size="sm" variant="default">
                      {p.pkMode}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    <Group gap={6} wrap="nowrap">
                      <Anchor component={Link} to={`/admin/maps/${p.mapId}`} size="sm">
                        {p.mapName || `#${p.mapId}`}
                      </Anchor>
                      <Text size="xs" c="dimmed">
                        ({p.x}, {p.y})
                      </Text>
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      )}
    </Stack>
  )
}
