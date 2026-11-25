import {
  Alert,
  Badge,
  Group,
  Loader,
  ScrollArea,
  Stack,
  Table,
  Text,
  TextInput,
} from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { IconAlertCircle } from '@tabler/icons-react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap } from '../../api/client'
import { useCan } from '../../app/auth'
import { ErrorAlert } from '../../app/ErrorAlert'
import { CharacterCell, DurationText, PkStatusBadges, ReleaseButton, useNumber, useTime } from './shared'

export function JailTab() {
  const { t } = useTranslation('pk')
  const number = useNumber()
  const time = useTime()
  const canModerate = useCan('Moderate')
  const [search, setSearch] = useState('')
  const [debounced] = useDebouncedValue(search, 250)

  const jail = useQuery({
    queryKey: ['pk', 'jail', debounced],
    queryFn: () =>
      unwrap(
        api.GET('/api/pk/jail', {
          params: { query: { search: debounced.trim() || undefined } },
        }),
      ),
    placeholderData: keepPreviousData,
  })

  const inmates = jail.data?.inmates ?? []
  const connected = jail.data?.gameConnected ?? false

  return (
    <Stack>
      <Group justify="space-between" wrap="wrap">
        <TextInput
          placeholder={t('search')}
          value={search}
          onChange={(e) => setSearch(e.currentTarget.value)}
          maw={320}
          style={{ flex: 1 }}
        />
      </Group>

      {jail.data && !connected && (
        <Alert color="yellow" icon={<IconAlertCircle size={16} />}>
          {t('gameOfflineNote')}
        </Alert>
      )}

      <ErrorAlert error={jail.error} />

      {jail.isPending ? (
        <Loader />
      ) : inmates.length === 0 ? (
        <Text c="dimmed">{t('jail.empty')}</Text>
      ) : (
        <ScrollArea>
          <Table striped highlightOnHover miw={760}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('jail.character')}</Table.Th>
                <Table.Th>{t('jail.state')}</Table.Th>
                <Table.Th>{t('jail.pkPoints')}</Table.Th>
                <Table.Th>{t('jail.since')}</Table.Th>
                <Table.Th>{t('jail.bail')}</Table.Th>
                <Table.Th>{t('jail.position')}</Table.Th>
                {canModerate && <Table.Th>{t('jail.actions')}</Table.Th>}
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {inmates.map((row) => (
                <Table.Tr key={row.character.id}>
                  <Table.Td>
                    <CharacterCell character={row.character} />
                  </Table.Td>
                  <Table.Td>
                    <Badge size="sm" variant="light" color={row.online ? 'green' : 'gray'}>
                      {t(row.online ? 'status.online' : 'status.offline')}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    <Group gap="xs" wrap="nowrap">
                      <Text size="sm" fw={600}>
                        {number(row.pkPoints)}
                      </Text>
                      <PkStatusBadges
                        pkPoints={row.pkPoints}
                        flashing={row.flashing}
                        nameColor={row.nameColor}
                      />
                    </Group>
                  </Table.Td>
                  <Table.Td>
                    <Stack gap={2}>
                      <Text size="sm">{time(row.jailedSince)}</Text>
                      <Group gap={6} wrap="nowrap">
                        <DurationText since={row.jailedSince} />
                        {row.jailedReason && (
                          <Badge size="xs" variant="outline" color="gray">
                            {t(`jail.reasons.${row.jailedReason}`, { defaultValue: row.jailedReason })}
                          </Badge>
                        )}
                      </Group>
                    </Stack>
                  </Table.Td>
                  <Table.Td>
                    {row.bailRequired > 0 ? (
                      <Text size="sm">{t('jail.silver', { amount: number(row.bailRequired) })}</Text>
                    ) : (
                      <Badge size="sm" variant="light" color="teal">
                        {t('jail.free')}
                      </Badge>
                    )}
                  </Table.Td>
                  <Table.Td>
                    <Text size="xs" c="dimmed">
                      {row.x}, {row.y}
                    </Text>
                  </Table.Td>
                  {canModerate && (
                    <Table.Td>
                      <ReleaseButton character={row.character} disabled={!connected} />
                    </Table.Td>
                  )}
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      )}
    </Stack>
  )
}
