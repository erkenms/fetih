import {
  Alert,
  Anchor,
  Badge,
  Card,
  Group,
  Loader,
  ScrollArea,
  SimpleGrid,
  Stack,
  Table,
  Tabs,
  Text,
  Title,
} from '@mantine/core'
import { IconArrowLeft, IconInfoCircle, IconMapPin } from '@tabler/icons-react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router'
import { api, unwrap } from '../../api/client'
import { can, useMe } from '../../app/auth'
import { ErrorAlert } from '../../app/ErrorAlert'
import { formatProfession } from '../../lib/gameMeta'
import { LogTimeline } from '../logs/LogTimeline'
import { AccountActions } from '../accounts/AccountActions'
import { accountStatusColor, authorityColor } from '../accounts/accountMeta'
import { CharacterActionsBar } from '../characters/CharacterActionsBar'
import { CharacterPanels } from '../characters/CharacterPanels'
import { isMuted } from '../characters/characterMeta'

const ipLink = (ip: string) => `/admin/players?online=0&ip=${encodeURIComponent(ip)}`

// An account and its character: the account holds at most one
export default function PlayerDetailPage() {
  const { id } = useParams()
  const accountId = Number(id)
  const { t, i18n } = useTranslation(['players', 'accounts', 'characters', 'items', 'common'])
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const me = useMe().data

  const account = useQuery({
    queryKey: ['account', accountId],
    queryFn: () => unwrap(api.GET('/api/accounts/{id}', { params: { path: { id: accountId } } })),
    enabled: Number.isFinite(accountId) && accountId > 0,
  })
  const characterId = account.data?.characters[0]?.characterId
  const character = useQuery({
    queryKey: ['character', characterId],
    queryFn: () =>
      unwrap(api.GET('/api/characters/{id}', { params: { path: { id: characterId! } } })),
    enabled: characterId != null,
  })

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['account', accountId] })
    void queryClient.invalidateQueries({ queryKey: ['character', characterId] })
    void queryClient.invalidateQueries({ queryKey: ['players'] })
  }

  const back = (
    <Anchor component={Link} to="/admin/players" size="sm">
      <Group gap={4}>
        <IconArrowLeft size={16} />
        {t('players:detail.back')}
      </Group>
    </Anchor>
  )

  if (account.isLoading || character.isLoading) {
    return (
      <Group justify="center" py="xl">
        <Loader />
      </Group>
    )
  }
  if (account.error || !account.data || character.error) {
    return (
      <Stack>
        {back}
        <ErrorAlert error={account.error ?? character.error} />
      </Stack>
    )
  }

  const acc = account.data
  const ch = character.data
  const muted = ch ? isMuted(ch.mutedUntil) : false
  const canModerate = can(me, 'Moderate')
  const canManage = can(me, 'ManageCharacters')
  const canViewLogs = can(me, 'ViewLogs')
  const formatClass = (cls: number) => formatProfession(cls, t, '—')

  return (
    <Stack>
      <Group justify="space-between" wrap="wrap">
        {back}
        {ch && (
          <Anchor component={Link} to={`/admin/maps/${ch.mapId}?x=${ch.x}&y=${ch.y}`} size="sm">
            <Group gap={4}>
              <IconMapPin size={15} />
              {t('players:detail.viewOnMap')}
            </Group>
          </Anchor>
        )}
      </Group>

      <Stack gap="sm">
        <Stack gap={4}>
          <Group gap="sm" wrap="wrap">
            <Title order={2}>{ch?.name ?? acc.username}</Title>
            {ch && (
              <Text c="dimmed" size="sm">
                #{ch.characterId}
              </Text>
            )}
            {ch && (
              <Badge color={ch.online ? 'green' : 'gray'}>
                {t(ch.online ? 'characters:status.online' : 'characters:status.offline')}
              </Badge>
            )}
            {ch?.dead && (
              <Badge color="red" variant="light">
                {t('characters:status.dead')}
              </Badge>
            )}
            {muted && ch?.mutedUntil && (
              <Badge color="orange" variant="light">
                {t('characters:status.mutedUntil', {
                  time: new Date(ch.mutedUntil).toLocaleString(i18n.language),
                })}
              </Badge>
            )}
            <Badge variant="light" color={accountStatusColor(acc.statusId)}>
              {t(`accounts:status.${acc.statusId}` as never)}
            </Badge>
            <Badge variant="light" color={authorityColor(acc.authorityId)}>
              {t(`common:authority.${acc.authorityId}` as never)}
            </Badge>
          </Group>
          <Text size="sm" c="dimmed">
            {t('players:detail.account')}: {acc.username} (#{acc.accountId})
            {ch && (
              <>
                {' '}· Lv.{ch.level} · {formatClass(ch.currentClass)}
                {ch.rebirths > 0 ? ` · RB ${ch.rebirths}` : ''} ·{' '}
                {ch.mapName ? `${ch.mapName} (#${ch.mapId})` : `#${ch.mapId}`} ({ch.x}, {ch.y})
              </>
            )}
          </Text>
        </Stack>

        <AccountActions account={acc} />
        {ch && (
          <CharacterActionsBar
            characterId={ch.characterId}
            characterName={ch.name}
            currentLevel={ch.level}
            currentMapId={ch.mapId}
            currentX={ch.x}
            currentY={ch.y}
            online={ch.online}
            muted={muted}
            canModerate={canModerate}
            canManage={canManage}
            onChanged={refresh}
          />
        )}
      </Stack>

      {ch && !ch.gameConnected && (
        <Alert color="yellow" icon={<IconInfoCircle size={18} />}>
          {t('characters:detail.gameOfflineNotice')}
        </Alert>
      )}
      {!ch && <Alert color="gray">{t('players:detail.noCharacter')}</Alert>}

      <Tabs defaultValue={ch ? 'profile' : 'account'} keepMounted={false}>
        <Tabs.List>
          {ch && (
            <>
              <Tabs.Tab value="profile">{t('characters:detail.tabs.profile')}</Tabs.Tab>
              <Tabs.Tab value="items">{t('characters:detail.tabs.items')}</Tabs.Tab>
              <Tabs.Tab value="skills">
                {t('characters:detail.tabs.skills')} ({ch.magics.length + ch.proficiencies.length})
              </Tabs.Tab>
              <Tabs.Tab value="relations">
                {t('characters:detail.tabs.relations')} (
                {ch.friends.length + ch.enemies.length + ch.tradePartners.length})
              </Tabs.Tab>
            </>
          )}
          <Tabs.Tab value="account">{t('players:detail.account')}</Tabs.Tab>
          <Tabs.Tab value="logins">
            {t('accounts:detail.logins')} ({acc.logins.length})
          </Tabs.Tab>
          {canViewLogs && <Tabs.Tab value="timeline">{t('characters:detail.tabs.timeline')}</Tabs.Tab>}
        </Tabs.List>

        {ch && <CharacterPanels ch={ch} canManage={canManage} onChanged={refresh} />}

        <Tabs.Panel value="account" pt="md">
          <Stack>
            <SimpleGrid cols={{ base: 1, sm: 4 }}>
              <Card withBorder>
                <Text size="xs" c="dimmed">
                  {t('accounts:detail.email')}
                </Text>
                <Text fw={500}>{acc.email || '—'}</Text>
              </Card>
              <Card withBorder>
                <Text size="xs" c="dimmed">
                  {t('accounts:detail.name')}
                </Text>
                <Text fw={500}>{acc.name || '—'}</Text>
              </Card>
              <Card withBorder>
                <Text size="xs" c="dimmed">
                  {t('accounts:detail.lastIp')}
                </Text>
                {acc.ipAddress ? (
                  <Anchor component={Link} to={ipLink(acc.ipAddress)} fw={500} title={t('accounts:detail.searchIp')}>
                    {acc.ipAddress}
                  </Anchor>
                ) : (
                  <Text fw={500}>—</Text>
                )}
              </Card>
              <Card withBorder>
                <Text size="xs" c="dimmed">
                  {t('accounts:detail.registered')}
                </Text>
                <Text fw={500}>{new Date(acc.registered).toLocaleString(i18n.language)}</Text>
              </Card>
            </SimpleGrid>

            <Card withBorder>
              <Title order={4} mb="sm">
                {t('accounts:detail.sameIp')} ({acc.sameIpAccounts.length})
              </Title>
              {acc.sameIpAccounts.length === 0 ? (
                <Text c="dimmed" size="sm">
                  {t('accounts:detail.noSameIp')}
                </Text>
              ) : (
                <ScrollArea>
                  <Table highlightOnHover striped>
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th>{t('accounts:column.id')}</Table.Th>
                        <Table.Th>{t('accounts:column.username')}</Table.Th>
                        <Table.Th>{t('accounts:column.status')}</Table.Th>
                        <Table.Th>{t('accounts:column.sharedIps')}</Table.Th>
                        <Table.Th>{t('accounts:column.registered')}</Table.Th>
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {acc.sameIpAccounts.map((other) => (
                        <Table.Tr
                          key={other.accountId}
                          style={{ cursor: 'pointer' }}
                          onClick={() => navigate(`/admin/players/${other.accountId}`)}
                        >
                          <Table.Td>#{other.accountId}</Table.Td>
                          <Table.Td>
                            <Text fw={600} size="sm">
                              {other.username}
                            </Text>
                          </Table.Td>
                          <Table.Td>
                            <Badge size="sm" variant="light" color={accountStatusColor(other.statusId)}>
                              {t(`accounts:status.${other.statusId}` as never)}
                            </Badge>
                          </Table.Td>
                          <Table.Td>{other.sharedIps.join(', ')}</Table.Td>
                          <Table.Td>{new Date(other.registered).toLocaleDateString(i18n.language)}</Table.Td>
                        </Table.Tr>
                      ))}
                    </Table.Tbody>
                  </Table>
                </ScrollArea>
              )}
            </Card>
          </Stack>
        </Tabs.Panel>

        <Tabs.Panel value="logins" pt="md">
          <Card withBorder>
            {acc.logins.length === 0 ? (
              <Text c="dimmed" size="sm">
                {t('accounts:detail.noLogins')}
              </Text>
            ) : (
              <ScrollArea>
                <Table striped>
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>{t('accounts:column.time')}</Table.Th>
                      <Table.Th>{t('accounts:column.ip')}</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {acc.logins.map((entry, idx) => (
                      <Table.Tr key={`${entry.timestamp}-${idx}`}>
                        <Table.Td>{new Date(entry.timestamp).toLocaleString(i18n.language)}</Table.Td>
                        <Table.Td>
                          <Anchor component={Link} to={ipLink(entry.ipAddress)} title={t('accounts:detail.searchIp')}>
                            {entry.ipAddress}
                          </Anchor>
                        </Table.Td>
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              </ScrollArea>
            )}
          </Card>
        </Tabs.Panel>

        {canViewLogs && (
          <Tabs.Panel value="timeline" pt="md">
            <LogTimeline accountId={acc.accountId} characterId={ch?.characterId} compact />
          </Tabs.Panel>
        )}
      </Tabs>
    </Stack>
  )
}
