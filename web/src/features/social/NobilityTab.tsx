import { Badge, Loader, Stack, Table } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { api, unwrap } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { nobilityColors } from '../../lib/gameMeta'
import { CharacterCell, DataTable, useNumber } from './shared'

export function NobilityTab() {
  const { t } = useTranslation('social')
  const number = useNumber()
  const board = useQuery({ queryKey: ['social', 'nobility'], queryFn: () => unwrap(api.GET('/api/social/nobility')) })
  if (board.isPending) return <Loader />
  return (
    <Stack>
      <ErrorAlert error={board.error} />
      <DataTable
        head={['#', t('member.character'), t('nobility.rank'), t('nobility.donation')]}
        empty={t('none')}
        rows={(board.data ?? []).map((e) => (
          <Table.Tr key={e.character.id}>
            <Table.Td>{e.position}</Table.Td>
            <Table.Td>
              <CharacterCell character={e.character} />
            </Table.Td>
            <Table.Td>
              <Badge color={nobilityColors[e.rank] ?? 'gray'} variant="light">
                {t(`nobility.ranks.${e.rank}`, { defaultValue: e.rank })}
              </Badge>
            </Table.Td>
            <Table.Td>{number(e.donation)}</Table.Td>
          </Table.Tr>
        ))}
      />
    </Stack>
  )
}
