import { Badge, Drawer, Group, Loader, SimpleGrid, Stack, Table, Text, Title } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router'
import { api, unwrap } from '../../api/client'
import { ErrorAlert } from '../../app/ErrorAlert'
import { Field } from './GuildsTab'
import { familyRankKey } from './names'
import { CharacterCell, DataTable, SearchBox, useDay, useNumber } from './shared'

export function FamiliesTab() {
  const { t } = useTranslation('social')
  const number = useNumber()
  const day = useDay()
  const [params, setParams] = useSearchParams()
  const [search, setSearch] = useState('')
  const [debounced] = useDebouncedValue(search.trim(), 300)
  const families = useQuery({
    queryKey: ['social', 'families', debounced],
    queryFn: () => unwrap(api.GET('/api/social/families', { params: { query: { search: debounced || undefined } } })),
    placeholderData: keepPreviousData,
  })
  const selected = Number(params.get('family')) || null
  const open = (id: number | null) =>
    setParams((p) => {
      if (id) p.set('family', String(id))
      else p.delete('family')
      return p
    })

  return (
    <Stack>
      <Group justify="space-between">
        <SearchBox value={search} onChange={setSearch} />
        {families.isFetching && <Loader size="sm" />}
      </Group>
      <ErrorAlert error={families.error} />
      <DataTable
        head={[t('family.name'), t('guild.leader'), t('guild.level'), t('guild.members'), t('guild.fund'), t('family.territories'), t('guild.created')]}
        empty={t('none')}
        rows={(families.data ?? []).map((f) => (
          <Table.Tr key={f.id} onClick={() => open(f.id)} style={{ cursor: 'pointer' }}>
            <Table.Td>
              <Text size="sm" fw={500}>
                {f.name}
              </Text>
            </Table.Td>
            <Table.Td>
              <CharacterCell character={f.leader} />
            </Table.Td>
            <Table.Td>{f.level}</Table.Td>
            <Table.Td>{f.members}</Table.Td>
            <Table.Td>{number(f.fund)}</Table.Td>
            <Table.Td>{f.territories}</Table.Td>
            <Table.Td>{day(f.created)}</Table.Td>
          </Table.Tr>
        ))}
      />
      <Drawer opened={selected != null} onClose={() => open(null)} position="right" size="xl" title={t('family.title')}>
        {selected != null && <FamilyDetail id={selected} />}
      </Drawer>
    </Stack>
  )
}

function FamilyDetail({ id }: { id: number }) {
  const { t } = useTranslation('social')
  const number = useNumber()
  const day = useDay()
  const family = useQuery({
    queryKey: ['social', 'family', id],
    queryFn: () => unwrap(api.GET('/api/social/families/{id}', { params: { path: { id } } })),
  })
  if (family.error) return <ErrorAlert error={family.error} />
  if (!family.data) return <Loader />
  const { family: f, members, allies, enemies, occupied, signedUp } = family.data

  return (
    <Stack>
      <Title order={3}>{f.name}</Title>
      <SimpleGrid cols={3}>
        <Field label={t('guild.leader')} value={<CharacterCell character={f.leader} />} />
        <Field label={t('guild.level')} value={f.level} />
        <Field label={t('guild.created')} value={day(f.created)} />
        <Field label={t('guild.fund')} value={number(f.fund)} />
      </SimpleGrid>
      <Field label={t('guild.announcement')} value={family.data.announcement || '—'} />
      <Group gap="xs">
        <Text size="sm" fw={600}>
          {t('family.occupied')}:
        </Text>
        {occupied.length === 0 ? (
          <Text size="sm" c="dimmed">—</Text>
        ) : (
          occupied.map((o) => (
            <Badge key={o.npcId} color="yellow" variant="light">
              {t('family.occupiedDays', { name: o.name, count: o.days })}
            </Badge>
          ))
        )}
      </Group>
      <Group gap="xs">
        <Text size="sm" fw={600}>
          {t('family.signedUp')}:
        </Text>
        {signedUp.length === 0 ? <Text size="sm" c="dimmed">—</Text> : signedUp.map((s) => <Badge key={s.npcId} variant="light">{s.name}</Badge>)}
      </Group>
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
        head={[t('member.character'), t('member.rank'), t('member.donation'), t('member.joined')]}
        empty={t('none')}
        rows={members.map((m) => (
          <Table.Tr key={m.character.id}>
            <Table.Td>
              <CharacterCell character={m.character} />
            </Table.Td>
            <Table.Td>{t(`rank.${familyRankKey(m.rank)}`)}</Table.Td>
            <Table.Td>{number(m.donation)}</Table.Td>
            <Table.Td>{day(m.joined)}</Table.Td>
          </Table.Tr>
        ))}
      />
    </Stack>
  )
}
