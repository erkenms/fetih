import { Badge, Loader, SegmentedControl, Stack, Table, Text } from '@mantine/core'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap } from '../../api/client'
import { useCan } from '../../app/auth'
import { ErrorAlert } from '../../app/ErrorAlert'
import { boardChannels } from './names'
import { CharacterCell, ConfirmAction, DataTable, useTime } from './shared'

const channelKeys: Record<number, string> = { 2201: 'trade', 2202: 'friend', 2203: 'team', 2204: 'guild', 2205: 'others' }

export function BoardTab() {
  const { t } = useTranslation('social')
  const time = useTime()
  const canModerate = useCan('Moderate')
  const [channel, setChannel] = useState('all')
  const board = useQuery({
    queryKey: ['social', 'board', channel],
    queryFn: () =>
      unwrap(api.GET('/api/social/board', { params: { query: { channel: channel === 'all' ? undefined : Number(channel) } } })),
    placeholderData: keepPreviousData,
  })
  return (
    <Stack>
      <SegmentedControl
        value={channel}
        onChange={setChannel}
        data={[{ value: 'all', label: t('board.all') }, ...boardChannels.map((c) => ({ value: String(c), label: t(`board.${channelKeys[c]}`) }))]}
        w="fit-content"
      />
      <ErrorAlert error={board.error} />
      {board.isPending ? (
        <Loader />
      ) : (
        <DataTable
          head={[t('board.posted'), t('board.channel'), t('board.author'), t('board.message'), '']}
          empty={t('board.none')}
          rows={(board.data ?? []).map((p) => (
            <Table.Tr key={`${p.channelId}-${p.characterId}`}>
              <Table.Td>
                <Text size="sm">{time(p.postedAt)}</Text>
              </Table.Td>
              <Table.Td>
                <Badge variant="light">{t(`board.${p.channel}`, { defaultValue: p.channel })}</Badge>
                {p.guild && (
                  <Text size="xs" c="dimmed">
                    {p.guild.name}
                  </Text>
                )}
              </Table.Td>
              <Table.Td>{p.author ? <CharacterCell character={p.author} /> : p.authorName}</Table.Td>
              <Table.Td>
                <Text size="sm">{p.message}</Text>
              </Table.Td>
              <Table.Td>
                {canModerate && (
                  <ConfirmAction
                    label={t('board.delete')}
                    question={t('board.deleteQuestion', { name: p.authorName })}
                    done={t('board.deleted')}
                    action={() =>
                      unwrap(
                        api.DELETE('/api/social/board/{channel}/{characterId}', {
                          params: { path: { channel: p.channelId, characterId: p.characterId } },
                        }),
                      )
                    }
                    invalidate={[['social', 'board']]}
                  />
                )}
              </Table.Td>
            </Table.Tr>
          ))}
        />
      )}
    </Stack>
  )
}
