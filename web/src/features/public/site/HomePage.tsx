import { Anchor, Badge, Button, Card, Group, SimpleGrid, Skeleton, Stack, Table, Text, Title } from '@mantine/core'
import { IconDownload, IconNews, IconSword, IconTrophy } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { api, unwrap } from '../../../api/client'
import { ErrorAlert } from '../../../app/ErrorAlert'
import { classKey, formatNumber } from './format'
import { PlayerAvatar } from './PlayerAvatar'

export default function HomePage() {
  const { t, i18n } = useTranslation('publicSite')
  const home = useQuery({
    queryKey: ['public', 'home'],
    queryFn: () => unwrap(api.GET('/api/public/home')),
  })

  const data = home.data

  return (
    <Stack gap="lg" py="md">
      <Card withBorder p="lg">
        <Group justify="space-between" align="flex-start" wrap="wrap" gap="md">
          <Stack gap="xs">
            <Group gap="sm" align="center">
              <Title order={1}>{t('home.title')}</Title>
              {home.isLoading ? (
                <Skeleton height={22} width={100} />
              ) : data?.gameOnline ? (
                <Badge color="green" variant="light" size="lg">
                  {t('home.online', { count: data.onlinePlayers ?? 0 })}
                </Badge>
              ) : (
                <Badge color="gray" variant="light" size="lg">
                  {t('home.offline')}
                </Badge>
              )}
            </Group>
            <Text c="dimmed">{t('home.subtitle')}</Text>
            {data?.guildWar && (data.guildWar.holderGuildName || data.guildWar.winnerGuildName) && (
              <Group gap="xs">
                <IconSword size={16} />
                {data.guildWar.holderGuildId && data.guildWar.holderGuildName && (
                  <Text size="sm">
                    {t('home.holder', { name: '' })}
                    <Anchor component={Link} to={`/guilds/${data.guildWar.holderGuildId}`} fw={600}>
                      {data.guildWar.holderGuildName}
                    </Anchor>
                  </Text>
                )}
                {data.guildWar.winnerGuildId &&
                  data.guildWar.winnerGuildName &&
                  data.guildWar.winnerGuildId !== data.guildWar.holderGuildId && (
                    <Text size="sm" c="dimmed">
                      ({t('home.winner', { name: '' })}
                      <Anchor component={Link} to={`/guilds/${data.guildWar.winnerGuildId}`}>
                        {data.guildWar.winnerGuildName}
                      </Anchor>
                      )
                    </Text>
                  )}
              </Group>
            )}
          </Stack>
          <Group>
            <Button component={Link} to="/rankings" leftSection={<IconTrophy size={16} />}>
              {t('home.browseRankings')}
            </Button>
            <Button component={Link} to="/download" variant="light" leftSection={<IconDownload size={16} />}>
              {t('home.downloadClient')}
            </Button>
          </Group>
        </Group>
      </Card>

      <ErrorAlert error={home.error} />

      <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
        <Card withBorder>
          <Group justify="space-between" mb="sm">
            <Title order={4}>{t('home.topPlayers')}</Title>
            <Anchor component={Link} to="/rankings/level" size="sm">
              {t('home.viewAll')}
            </Anchor>
          </Group>
          {home.isLoading ? (
            <Skeleton height={180} />
          ) : data && data.topPlayers.length > 0 ? (
            <Table striped highlightOnHover>
              <Table.Tbody>
                {data.topPlayers.map((p) => (
                  <Table.Tr key={p.characterId}>
                    <Table.Td w={32}>{p.rank}</Table.Td>
                    <Table.Td>
                      <Group gap="xs" wrap="nowrap">
                        <PlayerAvatar avatar={p.avatar} name={p.name} size="sm" />
                        <Anchor component={Link} to={`/characters/${p.characterId}`} fw={500}>
                          {p.name}
                        </Anchor>
                      </Group>
                    </Table.Td>
                    <Table.Td>{t(`classes.${classKey(p.class)}`)}</Table.Td>
                    <Table.Td ta="right">
                      Lv. {p.level}
                      {p.rebirths > 0 && (
                        <Badge size="xs" ml="xs" variant="light">
                          {p.rebirths}RB
                        </Badge>
                      )}
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          ) : (
            <Text c="dimmed" size="sm">
              {t('home.noData')}
            </Text>
          )}
        </Card>

        <Card withBorder>
          <Group justify="space-between" mb="sm">
            <Title order={4}>{t('home.topNobility')}</Title>
            <Anchor component={Link} to="/rankings/nobility" size="sm">
              {t('home.viewAll')}
            </Anchor>
          </Group>
          {home.isLoading ? (
            <Skeleton height={180} />
          ) : data && data.topNobility.length > 0 ? (
            <Table striped highlightOnHover>
              <Table.Tbody>
                {data.topNobility.map((n) => (
                  <Table.Tr key={n.characterId}>
                    <Table.Td w={32}>{n.rank}</Table.Td>
                    <Table.Td>
                      <Group gap="xs" wrap="nowrap">
                        <PlayerAvatar avatar={n.avatar} name={n.name} size="sm" />
                        <Anchor component={Link} to={`/characters/${n.characterId}`} fw={500}>
                          {n.name}
                        </Anchor>
                      </Group>
                    </Table.Td>
                    <Table.Td>
                      <Badge variant="light" size="sm">
                        {t(`nobilityRanks.${n.nobilityRank}`)}
                      </Badge>
                    </Table.Td>
                    <Table.Td ta="right">{formatNumber(n.donation, i18n.language)}</Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          ) : (
            <Text c="dimmed" size="sm">
              {t('home.noData')}
            </Text>
          )}
        </Card>

        <Card withBorder>
          <Group justify="space-between" mb="sm">
            <Title order={4}>{t('home.topGuilds')}</Title>
            <Anchor component={Link} to="/rankings/guilds" size="sm">
              {t('home.viewAll')}
            </Anchor>
          </Group>
          {home.isLoading ? (
            <Skeleton height={180} />
          ) : data && data.topGuilds.length > 0 ? (
            <Table striped highlightOnHover>
              <Table.Tbody>
                {data.topGuilds.map((g) => (
                  <Table.Tr key={g.guildId}>
                    <Table.Td w={32}>{g.rank}</Table.Td>
                    <Table.Td>
                      <Group gap="xs">
                        <Anchor component={Link} to={`/guilds/${g.guildId}`} fw={500}>
                          {g.name}
                        </Anchor>
                        {g.isGuildWarHolder && (
                          <Badge size="xs" color="yellow" variant="light">
                            {t('rankings.badges.holder')}
                          </Badge>
                        )}
                      </Group>
                    </Table.Td>
                    <Table.Td>{g.memberCount}</Table.Td>
                    <Table.Td ta="right">{formatNumber(g.fund, i18n.language)}</Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          ) : (
            <Text c="dimmed" size="sm">
              {t('home.noData')}
            </Text>
          )}
        </Card>

        <Card withBorder>
          <Group justify="space-between" mb="sm">
            <Title order={4}>{t('home.arenaLeaders')}</Title>
            <Anchor component={Link} to="/rankings/arena" size="sm">
              {t('home.viewAll')}
            </Anchor>
          </Group>
          {home.isLoading ? (
            <Skeleton height={180} />
          ) : data && data.topArena.length > 0 ? (
            <Table striped highlightOnHover>
              <Table.Tbody>
                {data.topArena.map((a) => (
                  <Table.Tr key={a.characterId}>
                    <Table.Td w={32}>{a.rank}</Table.Td>
                    <Table.Td>
                      <Group gap="xs" wrap="nowrap">
                        <PlayerAvatar avatar={a.avatar} name={a.name} size="sm" />
                        <Anchor component={Link} to={`/characters/${a.characterId}`} fw={500}>
                          {a.name}
                        </Anchor>
                      </Group>
                    </Table.Td>
                    <Table.Td ta="right">{formatNumber(a.points, i18n.language)}</Table.Td>
                    <Table.Td ta="right" c="dimmed">
                      {a.winsTotal} / {a.lossesTotal}
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          ) : (
            <Text c="dimmed" size="sm">
              {t('home.noData')}
            </Text>
          )}
        </Card>
      </SimpleGrid>

      <Card withBorder>
        <Group justify="space-between" mb="sm">
          <Group gap="xs">
            <IconNews size={18} />
            <Title order={4}>{t('home.latestNews')}</Title>
          </Group>
          <Anchor component={Link} to="/news" size="sm">
            {t('home.viewAll')}
          </Anchor>
        </Group>
        {home.isLoading ? (
          <Skeleton height={120} />
        ) : data && data.latestNews.length > 0 ? (
          <Stack gap="xs">
            {data.latestNews.map((item) => (
              <Group key={item.hash} justify="space-between" wrap="nowrap">
                <Group gap="xs" wrap="nowrap" style={{ minWidth: 0 }}>
                  <Badge variant="outline" size="xs" style={{ flexShrink: 0 }}>
                    {item.shortHash}
                  </Badge>
                  <Text size="sm" truncate>
                    {item.subject}
                  </Text>
                </Group>
                <Text size="xs" c="dimmed" style={{ flexShrink: 0 }}>
                  {new Date(item.date).toLocaleDateString(i18n.language)}
                </Text>
              </Group>
            ))}
          </Stack>
        ) : (
          <Text c="dimmed" size="sm">
            {t('home.noData')}
          </Text>
        )}
      </Card>
    </Stack>
  )
}
