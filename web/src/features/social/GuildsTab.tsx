import { Badge, Drawer, Group, Loader, SimpleGrid, Stack, Table, Text, Title } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router'
import { api, unwrap } from '../../api/client'
import { useCan } from '../../app/auth'
import { ErrorAlert } from '../../app/ErrorAlert'
import { guildRankKey } from './names'
import { CharacterCell, ConfirmAction, DataTable, SearchBox, useDay, useNumber } from './shared'

export function GuildsTab() {
  const { t } = useTranslation('social')
  const number = useNumber()
  const day = useDay()
  const [params, setParams] = useSearchParams()
  const [search, setSearch] = useState('')
  const [debounced] = useDebouncedValue(search.trim(), 300)
  const guilds = useQuery({
    queryKey: ['social', 'guilds', debounced],
    queryFn: () => unwrap(api.GET('/api/social/guilds', { params: { query: { search: debounced || undefined } } })),
    placeholderData: keepPreviousData,
  })
  const war = useQuery({ queryKey: ['social', 'guildwar'], queryFn: () => unwrap(api.GET('/api/social/guildwar')) })
  const holder = war.data?.liveHolder?.id ?? war.data?.saved.holder?.id
  const winner = war.data?.liveWinner?.id ?? war.data?.saved.winner?.id
  const selected = Number(params.get('guild')) || null
  const open = (id: number | null) =>
    setParams((p) => {
      if (id) p.set('guild', String(id))
      else p.delete('guild')
      return p
    })

  return (
    <Stack>
      <Group justify="space-between">
        <SearchBox value={search} onChange={setSearch} />
        {guilds.isFetching && <Loader size="sm" />}
      </Group>
      <ErrorAlert error={guilds.error} />
      <DataTable
        head={[t('guild.name'), t('guild.leader'), t('guild.level'), t('guild.members'), t('guild.fund'), t('guild.cpFund'), t('guild.created')]}
        empty={t('none')}
        rows={(guilds.data ?? []).map((g) => (
          <Table.Tr key={g.id} onClick={() => open(g.id)} style={{ cursor: 'pointer' }}>
            <Table.Td>
              <Group gap={6}>
                <Text size="sm" fw={500}>
                  {g.name}
                </Text>
                {g.id === winner && <Badge size="xs" color="yellow">{t('guild.ruling')}</Badge>}
                {g.id === holder && g.id !== winner && <Badge size="xs" color="orange">{t('guild.holding')}</Badge>}
              </Group>
            </Table.Td>
            <Table.Td>
              <CharacterCell character={g.leader} />
            </Table.Td>
            <Table.Td>{g.level}</Table.Td>
            <Table.Td>{g.members}</Table.Td>
            <Table.Td>{number(g.fund)}</Table.Td>
            <Table.Td>{number(g.conquerPointsFund)}</Table.Td>
            <Table.Td>{day(g.created)}</Table.Td>
          </Table.Tr>
        ))}
      />
      <Drawer opened={selected != null} onClose={() => open(null)} position="right" size="xl" title={t('guild.title')}>
        {selected != null && <GuildDetail id={selected} onGone={() => open(null)} />}
      </Drawer>
    </Stack>
  )
}

function GuildDetail({ id, onGone }: { id: number; onGone: () => void }) {
  const { t } = useTranslation('social')
  const number = useNumber()
  const day = useDay()
  const canManage = useCan('ManageCharacters')
  const guild = useQuery({
    queryKey: ['social', 'guild', id],
    queryFn: () => unwrap(api.GET('/api/social/guilds/{id}', { params: { path: { id } } })),
  })
  if (guild.error) return <ErrorAlert error={guild.error} />
  if (!guild.data) return <Loader />
  const { guild: g, members, allies, enemies } = guild.data

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={3}>{g.name}</Title>
        {canManage && (
          <ConfirmAction
            label={t('guild.disband')}
            question={t('guild.disbandQuestion', { name: g.name, count: g.members })}
            done={t('guild.disbanded', { name: g.name })}
            action={async () => {
              await unwrap(api.POST('/api/social/guilds/{id}/disband', { params: { path: { id } } }))
              onGone()
            }}
            invalidate={[['social', 'guilds'], ['social', 'guildwar']]}
          />
        )}
      </Group>
      <SimpleGrid cols={3}>
        <Field label={t('guild.leader')} value={<CharacterCell character={g.leader} />} />
        <Field label={t('guild.level')} value={g.level} />
        <Field label={t('guild.created')} value={day(g.created)} />
        <Field label={t('guild.fund')} value={number(g.fund)} />
        <Field label={t('guild.cpFund')} value={number(g.conquerPointsFund)} />
        <Field
          label={t('guild.requirements')}
          value={t('guild.requirementText', { level: guild.data.requiredLevel, rebirths: guild.data.requiredRebirths })}
        />
      </SimpleGrid>
      <Field label={t('guild.announcement')} value={guild.data.announcement || '—'} />
      <Group gap="xs">
        <Text size="sm" fw={600}>
          {t('guild.allies')}:
        </Text>
        {allies.length === 0 ? <Text size="sm" c="dimmed">—</Text> : allies.map((a) => <Badge key={a.id} color="green" variant="light">{a.name}</Badge>)}
      </Group>
      <Group gap="xs">
        <Text size="sm" fw={600}>
          {t('guild.enemies')}:
        </Text>
        {enemies.length === 0 ? <Text size="sm" c="dimmed">—</Text> : enemies.map((a) => <Badge key={a.id} color="red" variant="light">{a.name}</Badge>)}
      </Group>
      <DataTable
        head={[t('member.character'), t('member.rank'), t('member.silver'), t('member.cps'), t('member.joined')]}
        empty={t('none')}
        rows={members.map((m) => (
          <Table.Tr key={m.character.id}>
            <Table.Td>
              <CharacterCell character={m.character} />
            </Table.Td>
            <Table.Td>{t(`rank.${guildRankKey(m.rank)}`)}</Table.Td>
            <Table.Td>{number(m.silverDonation)}</Table.Td>
            <Table.Td>{number(m.conquerPointsDonation)}</Table.Td>
            <Table.Td>{day(m.joined)}</Table.Td>
          </Table.Tr>
        ))}
      />
    </Stack>
  )
}

export function Field({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div>
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Text size="sm" component="div">
        {value}
      </Text>
    </div>
  )
}
