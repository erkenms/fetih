import {
  Anchor,
  Badge,
  Card,
  Group,
  Loader,
  Paper,
  ScrollArea,
  SimpleGrid,
  Stack,
  Table,
  Text,
  Title,
  Tooltip,
  useComputedColorScheme,
} from '@mantine/core'
import { IconExternalLink } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { api, unwrap, type Schemas } from '../../api/client'
import { useMe } from '../../app/auth'
import { ErrorAlert } from '../../app/ErrorAlert'
import { byteSize, durationParts, grafanaDashboard, grafanaPanel, since } from './format'

// Panels of the provisioned Overview dashboard (observability/grafana/dashboards)
const panels = [
  { id: 7, key: 'online' },
  { id: 8, key: 'packets' },
  { id: 10, key: 'errors' },
  { id: 9, key: 'logins' },
] as const

export default function DashboardPage() {
  const { t } = useTranslation('dashboard')
  const me = useMe().data
  const dashboard = useQuery({
    queryKey: ['dashboard'],
    queryFn: () => unwrap(api.GET('/api/dashboard')),
    refetchInterval: 10_000,
  })
  const data = dashboard.data

  return (
    <Stack>
      <Group justify="space-between">
        <div>
          <Title order={2}>{t('title')}</Title>
          <Text c="dimmed">{t('welcome', { name: me?.username ?? '' })}</Text>
        </div>
        {dashboard.isFetching && <Loader size="sm" />}
      </Group>
      <ErrorAlert error={dashboard.error} />
      {data && (
        <>
          <Headline data={data} />
          <World game={data.game} />
          <SimpleGrid cols={{ base: 1, lg: 2 }}>
            <RecentLogins logs={data.logs} online={data.onlineAccounts} />
            <RecentErrors logs={data.logs} grafana={data.grafanaUrl} />
          </SimpleGrid>
          <Grafana base={data.grafanaUrl} />
        </>
      )}
    </Stack>
  )
}

function Stat({ label, value, hint, color }: { label: string; value: ReactNode; hint?: ReactNode; color?: string }) {
  return (
    <Card withBorder padding="md">
      <Text size="xs" c="dimmed" tt="uppercase" fw={600}>
        {label}
      </Text>
      <Text fz={28} fw={700} c={color} lh={1.3}>
        {value}
      </Text>
      {hint && (
        <Text size="sm" c="dimmed">
          {hint}
        </Text>
      )}
    </Card>
  )
}

function Headline({ data }: { data: Schemas['DashboardResponse'] }) {
  const { t, i18n } = useTranslation('dashboard')
  const number = (n: number) => n.toLocaleString(i18n.language)
  const game = data.game
  const uptime = game ? durationParts(game.uptimeSeconds ?? 0) : null
  const logs = data.logs
  const offline = <Badge color="red">{t('offline')}</Badge>
  const noLogs = (
    <Tooltip label={t('lokiDown')}>
      <Text span c="dimmed">
        —
      </Text>
    </Tooltip>
  )

  return (
    <SimpleGrid cols={{ base: 1, xs: 2, md: 3, xl: 6 }}>
      <Stat
        label={t('stat.online')}
        value={game ? number(game.players ?? 0) : offline}
        hint={game ? t('stat.connections', { count: game.connections ?? 0 }) : t('stat.gameDown')}
      />
      <Stat
        label={t('stat.uptime')}
        value={uptime ? t('uptime', uptime) : offline}
        hint={game?.startedAt ? t('stat.since', { time: new Date(game.startedAt).toLocaleString(i18n.language) }) : undefined}
      />
      <Stat
        label={t('stat.errors')}
        value={logs ? number(logs.errors) : noLogs}
        color={logs && logs.errors > 0 ? 'red' : undefined}
        hint={logs ? t('stat.warnings', { count: logs.warnings }) : undefined}
      />
      <Stat
        label={t('stat.logins')}
        value={logs ? number(logs.logins) : noLogs}
        hint={logs ? t('stat.loginAccounts', { count: logs.accounts }) : undefined}
      />
      <Stat
        label={t('stat.accounts')}
        value={number(data.counts.accounts)}
        hint={t('stat.new', { count: data.counts.accountsLastDay })}
      />
      <Stat
        label={t('stat.characters')}
        value={number(data.counts.characters)}
        hint={t('stat.new', { count: data.counts.charactersLastDay })}
      />
    </SimpleGrid>
  )
}

function World({ game }: { game: Schemas['ServerState'] | null }) {
  const { t, i18n } = useTranslation('dashboard')
  if (!game) return null
  const number = (n: number | undefined) => (n ?? 0).toLocaleString(i18n.language)
  const memory = byteSize(game.workingSetBytes ?? 0)
  const ms = (n: number | undefined) => (n ?? 0).toLocaleString(i18n.language, { maximumFractionDigits: 1 })
  const rows: [string, string][] = [
    [t('world.maps'), number(game.mapsLoaded)],
    [t('world.monsters'), t('world.alive', { alive: number(game.monstersAlive), total: number(game.monsters) })],
    [t('world.groundItems'), number(game.groundItems)],
    [t('world.booths'), number(game.booths)],
    [t('world.broadcasts'), number(game.broadcastQueue)],
    [t('world.memory'), `${memory.value.toLocaleString(i18n.language)} ${memory.unit}`],
    [t('world.monsterTick'), t('world.tick', { last: ms(game.monsterTickMilliseconds), slowest: ms(game.slowestMonsterTickMilliseconds) })],
  ]
  return (
    <Paper withBorder p="md">
      <Text fw={600} mb="xs">
        {t('world.title')}
      </Text>
      <SimpleGrid cols={{ base: 2, sm: 4, xl: 7 }} spacing="xs">
        {rows.map(([label, value]) => (
          <div key={label}>
            <Text size="xs" c="dimmed">
              {label}
            </Text>
            <Text fw={600}>{value}</Text>
          </div>
        ))}
      </SimpleGrid>
    </Paper>
  )
}

