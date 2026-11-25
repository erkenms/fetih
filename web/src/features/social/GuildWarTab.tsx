import { Badge, Group, Loader, Paper, Progress, SimpleGrid, Stack, Table, Text } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { api, unwrap } from '../../api/client'
import { useCan } from '../../app/auth'
import { ErrorAlert } from '../../app/ErrorAlert'
import { Field } from './GuildsTab'
import { ConfirmAction, DataTable, useNumber, useTime } from './shared'

export function GuildWarTab() {
  const { t } = useTranslation('social')
  const number = useNumber()
  const time = useTime()
  const canManage = useCan('ManageCharacters')
  const war = useQuery({
    queryKey: ['social', 'guildwar'],
    queryFn: () => unwrap(api.GET('/api/social/guildwar')),
    refetchInterval: 5000,
  })
  if (war.isPending) return <Loader />
  if (!war.data) return <ErrorAlert error={war.error} />
  const { live, saved } = war.data
  const winner = war.data.liveWinner ?? saved.winner
  const holder = war.data.liveHolder ?? saved.holder
  const percent = (life: number, max: number) => (max > 0 ? (life / max) * 100 : 0)

  return (
    <Stack>
      <ErrorAlert error={war.error} />
      <Paper withBorder p="md">
        <Group justify="space-between" mb="sm">
          <Group gap="xs">
            <Text fw={600}>{t('war.title')}</Text>
            {live == null ? (
              <Badge color="gray">{t('war.gameOffline')}</Badge>
            ) : live.active ? (
              <Badge color="red">{t('war.active')}</Badge>
            ) : (
              <Badge color="green" variant="light">{t('war.idle')}</Badge>
            )}
          </Group>
          {canManage && live != null && (
            <Group gap="xs">
              <ConfirmAction
                label={t('war.start')}
                question={t('war.startQuestion')}
                done={t('war.started')}
                color="orange"
                disabled={live.active}
                action={() => unwrap(api.POST('/api/social/guildwar/start'))}
                invalidate={[['social', 'guildwar']]}
              />
              <ConfirmAction
                label={t('war.end')}
                question={t('war.endQuestion')}
                done={t('war.ended')}
                disabled={!live.active}
                action={() => unwrap(api.POST('/api/social/guildwar/end'))}
                invalidate={[['social', 'guildwar'], ['social', 'guilds']]}
              />
            </Group>
          )}
        </Group>
        <SimpleGrid cols={{ base: 1, sm: 2, md: 4 }}>
          <Field label={t('war.winner')} value={winner?.name ?? '—'} />
          <Field label={t('war.holder')} value={holder?.name ?? '—'} />
          <Field label={t('war.wonAt')} value={time(live?.wonAt ?? saved.wonAt)} />
          <Field label={t('war.reward')} value={!winner ? '—' : (live?.rewardClaimed ?? saved.rewardClaimed) ? t('war.claimed') : t('war.unclaimed')} />
          <Field label={t('war.schedule')} value={live?.schedule ?? '—'} />
        </SimpleGrid>
      </Paper>
      {live != null && (
        <Paper withBorder p="md">
          <Text fw={600} mb="sm">
            {t('war.structures')}
          </Text>
          <Stack gap="sm">
            {[
              [t('war.pole'), live.poleLife ?? 0, live.poleMaxLife ?? 0, null],
              [t('war.leftGate'), live.leftGateLife ?? 0, live.gateMaxLife ?? 0, live.leftGateOpen],
              [t('war.rightGate'), live.rightGateLife ?? 0, live.gateMaxLife ?? 0, live.rightGateOpen],
            ].map(([label, life, max, open]) => (
              <div key={label as string}>
                <Group justify="space-between">
                  <Group gap="xs">
                    <Text size="sm">{label as string}</Text>
                    {open != null && <Badge size="xs" variant="light" color={open ? 'green' : 'gray'}>{open ? t('war.open') : t('war.closed')}</Badge>}
                  </Group>
                  <Text size="xs" c="dimmed">
                    {number(life as number)} / {number(max as number)}
                  </Text>
                </Group>
                <Progress value={percent(life as number, max as number)} color="red" />
              </div>
            ))}
          </Stack>
        </Paper>
      )}
      {live != null && (
        <Paper withBorder p="md">
          <Text fw={600} mb="sm">
            {t('war.scores')}
          </Text>
          <DataTable
            head={['#', t('guild.name'), t('war.score')]}
            empty={t('war.noScores')}
            rows={(live.scores ?? []).map((s, i) => (
              <Table.Tr key={s.guildID}>
                <Table.Td>{i + 1}</Table.Td>
                <Table.Td>{s.name}</Table.Td>
                <Table.Td>{number(s.score ?? 0)}</Table.Td>
              </Table.Tr>
            ))}
          />
        </Paper>
      )}
    </Stack>
  )
}
