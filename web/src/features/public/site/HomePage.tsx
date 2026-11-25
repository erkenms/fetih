import { Anchor, Button, Group, PasswordInput, Skeleton, Stack, Table, Text, TextInput } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { useState, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { api, unwrap, type Schemas } from '../../../api/client'
import { can, useLogin, useLogout, useMe } from '../../../app/auth'
import { ErrorAlert } from '../../../app/ErrorAlert'
import { BigButton, Panel } from '../../../theme/Panel'
import { MiniTabs, SideMenu } from '../../../theme/SideMenu'
import { formatNumber } from './format'
import classes from './HomePage.module.css'
import { PlayerAvatar } from './PlayerAvatar'

type Home = Schemas['PublicHomeResponse']

// The site's front page, in docs/web-style/main.html's three columns
export default function HomePage() {
  const home = useQuery({
    queryKey: ['public', 'home'],
    queryFn: () => unwrap(api.GET('/api/public/home')),
  })

  return (
    <>
      <ErrorAlert error={home.error} />
      <div className={classes.columns}>
        <aside className={classes.left}>
          <LeftColumn />
        </aside>
        <main className={classes.main}>
          <Ticker data={home.data} loading={home.isLoading} />
          <Announcements />
        </main>
        <aside className={classes.right}>
          <ServerStatus data={home.data} />
          <GuildWar data={home.data} />
          <Best data={home.data} loading={home.isLoading} />
          <InGame />
        </aside>
      </div>
    </>
  )
}

function LeftColumn() {
  const { t } = useTranslation('publicSite')
  const me = useMe().data
  return (
    <>
      <BigButton to="/download" color="gold" title={t('home.download')} note={t('home.downloadNote')} />
      {me ? (
        <BigButton to="/account" color="red" title={t('home.myAccount')} note={t('home.myAccountNote')} />
      ) : (
        <BigButton to="/register" color="red" title={t('home.register')} note={t('home.registerNote')} />
      )}
      <LoginBox />
      <Panel title={t('home.menu')} icon="icons/items/711394.png" pt={2}>
        <SideMenu
          soon={t('home.soon')}
          sections={[
            { title: t('home.menuNews'), items: [{ label: t('home.latestUpdates'), to: '/news' }] },
            {
              title: t('home.menuGame'),
              items: [{ label: t('home.download'), to: '/download' }, { label: t('home.guide') }, { label: t('home.faq') }],
            },
            {
              title: t('home.menuCommunity'),
              items: [
                { label: t('rankings.title'), to: '/rankings', end: true },
                { label: t('rankings.tabs.guilds'), to: '/rankings/guilds' },
                { label: t('rankings.tabs.arena'), to: '/rankings/arena' },
              ],
            },
            {
              title: t('home.menuAccount'),
              items: me
                ? [{ label: t('home.myAccount'), to: '/account' }]
                : [{ label: t('home.register'), to: '/register' }],
            },
          ]}
        />
      </Panel>
      <Panel title={t('home.gallery')} icon="icons/items/724426.png" p={6}>
        <img className={classes.gallery} src="/assets/site/character1.png" alt="" />
        <Text ta="center" size="sm" mt={5} c="dimmed">
          {t('home.wallpapers')} ({t('home.soon')})
        </Text>
      </Panel>
    </>
  )
}

function LoginBox() {
  const { t } = useTranslation()
  const { t: ts } = useTranslation('publicSite')
  const me = useMe().data
  const login = useLogin()
  const logout = useLogout()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')

  if (me)
    return (
      <Panel title={ts('home.loginTitle')} icon="icons/items/713059.png">
        <Stack gap={4}>
          <Text size="sm">{ts('home.welcome', { name: me.username })}</Text>
          <Anchor component={Link} to="/account" size="sm">
            » {ts('home.myAccount')}
          </Anchor>
          {can(me, 'AdminArea') && (
            <Anchor component={Link} to="/admin" size="sm">
              » {t('shell.adminPanel')}
            </Anchor>
          )}
          <Group mt={4}>
            <Button size="xs" onClick={() => logout.mutate()} loading={logout.isPending}>
              {t('shell.logout')}
            </Button>
          </Group>
        </Stack>
      </Panel>
    )

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (username.trim() && password) login.mutate({ username: username.trim(), password })
  }

  return (
    <Panel title={ts('home.loginTitle')} icon="icons/items/713059.png">
      <form onSubmit={submit}>
        <Stack gap={6}>
          <ErrorAlert error={login.error} />
          <TextInput size="xs" label={t('login.username')} autoComplete="username" value={username} onChange={(e) => setUsername(e.currentTarget.value)} />
          <PasswordInput size="xs" label={t('login.password')} autoComplete="current-password" maxLength={16} value={password} onChange={(e) => setPassword(e.currentTarget.value)} />
          <Group justify="space-between" mt={2}>
            <Button size="xs" type="submit" loading={login.isPending}>
              {t('login.submit')}
            </Button>
            <Anchor component={Link} to="/register" size="xs" fw={400}>
              {ts('home.register')}
            </Anchor>
          </Group>
        </Stack>
      </form>
    </Panel>
  )
}

