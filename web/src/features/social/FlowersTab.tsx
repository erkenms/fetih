import { Loader, SegmentedControl, Stack, Table } from '@mantine/core'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap, type Schemas } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { CharacterCell, DataTable, useNumber } from './shared'

type Kind = Schemas['FlowerKind']
const kinds: Kind[] = ['RedRose', 'Lily', 'Orchid', 'Tulip']

export function FlowersTab() {
  const { t } = useTranslation('social')
  const number = useNumber()
  const [kind, setKind] = useState<Kind>('RedRose')
  const flowers = useQuery({
    queryKey: ['social', 'flowers', kind],
    queryFn: () => unwrap(api.GET('/api/social/flowers', { params: { query: { kind } } })),
    placeholderData: keepPreviousData,
  })
  return (
    <Stack>
      <SegmentedControl
        value={kind}
        onChange={(v) => setKind(v as Kind)}
        data={kinds.map((k) => ({ value: k, label: t(`flowers.${k}`) }))}
        w="fit-content"
      />
      <ErrorAlert error={flowers.error} />
      {flowers.isPending ? (
        <Loader />
      ) : (
        <DataTable
          head={['#', t('member.character'), t('flowers.total'), t('flowers.today')]}
          empty={t('none')}
          rows={(flowers.data ?? []).map((e) => (
            <Table.Tr key={e.character.id}>
              <Table.Td>{e.position}</Table.Td>
              <Table.Td>
                <CharacterCell character={e.character} />
              </Table.Td>
              <Table.Td>{number(e.amount)}</Table.Td>
              <Table.Td>{number(e.today)}</Table.Td>
            </Table.Tr>
          ))}
        />
      )}
    </Stack>
  )
}
