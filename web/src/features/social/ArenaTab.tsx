import { Group, Loader, Select, Stack, Table, Text } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { classKey } from './names'
import { CharacterCell, DataTable, useNumber } from './shared'

export function ArenaTab() {
  const { t } = useTranslation('social')
  const number = useNumber()
  const [season, setSeason] = useState<string | null>(null)
  const arena = useQuery({ queryKey: ['social', 'arena'], queryFn: () => unwrap(api.GET('/api/social/arena')) })
  const past = useQuery({
    queryKey: ['social', 'arena', season],
    queryFn: () => unwrap(api.GET('/api/social/arena/seasons/{season}', { params: { path: { season: Number(season) } } })),
    enabled: season != null,
  })

  if (arena.isPending) return <Loader />
  return (
    <Stack>
      <ErrorAlert error={arena.error ?? past.error} />
      <Group>
        <Select
          label={t('arena.season')}
          data={[{ value: '', label: t('arena.current') }, ...(arena.data?.seasons ?? []).map((s) => ({ value: String(s), label: t('arena.seasonNumber', { season: s }) }))]}
          value={season ?? ''}
          onChange={(v) => setSeason(v ? v : null)}
          allowDeselect={false}
          maw={240}
        />
      </Group>
      {season == null ? (
        <DataTable
          head={['#', t('member.character'), t('arena.points'), t('arena.today'), t('arena.total'), t('arena.honor')]}
          empty={t('none')}
          rows={(arena.data?.current ?? []).map((e) => (
            <Table.Tr key={e.character.id}>
              <Table.Td>{e.position}</Table.Td>
              <Table.Td>
                <CharacterCell character={e.character} />
              </Table.Td>
              <Table.Td>{number(e.points)}</Table.Td>
              <Table.Td>{t('arena.record', { wins: e.winsToday, losses: e.lossesToday })}</Table.Td>
              <Table.Td>{t('arena.record', { wins: e.winsTotal, losses: e.lossesTotal })}</Table.Td>
              <Table.Td>{t('arena.honorText', { points: number(e.honorPoints), total: number(e.honorTotal) })}</Table.Td>
            </Table.Tr>
          ))}
        />
      ) : past.isPending ? (
        <Loader />
      ) : (
        <DataTable
          head={['#', t('member.character'), t('arena.points'), t('arena.total')]}
          empty={t('none')}
          rows={(past.data ?? []).map((e) => {
            const cls = classKey(e.class)
            return (
              <Table.Tr key={e.characterId}>
                <Table.Td>{e.rank}</Table.Td>
                <Table.Td>
                  <Text size="sm" fw={500} span>
                    {e.name}
                  </Text>{' '}
                  <Text size="xs" c="dimmed" span>
                    {t('levelClass', { level: e.level, class: cls ? t(`class.${cls}`) : '' })}
                  </Text>
                </Table.Td>
                <Table.Td>{number(e.points)}</Table.Td>
                <Table.Td>{t('arena.record', { wins: e.wins, losses: e.losses })}</Table.Td>
              </Table.Tr>
            )
          })}
        />
      )}
    </Stack>
  )
}
