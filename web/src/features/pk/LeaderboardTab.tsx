import {
  Anchor,
  Badge,
  Group,
  Loader,
  ScrollArea,
  SegmentedControl,
  Stack,
  Table,
  Text,
  TextInput,
} from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { api, unwrap } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { CharacterCell, PkStatusBadges, useNumber } from './shared'

export function LeaderboardTab() {
  const { t } = useTranslation('pk')
  const number = useNumber()
  const [color, setColor] = useState('all')
  const [search, setSearch] = useState('')
  const [debounced] = useDebouncedValue(search, 250)

  const board = useQuery({
    queryKey: ['pk', 'leaderboard', color, debounced],
    queryFn: () =>
      unwrap(
        api.GET('/api/pk/leaderboard', {
          params: {
            query: {
              color: color === 'all' ? undefined : color,
              search: debounced.trim() || undefined,
            },
          },
        }),
      ),
    placeholderData: keepPreviousData,
  })

  const entries = board.data?.entries ?? []

  return (
    <Stack>
      <Group wrap="wrap" gap="sm">
        <SegmentedControl
          value={color}
          onChange={setColor}
          data={[
            { value: 'all', label: t('leaderboard.all') },
            { value: 'red', label: t('leaderboard.red') },
            { value: 'black', label: t('leaderboard.black') },
          ]}
        />
        <TextInput
          placeholder={t('search')}
          value={search}
          onChange={(e) => setSearch(e.currentTarget.value)}
          w={260}
        />
      </Group>

      <ErrorAlert error={board.error} />

      {board.isPending ? (
        <Loader />
      ) : entries.length === 0 ? (
        <Text c="dimmed">{t('leaderboard.empty')}</Text>
      ) : (
        <ScrollArea>
          <Table striped highlightOnHover miw={720}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th w={56}>{t('leaderboard.rank')}</Table.Th>
                <Table.Th>{t('leaderboard.character')}</Table.Th>
                <Table.Th>{t('leaderboard.pkPoints')}</Table.Th>
                <Table.Th>{t('leaderboard.status')}</Table.Th>
                <Table.Th>{t('leaderboard.location')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {entries.map((entry) => (
                <Table.Tr key={entry.character.id}>
                  <Table.Td>
                    <Text size="sm" fw={600} c="dimmed">
                      {entry.position}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Group gap="xs" wrap="nowrap">
                      <CharacterCell character={entry.character} />
                      {entry.rebirths > 0 && (
                        <Badge size="xs" variant="outline">
                          {t('leaderboard.rebirths', { count: entry.rebirths })}
                        </Badge>
                      )}
                    </Group>
                  </Table.Td>
                  <Table.Td>
                    <Group gap="xs" wrap="nowrap">
                      <Text size="sm" fw={600}>
                        {number(entry.pkPoints)}
                      </Text>
                      <PkStatusBadges
                        pkPoints={entry.pkPoints}
                        flashing={entry.flashing}
                        nameColor={entry.nameColor}
                      />
                    </Group>
                  </Table.Td>
                  <Table.Td>
                    <Badge size="sm" variant="light" color={entry.online ? 'green' : 'gray'}>
                      {t(entry.online ? 'status.online' : 'status.offline')}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    <Group gap={6} wrap="nowrap">
                      <Anchor component={Link} to={`/admin/maps/${entry.mapId}`} size="sm">
                        {entry.mapName || `#${entry.mapId}`}
                      </Anchor>
                      {entry.inJail && (
                        <Badge size="xs" variant="light" color="orange">
                          {t('status.inJail')}
                        </Badge>
                      )}
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
