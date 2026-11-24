import { Anchor, Badge, Card, Group, Loader, Paper, ScrollArea, SimpleGrid, Stack, Table, Text, Title } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router'
import { api, ApiError, unwrap } from '../../../api/client'
import { ErrorAlert } from '../../../app/ErrorAlert'
import { classKey, formatNumber, formatUnixDate, guildRankKey } from './format'
import { PlayerAvatar } from './PlayerAvatar'

export default function GuildPage() {
  const { id } = useParams<{ id: string }>()
  const { t, i18n } = useTranslation('publicSite')
  const guildId = Number(id)

  const query = useQuery({
    queryKey: ['public', 'guild', guildId],
    queryFn: () => unwrap(api.GET('/api/public/guilds/{id}', { params: { path: { id: guildId } } })),
    enabled: Number.isFinite(guildId) && guildId > 0,
  })

  if (query.isLoading) {
    return (
      <Group justify="center" py="xl">
        <Loader />
      </Group>
    )
  }

  if (query.error instanceof ApiError && query.error.status === 404) {
    return (
      <Stack py="xl">
        <Title order={2}>{t('guild.notFound')}</Title>
      </Stack>
    )
  }

  const g = query.data
  if (!g) return <ErrorAlert error={query.error} />

  return (
    <Stack gap="lg" py="md">
      <Card withBorder p="lg">
        <Group justify="space-between" wrap="wrap" gap="md">
          <Stack gap={4}>
            <Group gap="sm">
              <Title order={2}>{g.name}</Title>
              <Badge size="lg" variant="light">
                {t('guild.level', { level: g.level })}
              </Badge>
              {g.isGuildWarHolder && (
                <Badge size="lg" color="yellow" variant="light">
                  {t('rankings.badges.holder')}
                </Badge>
              )}
              {g.isGuildWarWinner && !g.isGuildWarHolder && (
                <Badge size="lg" color="orange" variant="light">
                  {t('rankings.badges.winner')}
                </Badge>
              )}
            </Group>
            {g.announcement && <Text c="dimmed">{g.announcement}</Text>}
          </Stack>
        </Group>
      </Card>

      <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
        <Card withBorder>
          <Title order={4} mb="sm">
            {t('guild.title')}
          </Title>
          <Table>
            <Table.Tbody>
              <Table.Tr>
                <Table.Td c="dimmed">{t('guild.leader')}</Table.Td>
                <Table.Td>
                  {g.leaderName ? (
                    <Anchor component={Link} to={`/characters/${g.leaderId}`} fw={500}>
                      {g.leaderName}
                    </Anchor>
                  ) : (
                    '—'
                  )}
                </Table.Td>
              </Table.Tr>
              <Table.Tr>
                <Table.Td c="dimmed">{t('guild.fund')}</Table.Td>
                <Table.Td>{formatNumber(g.fund, i18n.language)}</Table.Td>
              </Table.Tr>
              <Table.Tr>
                <Table.Td c="dimmed">{t('guild.cpFund')}</Table.Td>
                <Table.Td>{formatNumber(g.conquerPointsFund, i18n.language)}</Table.Td>
              </Table.Tr>
              <Table.Tr>
                <Table.Td c="dimmed">{t('guild.created')}</Table.Td>
                <Table.Td>{formatUnixDate(g.created, i18n.language)}</Table.Td>
              </Table.Tr>
            </Table.Tbody>
          </Table>
        </Card>

        <Card withBorder>
          <Stack gap="md">
            <div>
              <Title order={5} mb="xs">
                {t('guild.allies')}
              </Title>
              {g.allies.length > 0 ? (
                <Group gap="xs">
                  {g.allies.map((a) => (
                    <Badge key={a.guildId} variant="light" color="green" component={Link} to={`/guilds/${a.guildId}`} style={{ cursor: 'pointer' }}>
                      {a.name}
                    </Badge>
                  ))}
                </Group>
              ) : (
                <Text size="sm" c="dimmed">
                  {t('guild.noRelations')}
                </Text>
              )}
            </div>
            <div>
              <Title order={5} mb="xs">
                {t('guild.enemies')}
              </Title>
              {g.enemies.length > 0 ? (
                <Group gap="xs">
                  {g.enemies.map((e) => (
                    <Badge key={e.guildId} variant="light" color="red" component={Link} to={`/guilds/${e.guildId}`} style={{ cursor: 'pointer' }}>
                      {e.name}
                    </Badge>
                  ))}
                </Group>
              ) : (
                <Text size="sm" c="dimmed">
                  {t('guild.noRelations')}
                </Text>
              )}
            </div>
          </Stack>
        </Card>
      </SimpleGrid>

      <Paper withBorder>
        <Title order={4} p="md" pb="xs">
          {t('guild.members', { count: g.members.length })}
        </Title>
        <ScrollArea>
          <Table striped highlightOnHover miw={600}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('rankings.columns.player')}</Table.Th>
                <Table.Th>{t('rankings.columns.class')}</Table.Th>
                <Table.Th>{t('rankings.columns.level')}</Table.Th>
                <Table.Th>{t('guild.rank')}</Table.Th>
                <Table.Th>{t('guild.joined')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {g.members.map((m) => (
                <Table.Tr key={m.characterId}>
                  <Table.Td>
                    <Group gap="xs" wrap="nowrap">
                      <PlayerAvatar avatar={m.avatar} name={m.name} size="sm" />
                      <Anchor component={Link} to={`/characters/${m.characterId}`} fw={500}>
                        {m.name}
                      </Anchor>
                    </Group>
                  </Table.Td>
                  <Table.Td>{t(`classes.${classKey(m.class)}`)}</Table.Td>
                  <Table.Td>{m.level}</Table.Td>
                  <Table.Td>{t(`guildRanks.${guildRankKey(m.rank)}`)}</Table.Td>
                  <Table.Td>{formatUnixDate(m.joined, i18n.language)}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      </Paper>
    </Stack>
  )
}