function Ago({ time }: { time: string }) {
  const { i18n } = useTranslation()
  const date = new Date(time)
  const { value, unit } = since(date, new Date())
  return (
    <Tooltip label={date.toLocaleString(i18n.language)}>
      <Text span size="sm">
        {new Intl.RelativeTimeFormat(i18n.language, { numeric: 'auto' }).format(value, unit)}
      </Text>
    </Tooltip>
  )
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Paper withBorder p="md">
      <Text fw={600} mb="xs">
        {title}
      </Text>
      {children}
    </Paper>
  )
}

function RecentLogins({ logs, online }: { logs: Schemas['LogStatsResponse'] | null; online: number[] }) {
  const { t } = useTranslation('dashboard')
  const onlineSet = new Set(online)
  if (!logs) return <Section title={t('logins.title')}><Text c="dimmed">{t('lokiDown')}</Text></Section>
  return (
    <Section title={t('logins.title')}>
      {logs.recentLogins.length === 0 ? (
        <Text c="dimmed">{t('logins.none')}</Text>
      ) : (
        <ScrollArea>
          <Table striped miw={520}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('logins.time')}</Table.Th>
                <Table.Th>{t('logins.character')}</Table.Th>
                <Table.Th>{t('logins.account')}</Table.Th>
                <Table.Th>{t('logins.ip')}</Table.Th>
                <Table.Th>{t('logins.map')}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {logs.recentLogins.map((login) => (
                <Table.Tr key={`${login.timestamp}-${login.characterId}`}>
                  <Table.Td>
                    <Ago time={login.timestamp} />
                  </Table.Td>
                  <Table.Td>
                    <Group gap={6} wrap="nowrap">
                      <Text size="sm" fw={500}>
                        {login.name}
                      </Text>
                      <Text size="xs" c="dimmed">
                        {t('logins.level', { level: login.level })}
                      </Text>
                      {onlineSet.has(login.accountId) && (
                        <Badge size="xs" color="green" variant="light">
                          {t('logins.online')}
                        </Badge>
                      )}
                    </Group>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm">#{login.accountId}</Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" ff="monospace">
                      {login.ipAddress}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm">{login.mapId}</Text>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      )}
    </Section>
  )
}

function RecentErrors({ logs, grafana }: { logs: Schemas['LogStatsResponse'] | null; grafana: string }) {
  const { t } = useTranslation('dashboard')
  if (!logs) return <Section title={t('errors.title')}><Text c="dimmed">{t('lokiDown')}</Text></Section>
  return (
    <Section title={t('errors.title')}>
      {logs.recentErrors.length === 0 ? (
        <Text c="dimmed">{t('errors.none')}</Text>
      ) : (
        <Stack gap="xs">
          {logs.recentErrors.map((line) => (
            <div key={`${line.timestamp}-${line.message}`}>
              <Group gap={6}>
                <Badge size="xs" color={line.level === 'fatal' ? 'grape' : 'red'}>
                  {line.level}
                </Badge>
                <Text size="xs" c="dimmed">
                  {line.server} · {line.event}
                </Text>
                <Text size="xs" c="dimmed">
                  <Ago time={line.timestamp} />
                </Text>
              </Group>
              <Text size="sm" lineClamp={2}>
                {line.message}
              </Text>
            </div>
          ))}
        </Stack>
      )}
      <Anchor href={grafanaDashboard(grafana)} target="_blank" rel="noreferrer" size="sm" mt="sm" display="inline-block">
        {t('openGrafana')} <IconExternalLink size={12} />
      </Anchor>
    </Section>
  )
}

function Grafana({ base }: { base: string }) {
  const { t } = useTranslation('dashboard')
  const theme = useComputedColorScheme('light')
  return (
    <Paper withBorder p="md">
      <Group justify="space-between" mb="xs">
        <Text fw={600}>{t('grafana.title')}</Text>
        <Anchor href={grafanaDashboard(base)} target="_blank" rel="noreferrer" size="sm">
          {t('openGrafana')} <IconExternalLink size={12} />
        </Anchor>
      </Group>
      <Text size="xs" c="dimmed" mb="sm">
        {t('grafana.note')}
      </Text>
      <SimpleGrid cols={{ base: 1, lg: 2 }}>
        {panels.map((panel) => (
          <iframe
            key={panel.id}
            title={t(`grafana.${panel.key}`)}
            src={grafanaPanel(base, panel.id, theme)}
            style={{ width: '100%', height: 260, border: 0, borderRadius: 4 }}
            loading="lazy"
          />
        ))}
      </SimpleGrid>
    </Paper>
  )
}
