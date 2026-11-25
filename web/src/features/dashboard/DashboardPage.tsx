import {
  Anchor,
  Badge,
  Group,
  Loader,
  ScrollArea,
  SimpleGrid,
  Stack,
  Table,
  Text,
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
import { Panel } from '../../theme/Panel'
import classes from './DashboardPage.module.css'
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
    <Stack gap={10}>
      <ErrorAlert error={dashboard.error} />
      <Panel
        title={t('title')}
        icon="icons/items/724426.png"
        aside={dashboard.isFetching ? <Loader size={12} color="#f5dfa3" /> : t('welcome', { name: me?.username ?? '' })}
        p={4}
      >
        {data ? <Headline data={data} /> : <Loader size="sm" m="sm" />}
      </Panel>
      {data && (
        <>
          <div className={classes.row}>
            <div className={classes.wide}>
              <RecentLogins logs={data.logs} online={data.onlineAccounts} />
            </div>
            <div className={classes.narrow}>
              <World game={data.game} />
              <RecentErrors logs={data.logs} grafana={data.grafanaUrl} />
            </div>
          </div>
          <Grafana base={data.grafanaUrl} />
        </>
      )}
    </Stack>
  )
}

function Stat({ label, value, hint, color }: { label: string; value: ReactNode; hint?: ReactNode; color?: string }) {
  return (
    <div className={classes.stat}>
      <span className={classes.statLabel}>{label}</span>
      <span className={classes.statValue} style={color ? { color } : undefined}>
        {value}
      </span>
      {hint && <span className={classes.statHint}>{hint}</span>}
    </div>
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
    <div className={classes.stats}>
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
        color={logs && logs.errors > 0 ? '#a01a10' : undefined}
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
    </div>
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
    <Panel title={t('world.title')} icon="icons/items/610120.png" px={8} py={4} mb={10}>
      <Table striped>
        <Table.Tbody>
          {rows.map(([label, value]) => (
            <Table.Tr key={label}>
              <Table.Td>{label}</Table.Td>
              <Table.Td ta="right" fw={700}>
                {value}
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Panel>
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

function Section({ title, icon, children, aside }: { title: string; icon: string; children: ReactNode; aside?: ReactNode }) {
  return (
    <Panel title={title} icon={icon} aside={aside} p="sm" mb={10}>
      {children}
    </Panel>
  )
}

function RecentLogins({ logs, online }: { logs: Schemas['LogStatsResponse'] | null; online: number[] }) {
  const { t } = useTranslation('dashboard')
  const onlineSet = new Set(online)
  if (!logs) return <Section title={t('logins.title')} icon="icons/items/713059.png"><Text c="dimmed">{t('lokiDown')}</Text></Section>
  return (
    <Section title={t('logins.title')} icon="icons/items/713059.png">
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
  if (!logs) return <Section title={t('errors.title')} icon="icons/items/2000247.png"><Text c="dimmed">{t('lokiDown')}</Text></Section>
  return (
    <Section title={t('errors.title')} icon="icons/items/2000247.png">
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
    <Panel
      title={t('grafana.title')}
      icon="icons/items/724426.png"
      aside={
        <Anchor href={grafanaDashboard(base)} target="_blank" rel="noreferrer" size="xs" c="#f5dfa3">
          {t('openGrafana')} <IconExternalLink size={10} />
        </Anchor>
      }
      p="sm"
    >
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
    </Panel>
  )
}
