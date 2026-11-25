import {
  Alert,
  Anchor,
  Badge,
  Card,
  Divider,
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
import { IconArrowLeft, IconInfoCircle, IconMapPin, IconUser } from '@tabler/icons-react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router'
import { api, unwrap, type Schemas } from '../../api/client'
import { can, useMe } from '../../app/auth'
import { ErrorAlert } from '../../app/ErrorAlert'
import { formatProfession } from '../../lib/gameMeta'
import { CharacterTimelineTab } from '../logs/CharacterTimelineTab'
import { CharacterActionsBar } from './CharacterActionsBar'
import { CharacterItemsView } from './CharacterItemsView'
import { isMuted, pkColor } from './characterMeta'

function StatRow({ label, value }: { label: string; value: string | number }) {
  return (
    <Group justify="space-between" gap="md">
      <Text size="sm" c="dimmed">
        {label}
      </Text>
      <Text size="sm" fw={500}>
        {value}
      </Text>
    </Group>
  )
}

function RelationsTable({
  title,
  entries,
  showSince,
  emptyLabel,
  formatClass,
  language,
  onSelect,
}: {
  title: string
  entries: Schemas['CharacterRelationEntry'][]
  showSince?: boolean
  emptyLabel: string
  formatClass: (cls: number) => string
  language: string
  onSelect: (id: number) => void
}) {
  const { t } = useTranslation('characters')
  return (
    <Card withBorder>
      <Title order={5} mb="xs">
        {title}
      </Title>
      {entries.length === 0 ? (
        <Text size="sm" c="dimmed">
          {emptyLabel}
        </Text>
      ) : (
        <ScrollArea>
          <Table highlightOnHover striped>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t('column.name')}</Table.Th>
                <Table.Th>{t('column.level')}</Table.Th>
                <Table.Th>{t('column.class')}</Table.Th>
                {showSince && <Table.Th>{t('column.since')}</Table.Th>}
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {entries.map((entry) => (
                <Table.Tr
                  key={entry.characterId}
                  style={{ cursor: 'pointer' }}
                  onClick={() => onSelect(entry.characterId)}
                >
                  <Table.Td>
                    <Text size="sm" fw={500}>
                      {entry.name}
                    </Text>
                  </Table.Td>
                  <Table.Td>{entry.level}</Table.Td>
                  <Table.Td>{formatClass(entry.currentClass)}</Table.Td>
                  {showSince && (
                    <Table.Td>
                      {entry.since ? new Date(entry.since).toLocaleString(language) : '—'}
                    </Table.Td>
                  )}
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </ScrollArea>
      )}
    </Card>
  )
}

export default function CharacterDetailPage() {
  const { id } = useParams()
  const characterId = Number(id)
  const { t, i18n } = useTranslation(['characters', 'items'])
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const me = useMe().data

  const detail = useQuery({
    queryKey: ['character', characterId],
    queryFn: () =>
      unwrap(api.GET('/api/characters/{id}', { params: { path: { id: characterId } } })),
    enabled: Number.isFinite(characterId) && characterId > 0,
  })

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['character', characterId] })
    void queryClient.invalidateQueries({ queryKey: ['characters'] })
  }

  const formatClass = (cls: number) => formatProfession(cls, t, '—')

  if (detail.isLoading) {
    return (
      <Group justify="center" py="xl">
        <Loader />
      </Group>
    )
  }

  if (detail.error || !detail.data) {
    return (
      <Stack>
        <Anchor component={Link} to="/admin/characters" size="sm">
          <Group gap={4}>
            <IconArrowLeft size={16} />
            {t('characters:detail.back')}
          </Group>
        </Anchor>
        <ErrorAlert error={detail.error} />
      </Stack>
    )
  }

  const ch = detail.data
  const muted = isMuted(ch.mutedUntil)
  const canModerate = can(me, 'Moderate')
  const canManage = can(me, 'ManageCharacters')
  const canViewLogs = can(me, 'ViewLogs')

  return (
    <Stack>
      <Group justify="space-between" wrap="wrap">
        <Anchor component={Link} to="/admin/characters" size="sm">
          <Group gap={4}>
            <IconArrowLeft size={16} />
            {t('characters:detail.back')}
          </Group>
        </Anchor>
        <Group gap="md">
          <Anchor component={Link} to={`/admin/accounts/${ch.accountId}`} size="sm">
            <Group gap={4}>
              <IconUser size={15} />
              {ch.accountUsername || `#${ch.accountId}`} ({t('characters:detail.viewAccount')})
            </Group>
          </Anchor>
          <Anchor
            component={Link}
            to={`/admin/maps/${ch.mapId}?x=${ch.x}&y=${ch.y}`}
            size="sm"
          >
            <Group gap={4}>
              <IconMapPin size={15} />
              {t('characters:detail.viewOnMap')}
            </Group>
          </Anchor>
        </Group>
      </Group>

      <Group justify="space-between" align="flex-start" wrap="wrap">
        <Stack gap={4}>
          <Group gap="sm" wrap="wrap">
            <Title order={2}>{ch.name}</Title>
            <Text c="dimmed" size="sm">
              #{ch.characterId}
            </Text>
            <Badge color={ch.online ? 'green' : 'gray'}>
              {t(ch.online ? 'characters:status.online' : 'characters:status.offline')}
            </Badge>
            {ch.dead && (
              <Badge color="red" variant="light">
                {t('characters:status.dead')}
              </Badge>
            )}
            {muted && ch.mutedUntil && (
              <Badge color="orange" variant="light">
                {t('characters:status.mutedUntil', {
                  time: new Date(ch.mutedUntil).toLocaleString(i18n.language),
                })}
              </Badge>
            )}
          </Group>
          <Text size="sm" c="dimmed">
            Lv.{ch.level} · {formatClass(ch.currentClass)}
            {ch.rebirths > 0 ? ` · RB ${ch.rebirths}` : ''} ·{' '}
            {ch.mapName ? `${ch.mapName} (#${ch.mapId})` : `#${ch.mapId}`} ({ch.x}, {ch.y})
          </Text>
        </Stack>

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
      </Group>

      {!ch.gameConnected && (
        <Alert color="yellow" icon={<IconInfoCircle size={18} />}>
          {t('characters:detail.gameOfflineNotice')}
        </Alert>
      )}

      <Tabs defaultValue="profile">
        <Tabs.List>
          <Tabs.Tab value="profile">{t('characters:detail.tabs.profile')}</Tabs.Tab>
          <Tabs.Tab value="items">{t('characters:detail.tabs.items')}</Tabs.Tab>
          <Tabs.Tab value="skills">
            {t('characters:detail.tabs.skills')} ({ch.magics.length + ch.proficiencies.length})
          </Tabs.Tab>
          <Tabs.Tab value="relations">
            {t('characters:detail.tabs.relations')} (
            {ch.friends.length + ch.enemies.length + ch.tradePartners.length})
          </Tabs.Tab>
          {canViewLogs && (
            <Tabs.Tab value="timeline">{t('characters:detail.tabs.timeline')}</Tabs.Tab>
          )}
        </Tabs.List>

        <Tabs.Panel value="profile" pt="md">
          <Stack>
            <SimpleGrid cols={{ base: 1, md: 2 }}>
              <Card withBorder>
                <Title order={4} mb="sm">
                  {t('characters:detail.stats')}
                </Title>
                <Stack gap={6}>
                  <StatRow
                    label={t('characters:detail.hp')}
                    value={ch.maxHealth ? `${ch.health} / ${ch.maxHealth}` : ch.health}
                  />
                  <StatRow
                    label={t('characters:detail.mp')}
                    value={ch.maxMana ? `${ch.mana} / ${ch.maxMana}` : ch.mana}
                  />
                  <Divider my={4} />
                  <StatRow label={t('characters:detail.strength')} value={ch.strength} />
                  <StatRow label={t('characters:detail.agility')} value={ch.agility} />
                  <StatRow label={t('characters:detail.vitality')} value={ch.vitality} />
                  <StatRow label={t('characters:detail.spirit')} value={ch.spirit} />
                  <StatRow label={t('characters:detail.attributePoints')} value={ch.attributePoints} />
                </Stack>
              </Card>

              <Card withBorder>
                <Title order={4} mb="sm">
                  {t('characters:detail.wealth')}
                </Title>
                <Stack gap={6}>
                  <StatRow
                    label={t('characters:detail.silver')}
                    value={ch.silver.toLocaleString(i18n.language)}
                  />
                  <StatRow
                    label={t('characters:detail.storedSilver')}
                    value={ch.storedSilver.toLocaleString(i18n.language)}
                  />
                  <StatRow
                    label={t('characters:detail.cp')}
                    value={ch.conquerPoints.toLocaleString(i18n.language)}
                  />
                  <Divider my={4} />
                  <Group justify="space-between">
                    <Text size="sm" c="dimmed">
                      {t('characters:detail.pkPoints')}
                    </Text>
                    <Badge size="sm" variant="light" color={pkColor(ch.killPoints)}>
                      {ch.killPoints}
                    </Badge>
                  </Group>
                  <StatRow
                    label={t('characters:detail.virtue')}
                    value={ch.virtue.toLocaleString(i18n.language)}
                  />
                  <StatRow label={t('characters:detail.vip')} value={ch.vipLevel} />
                </Stack>
              </Card>

              <Card withBorder>
                <Title order={4} mb="sm">
                  {t('characters:detail.classInfo')}
                </Title>
                <Stack gap={6}>
                  <StatRow
                    label={t('characters:detail.currentClass')}
                    value={formatClass(ch.currentClass)}
                  />
                  <StatRow
                    label={t('characters:detail.previousClass')}
                    value={formatClass(ch.previousClass)}
                  />
                  <StatRow
                    label={t('characters:detail.ancestorClass')}
                    value={formatClass(ch.ancestorClass)}
                  />
                  <StatRow label={t('characters:detail.rebirths')} value={ch.rebirths} />
                  <StatRow
                    label={t('characters:detail.experience')}
                    value={ch.experience.toLocaleString(i18n.language)}
                  />
                  <StatRow
                    label={t('characters:detail.spouse')}
                    value={!ch.spouse || ch.spouse === 'None' ? t('characters:detail.social.none') : ch.spouse}
                  />
                </Stack>
              </Card>

              <Card withBorder>
                <Title order={4} mb="sm">
                  {t('characters:detail.location')}
                </Title>
                <Stack gap={6}>
                  <StatRow
                    label={t('characters:column.location')}
                    value={`${ch.mapName ? `${ch.mapName} (#${ch.mapId})` : `#${ch.mapId}`} (${ch.x}, ${ch.y})`}
                  />
                  <StatRow
                    label={t('characters:detail.registered')}
                    value={new Date(ch.registered).toLocaleString(i18n.language)}
                  />
                  {ch.ipAddress && (
                    <StatRow label={t('characters:detail.ipAddress')} value={ch.ipAddress} />
                  )}
                  {ch.loggedInAt && (
                    <StatRow
                      label={t('characters:detail.loggedInAt')}
                      value={new Date(ch.loggedInAt).toLocaleString(i18n.language)}
                    />
                  )}
                </Stack>
              </Card>
            </SimpleGrid>

            <Title order={4}>{t('characters:detail.social.title')}</Title>
            <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }}>
              <Card withBorder>
                <Title order={5} mb="xs">
                  {t('characters:detail.social.guild')}
                </Title>
                {ch.guild ? (
                  <Stack gap={4}>
                    <Text fw={600}>
                      {ch.guild.name} <Text span size="xs" c="dimmed">#{ch.guild.syndicateId}</Text>
                    </Text>
                    <StatRow label={t('characters:detail.social.rank')} value={ch.guild.rank} />
                    <StatRow
                      label={t('characters:detail.social.donation')}
                      value={ch.guild.silverDonation.toLocaleString(i18n.language)}
                    />
                    <StatRow
                      label={t('characters:detail.social.cpDonation')}
                      value={ch.guild.conquerPointsDonation.toLocaleString(i18n.language)}
                    />
                  </Stack>
                ) : (
                  <Text size="sm" c="dimmed">
                    {t('characters:detail.social.none')}
                  </Text>
                )}
              </Card>

              <Card withBorder>
                <Title order={5} mb="xs">
                  {t('characters:detail.social.family')}
                </Title>
                {ch.family ? (
                  <Stack gap={4}>
                    <Text fw={600}>
                      {ch.family.name} <Text span size="xs" c="dimmed">#{ch.family.familyId}</Text>
                    </Text>
                    <StatRow label={t('characters:detail.social.rank')} value={ch.family.rank} />
                    <StatRow
                      label={t('characters:detail.social.donation')}
                      value={ch.family.donation.toLocaleString(i18n.language)}
                    />
                  </Stack>
                ) : (
                  <Text size="sm" c="dimmed">
                    {t('characters:detail.social.none')}
                  </Text>
                )}
              </Card>

              <Card withBorder>
                <Title order={5} mb="xs">
                  {t('characters:detail.social.nobility')}
                </Title>
                {ch.nobility ? (
                  <StatRow
                    label={t('characters:detail.social.donation')}
                    value={ch.nobility.donation.toLocaleString(i18n.language)}
                  />
                ) : (
                  <Text size="sm" c="dimmed">
                    {t('characters:detail.social.none')}
                  </Text>
                )}
              </Card>

              <Card withBorder>
                <Title order={5} mb="xs">
                  {t('characters:detail.social.flowers')}
                </Title>
                {ch.flowers ? (
                  <Stack gap={4}>
                    <StatRow
                      label={t('characters:detail.social.roses')}
                      value={`${ch.flowers.redRoses} (+${ch.flowers.redRosesToday})`}
                    />
                    <StatRow
                      label={t('characters:detail.social.lilies')}
                      value={`${ch.flowers.lilies} (+${ch.flowers.liliesToday})`}
                    />
                    <StatRow
                      label={t('characters:detail.social.orchids')}
                      value={`${ch.flowers.orchids} (+${ch.flowers.orchidsToday})`}
                    />
                    <StatRow
                      label={t('characters:detail.social.tulips')}
                      value={`${ch.flowers.tulips} (+${ch.flowers.tulipsToday})`}
                    />
                  </Stack>
                ) : (
                  <Text size="sm" c="dimmed">
                    {t('characters:detail.social.none')}
                  </Text>
                )}
              </Card>

              <Card withBorder>
                <Title order={5} mb="xs">
                  {t('characters:detail.social.arena')}
                </Title>
                {ch.arena ? (
                  <Stack gap={4}>
                    <StatRow
                      label={t('characters:detail.social.points')}
                      value={ch.arena.points}
                    />
                    <StatRow
                      label={t('characters:detail.social.today')}
                      value={`${ch.arena.winsToday}W / ${ch.arena.lossesToday}L`}
                    />
                    <StatRow
                      label={t('characters:detail.social.total')}
                      value={`${ch.arena.winsTotal}W / ${ch.arena.lossesTotal}L`}
                    />
                    <StatRow
                      label={t('characters:detail.social.honor')}
                      value={`${ch.arena.honorPoints} (${ch.arena.honorTotal})`}
                    />
                  </Stack>
                ) : (
                  <Text size="sm" c="dimmed">
                    {t('characters:detail.social.none')}
                  </Text>
                )}
              </Card>
            </SimpleGrid>
          </Stack>
        </Tabs.Panel>

        <Tabs.Panel value="items" pt="md">
          <CharacterItemsView
            characterId={ch.characterId}
            characterName={ch.name}
            equipment={ch.equipment}
            inventory={ch.inventory}
            warehouses={ch.warehouses}
            detained={ch.detained}
            canManage={canManage}
            onChanged={refresh}
          />
        </Tabs.Panel>

        <Tabs.Panel value="skills" pt="md">
          <SimpleGrid cols={{ base: 1, lg: 2 }}>
            <Card withBorder>
              <Title order={4} mb="sm">
                {t('characters:detail.skills.magics', { count: ch.magics.length })}
              </Title>
              {ch.magics.length === 0 ? (
                <Text size="sm" c="dimmed">
                  {t('characters:detail.skills.noMagics')}
                </Text>
              ) : (
                <ScrollArea>
                  <Table striped>
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th>{t('characters:column.skill')}</Table.Th>
                        <Table.Th>{t('characters:column.type')}</Table.Th>
                        <Table.Th>{t('characters:column.level')}</Table.Th>
                        <Table.Th>{t('characters:column.experience')}</Table.Th>
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {ch.magics.map((m) => (
                        <Table.Tr key={m.type}>
                          <Table.Td>{m.name || `#${m.type}`}</Table.Td>
                          <Table.Td>{m.type}</Table.Td>
                          <Table.Td>{m.level}</Table.Td>
                          <Table.Td>{m.experience.toLocaleString(i18n.language)}</Table.Td>
                        </Table.Tr>
                      ))}
                    </Table.Tbody>
                  </Table>
                </ScrollArea>
              )}
            </Card>

            <Card withBorder>
              <Title order={4} mb="sm">
                {t('characters:detail.skills.proficiencies', { count: ch.proficiencies.length })}
              </Title>
              {ch.proficiencies.length === 0 ? (
                <Text size="sm" c="dimmed">
                  {t('characters:detail.skills.noProficiencies')}
                </Text>
              ) : (
                <ScrollArea>
                  <Table striped>
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th>{t('characters:column.type')}</Table.Th>
                        <Table.Th>{t('characters:column.level')}</Table.Th>
                        <Table.Th>{t('characters:column.experience')}</Table.Th>
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {ch.proficiencies.map((p) => (
                        <Table.Tr key={p.type}>
                          <Table.Td>{p.type}</Table.Td>
                          <Table.Td>{p.level}</Table.Td>
                          <Table.Td>{p.experience.toLocaleString(i18n.language)}</Table.Td>
                        </Table.Tr>
                      ))}
                    </Table.Tbody>
                  </Table>
                </ScrollArea>
              )}
            </Card>
          </SimpleGrid>
        </Tabs.Panel>

        <Tabs.Panel value="relations" pt="md">
          <SimpleGrid cols={{ base: 1, lg: 3 }}>
            <RelationsTable
              title={t('characters:detail.relations.friends', { count: ch.friends.length })}
              entries={ch.friends}
              emptyLabel={t('characters:detail.relations.none')}
              formatClass={formatClass}
              language={i18n.language}
              onSelect={(cid) => navigate(`/admin/characters/${cid}`)}
            />
            <RelationsTable
              title={t('characters:detail.relations.enemies', { count: ch.enemies.length })}
              entries={ch.enemies}
              showSince
              emptyLabel={t('characters:detail.relations.none')}
              formatClass={formatClass}
              language={i18n.language}
              onSelect={(cid) => navigate(`/admin/characters/${cid}`)}
            />
            <RelationsTable
              title={t('characters:detail.relations.tradePartners', {
                count: ch.tradePartners.length,
              })}
              entries={ch.tradePartners}
              showSince
              emptyLabel={t('characters:detail.relations.none')}
              formatClass={formatClass}
              language={i18n.language}
              onSelect={(cid) => navigate(`/admin/characters/${cid}`)}
            />
          </SimpleGrid>
        </Tabs.Panel>

        {canViewLogs && (
          <Tabs.Panel value="timeline" pt="md">
            <CharacterTimelineTab characterId={ch.characterId} accountId={ch.accountId} />
          </Tabs.Panel>
        )}
      </Tabs>
    </Stack>
  )
}