// Until the site has its own news, the ticker shows the latest changes to the server
function Ticker({ data, loading }: { data?: Home; loading: boolean }) {
  const { t, i18n } = useTranslation('publicSite')
  return (
    <Panel title={t('home.ticker')} icon="icons/items/2000247.png" px={8} py={4}>
      {loading ? (
        <Skeleton height={110} />
      ) : data && data.latestNews.length > 0 ? (
        <Table striped>
          <Table.Tbody>
            {data.latestNews.map((item) => (
              <Table.Tr key={item.hash}>
                <Table.Td className={classes.date}>{new Date(item.date).toLocaleDateString(i18n.language)}</Table.Td>
                <Table.Td>{item.subject}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      ) : (
        <Text c="dimmed" size="sm" py={4}>
          {t('home.noData')}
        </Text>
      )}
      <div className={classes.more}>
        <Anchor component={Link} to="/news" size="sm">
          {t('home.allNews')} »
        </Anchor>
      </div>
    </Panel>
  )
}

function Announcements() {
  const { t } = useTranslation('publicSite')
  return (
    <Panel title={t('home.announcements')} icon="icons/items/610120.png" p="md">
      <Text size="md" c="dimmed">
        {t('home.noAnnouncements')}
      </Text>
    </Panel>
  )
}

function ServerStatus({ data }: { data?: Home }) {
  const { t, i18n } = useTranslation('publicSite')
  const rows: [string, ReactNode][] = [
    [
      t('home.status'),
      data ? (
        <Text span fw={700} size="sm" c={data.gameOnline ? '#1d6a12' : '#a01a10'}>
          {data.gameOnline ? t('home.statusOn') : t('home.statusOff')}
        </Text>
      ) : (
        '—'
      ),
    ],
    [t('home.onlineCount'), data?.onlinePlayers != null ? formatNumber(data.onlinePlayers, i18n.language) : '—'],
    [t('home.version'), '5619'],
  ]
  return (
    <Panel title={t('home.serverStatus')} icon="icons/items/724426.png" px={8} py={4}>
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

function GuildWar({ data }: { data?: Home }) {
  const { t } = useTranslation('publicSite')
  const war = data?.guildWar
  return (
    <Panel title={t('home.guildWar')} icon="icons/items/410036.png">
      <div style={{ textAlign: 'center' }}>
        {war?.holderGuildId && war.holderGuildName ? (
          <>
            <Text size="xs" c="dimmed">
              {t('home.holderLabel')}
            </Text>
            <Anchor component={Link} to={`/guilds/${war.holderGuildId}`} className={classes.holder} display="block">
              {war.holderGuildName}
            </Anchor>
          </>
        ) : (
          <Text size="sm" c="dimmed">
            {t('home.noGuildWar')}
          </Text>
        )}
        {war?.winnerGuildId && war.winnerGuildName && (
          <Text size="xs" mt={4}>
            {t('home.winnerLabel')}:{' '}
            <Anchor component={Link} to={`/guilds/${war.winnerGuildId}`} size="xs">
              {war.winnerGuildName}
            </Anchor>
          </Text>
        )}
      </div>
    </Panel>
  )
}

type BestTab = 'level' | 'nobility' | 'arena' | 'guilds'

function Best({ data, loading }: { data?: Home; loading: boolean }) {
  const { t, i18n } = useTranslation('publicSite')
  const [tab, setTab] = useState<BestTab>('level')
  const n = (v: number) => formatNumber(v, i18n.language)

  const rows: { key: number; rank: number; name: string; to: string; avatar?: number; value: string }[] = !data
    ? []
    : tab === 'level'
      ? data.topPlayers.map((p) => ({ key: p.characterId, rank: p.rank, name: p.name, to: `/characters/${p.characterId}`, avatar: p.avatar, value: String(p.level) }))
      : tab === 'nobility'
        ? data.topNobility.map((p) => ({ key: p.characterId, rank: p.rank, name: p.name, to: `/characters/${p.characterId}`, avatar: p.avatar, value: t(`nobilityRanks.${p.nobilityRank}`) }))
        : tab === 'arena'
          ? data.topArena.map((p) => ({ key: p.characterId, rank: p.rank, name: p.name, to: `/characters/${p.characterId}`, avatar: p.avatar, value: n(p.points) }))
          : data.topGuilds.map((g) => ({ key: g.guildId, rank: g.rank, name: g.name, to: `/guilds/${g.guildId}`, value: n(g.fund) }))

  return (
    <Panel title={t('home.best')} icon="icons/items/200007.png" px={8} py={6}>
      <MiniTabs
        value={tab}
        onChange={setTab}
        tabs={[
          { value: 'level', label: t('home.tabLevel') },
          { value: 'nobility', label: t('home.tabNobility') },
          { value: 'arena', label: t('home.tabArena') },
          { value: 'guilds', label: t('home.tabGuilds') },
        ]}
      />
      {loading ? (
        <Skeleton height={140} />
      ) : rows.length > 0 ? (
        <Table striped>
          <Table.Tbody>
            {rows.map((r) => (
              <Table.Tr key={r.key}>
                <Table.Td w={18} fw={700} c="dimmed">
                  {r.rank}.
                </Table.Td>
                <Table.Td>
                  <Group gap={4} wrap="nowrap">
                    {r.avatar != null && <PlayerAvatar avatar={r.avatar} name={r.name} size={20} />}
                    <Anchor component={Link} to={r.to} size="sm" truncate>
                      {r.name}
                    </Anchor>
                  </Group>
                </Table.Td>
                <Table.Td ta="right">{r.value}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      ) : (
        <Text c="dimmed" size="sm" py={4}>
          {t('home.noData')}
        </Text>
      )}
      <div className={classes.more}>
        <Anchor component={Link} to={`/rankings/${tab}`} size="sm">
          {t('home.fullRanking')} »
        </Anchor>
      </div>
    </Panel>
  )
}

function InGame() {
  const { t } = useTranslation('publicSite')
  return (
    <Panel title={t('home.inGame')} icon="icons/items/610120.png" p={5}>
      <div className={classes.chat}>{t('home.inGameEmpty')}</div>
    </Panel>
  )
}
